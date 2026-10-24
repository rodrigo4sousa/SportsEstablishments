using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Bookings;

/// <summary>US11: the owner's calendar of one club for up to seven days, through the T02 mechanism.</summary>
public sealed class ClubCalendarTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string CalendarPath(Guid clubId, string? from, string? to, string? includeCancelled = null)
    {
        var query = new List<string>();
        if (from is not null)
        {
            query.Add($"from={from}");
        }

        if (to is not null)
        {
            query.Add($"to={to}");
        }

        if (includeCancelled is not null)
        {
            query.Add($"includeCancelled={includeCancelled}");
        }

        return $"/api/manage/clubs/{clubId}/calendar" + (query.Count == 0 ? string.Empty : "?" + string.Join('&', query));
    }

    private static async Task<HttpResponseMessage> GetAsync(WebApplicationFactory<Program> app, string path, string? accessToken)
    {
        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (accessToken is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, accessToken));
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = await JsonAsync(response, status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        return problem;
    }

    /// <summary>An owner with a session and a club, and a pitch open every day 09:00-13:00 unless told otherwise.</summary>
    private async Task<(TestClub Club, TestPitch Pitch, string Token)> OwnerWithClubAsync(params (DayOfWeek Day, string Opens, string Closes)[] open)
    {
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner, "Dono do Clube");
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id, "Campo 1", open: open.Length == 0 ? TestVenues.MondayMorning : open);
        return (club, pitch, tokens.AccessToken);
    }

    private async Task<Guid> BookAsync(Guid pitchId, string start, string end, string status = "Confirmed", AppUser? player = null)
    {
        var booking = await factory.AddBookingAsync(pitchId, TestVenues.Utc(start), TestVenues.Utc(end), player);
        if (status != "Confirmed")
        {
            await factory.SetBookingStatusAsync(booking.Id, status);
        }

        return booking.Id;
    }

    private static List<Guid> BookingIds(JsonElement calendar) =>
        calendar.GetProperty("bookings").EnumerateArray().Select(booking => booking.GetProperty("id").GetGuid()).ToList();

    private static List<Guid> PitchIds(JsonElement calendar) =>
        calendar.GetProperty("pitches").EnumerateArray().Select(pitch => pitch.GetProperty("id").GetGuid()).ToList();

    // ---- The range

    [Theory]
    [InlineData(null, "2026-05-10", "from", "Indica a data inicial.")]
    [InlineData("2026-05-04", null, "to", "Indica a data final.")]
    [InlineData("04/05/2026", "2026-05-10", "from", "Indica a data no formato AAAA-MM-DD, por exemplo 2026-10-25.")]
    [InlineData("2026-05-04", "2026-5-10", "to", "Indica a data no formato AAAA-MM-DD, por exemplo 2026-10-25.")]
    [InlineData("2026-02-30", "2026-03-02", "from", "Indica a data no formato AAAA-MM-DD, por exemplo 2026-10-25.")]
    [InlineData("2026-05-10", "2026-05-09", "to", "A data final não pode ser anterior à inicial.")]
    [InlineData("2026-05-04", "2026-05-11", "to", "O calendário mostra no máximo 7 dias de cada vez.")]
    [InlineData("2026-01-01", "2026-12-31", "to", "O calendário mostra no máximo 7 dias de cada vez.")]
    [InlineData("0001-01-01", "0001-01-02", "from", "A data tem de estar entre 2000 e 2100.")]
    [InlineData("2026-05-04", "9999-12-31", "to", "A data tem de estar entre 2000 e 2100.")]
    public async Task A_bad_range_is_400_on_the_field_with_a_message_in_portuguese(string? from, string? to, string field, string message)
    {
        var (club, _, token) = await OwnerWithClubAsync();

        var problem = await ProblemAsync(await GetAsync(factory, CalendarPath(club.Id, from, to), token), HttpStatusCode.BadRequest, "validation_failed");

        problem.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()).ShouldContain(message);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("1")]
    [InlineData("maybe")]
    public async Task A_wrong_includeCancelled_is_400_on_its_field(string value)
    {
        var (club, _, token) = await OwnerWithClubAsync();

        var problem = await ProblemAsync(
            await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-10", value), token), HttpStatusCode.BadRequest, "validation_failed");

        problem.GetProperty("errors").GetProperty("includeCancelled").EnumerateArray().Single().GetString().ShouldBe("Indica «true» ou «false».");
    }

    [Theory]
    [InlineData("2026-05-04", "2026-05-04", 1)]
    [InlineData("2026-05-04", "2026-05-10", 7)] // a week, both ends counted
    [InlineData("2026-12-27", "2027-01-02", 7)] // across a year
    public async Task One_to_seven_days_are_accepted_and_the_response_has_one_entry_per_day(string from, string to, int days)
    {
        var (club, _, token) = await OwnerWithClubAsync();

        var calendar = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, from, to), token), HttpStatusCode.OK);

        calendar.GetProperty("from").GetString().ShouldBe(from);
        calendar.GetProperty("to").GetString().ShouldBe(to);
        calendar.GetProperty("pitches").EnumerateArray().Single().GetProperty("days").GetArrayLength().ShouldBe(days);
    }

    [Fact]
    public async Task The_owner_may_look_at_the_past_and_the_far_future_the_booking_window_does_not_apply()
    {
        var (club, _, token) = await OwnerWithClubAsync();

        using var past = await GetAsync(factory, CalendarPath(club.Id, "2024-03-04", "2024-03-10"), token);
        using var future = await GetAsync(factory, CalendarPath(club.Id, "2031-03-03", "2031-03-09"), token);

        past.StatusCode.ShouldBe(HttpStatusCode.OK);
        future.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ---- Contents

    [Fact]
    public async Task The_calendar_lists_the_pitches_with_each_days_opening_hours_the_bookings_with_the_players_name_and_the_blocks()
    {
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner, "Dono do Clube");
        var club = await factory.CreateClubAsync(owner.Id);
        // Padel: Monday and Wednesday, 60 minutes. Ténis: closed on Monday, Tuesday 10:00-24:00 (midnight), 30 minutes.
        var padel = await factory.CreatePitchAsync(
            club.Id, "Padel 1", slotMinutes: 60, open: [(DayOfWeek.Monday, "09:00", "13:00"), (DayOfWeek.Wednesday, "14:30", "20:00")]);
        var tennis = await factory.CreatePitchAsync(club.Id, "Ténis 1", Domain.Pitches.Sport.Tennis, 12m, 30, (DayOfWeek.Tuesday, "10:00", "00:00"));
        var rita = (await factory.CreateSignedInPlayerAsync("Rita Jogadora")).User;
        var padelBooking = await BookAsync(padel.Id, "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z", player: rita);
        var blockId = await factory.AddBlockAsync(tennis.Id, TestVenues.Utc("2026-05-05T09:00:00Z"), TestVenues.Utc("2026-05-05T10:00:00Z"));

        using var response = await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-10"), tokens.AccessToken);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var calendar = JsonDocument.Parse(text).RootElement;

        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        var pitches = calendar.GetProperty("pitches").EnumerateArray().ToList();
        pitches.Select(pitch => pitch.GetProperty("name").GetString()).ShouldBe(["Padel 1", "Ténis 1"]);
        var padelJson = pitches[0];
        padelJson.GetProperty("id").GetGuid().ShouldBe(padel.Id);
        padelJson.GetProperty("sport").GetString().ShouldBe("padel");
        padelJson.GetProperty("status").GetString().ShouldBe("active");
        padelJson.GetProperty("slotDurationMinutes").GetInt32().ShouldBe(60);
        var padelDays = padelJson.GetProperty("days").EnumerateArray().Select(day => (day.GetProperty("date").GetString(), Hours(day))).ToList();
        padelDays.ShouldBe(
        [
            ("2026-05-04", "09:00-13:00"), ("2026-05-05", "closed"), ("2026-05-06", "14:30-20:00"),
            ("2026-05-07", "closed"), ("2026-05-08", "closed"), ("2026-05-09", "closed"), ("2026-05-10", "closed"),
        ]);
        pitches[1].GetProperty("slotDurationMinutes").GetInt32().ShouldBe(30);
        pitches[1].GetProperty("days").EnumerateArray().Select(Hours).ShouldBe(["closed", "10:00-00:00", "closed", "closed", "closed", "closed", "closed"]);

        var booking = calendar.GetProperty("bookings").EnumerateArray().Single();
        booking.GetProperty("id").GetGuid().ShouldBe(padelBooking);
        booking.GetProperty("pitchId").GetGuid().ShouldBe(padel.Id);
        booking.GetProperty("startsAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc("2026-05-04T08:00:00Z"));
        booking.GetProperty("endsAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc("2026-05-04T09:00:00Z"));
        booking.GetProperty("status").GetString().ShouldBe("confirmed");
        booking.GetProperty("price").GetDecimal().ShouldBe(20m);
        booking.GetProperty("playerName").GetString().ShouldBe("Rita Jogadora");

        var block = calendar.GetProperty("blocks").EnumerateArray().Single();
        block.GetProperty("id").GetGuid().ShouldBe(blockId);
        block.GetProperty("pitchId").GetGuid().ShouldBe(tennis.Id);
        block.GetProperty("startsAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc("2026-05-05T09:00:00Z"));
        block.GetProperty("endsAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc("2026-05-05T10:00:00Z"));
        block.GetProperty("reason").GetString().ShouldBe("Manutenção");

        // Only the name of the player: no email, no account id, no phone.
        text.ShouldNotContain(rita.Email!);
        text.ShouldNotContain(rita.Id.ToString());
        text.ShouldNotContain(rita.PhoneNumber ?? "no-phone-number");
        booking.EnumerateObject().Select(property => property.Name).ShouldBe(["id", "pitchId", "startsAt", "endsAt", "status", "price", "playerName", "createdByClub"]);
    }

    private static string Hours(JsonElement day) =>
        day.GetProperty("opens").ValueKind == JsonValueKind.Null ? "closed" : $"{day.GetProperty("opens").GetString()}-{day.GetProperty("closes").GetString()}";

    [Fact]
    public async Task Bookings_and_blocks_come_in_time_order()
    {
        var (club, pitch, token) = await OwnerWithClubAsync();
        var late = await BookAsync(pitch.Id, "2026-05-04T11:00:00Z", "2026-05-04T12:00:00Z");
        var early = await BookAsync(pitch.Id, "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z");
        var lateBlock = await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-04T10:00:00Z"), TestVenues.Utc("2026-05-04T10:30:00Z"));
        var earlyBlock = await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-04T09:00:00Z"), TestVenues.Utc("2026-05-04T09:30:00Z"));

        var calendar = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-04"), token), HttpStatusCode.OK);

        BookingIds(calendar).ShouldBe([early, late]);
        calendar.GetProperty("blocks").EnumerateArray().Select(block => block.GetProperty("id").GetGuid()).ShouldBe([earlyBlock, lateBlock]);
    }

    [Fact]
    public async Task Only_what_overlaps_the_range_is_returned_and_never_another_clubs_bookings()
    {
        var (club, pitch, token) = await OwnerWithClubAsync();
        var inside = await BookAsync(pitch.Id, "2026-05-05T08:00:00Z", "2026-05-05T09:00:00Z");
        var before = await BookAsync(pitch.Id, "2026-05-03T08:00:00Z", "2026-05-03T09:00:00Z");
        var after = await BookAsync(pitch.Id, "2026-05-07T08:00:00Z", "2026-05-07T09:00:00Z");
        await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-03T08:00:00Z"), TestVenues.Utc("2026-05-03T09:00:00Z"));
        var (otherClub, otherPitch, _) = await OwnerWithClubAsync();
        var elsewhere = await BookAsync(otherPitch.Id, "2026-05-05T08:00:00Z", "2026-05-05T09:00:00Z");
        await factory.AddBlockAsync(otherPitch.Id, TestVenues.Utc("2026-05-05T10:00:00Z"), TestVenues.Utc("2026-05-05T11:00:00Z"));

        var calendar = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-06"), token), HttpStatusCode.OK);

        BookingIds(calendar).ShouldBe([inside]);
        BookingIds(calendar).ShouldNotContain(before);
        BookingIds(calendar).ShouldNotContain(after);
        BookingIds(calendar).ShouldNotContain(elsewhere);
        calendar.GetProperty("blocks").GetArrayLength().ShouldBe(0);
        PitchIds(calendar).ShouldBe([pitch.Id]);
        otherClub.Id.ShouldNotBe(club.Id);
    }

    [Fact]
    public async Task The_range_is_in_the_clubs_local_dates_and_a_booking_that_spans_local_midnight_is_on_both_days()
    {
        // Summer: Lisbon is UTC+1, so local midnight between 4 and 5 May is 2026-05-04T23:00Z.
        var (club, pitch, token) = await OwnerWithClubAsync();
        var lateOnFourth = await BookAsync(pitch.Id, "2026-05-04T21:30:00Z", "2026-05-04T22:30:00Z"); // 22:30-23:30 on the 4th
        var spanning = await BookAsync(pitch.Id, "2026-05-04T22:30:00Z", "2026-05-04T23:30:00Z"); // 23:30-00:30
        var earlyOnFifth = await BookAsync(pitch.Id, "2026-05-04T23:30:00Z", "2026-05-05T00:30:00Z"); // 00:30-01:30 on the 5th

        var fourth = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-04"), token), HttpStatusCode.OK);
        var fifth = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-05", "2026-05-05"), token), HttpStatusCode.OK);

        BookingIds(fourth).ShouldBe([lateOnFourth, spanning]);
        BookingIds(fifth).ShouldBe([spanning, earlyOnFifth]);
    }

    [Fact]
    public async Task The_day_the_clocks_go_back_is_25_hours_and_the_repeated_hour_is_in_it()
    {
        // 25 October 2026: Lisbon midnight is 23:00Z on the 24th; at 02:00 WEST the clocks go back to 01:00 WET; the next
        // midnight is 00:00Z on the 26th. So the local day 25 is [2026-10-24T23:00Z, 2026-10-26T00:00Z).
        var (club, pitch, token) = await OwnerWithClubAsync((DayOfWeek.Sunday, "09:00", "13:00"));
        var endsAtMidnight = await BookAsync(pitch.Id, "2026-10-24T22:00:00Z", "2026-10-24T23:00:00Z"); // 23:00-00:00 on the 24th: out
        var firstMinute = await BookAsync(pitch.Id, "2026-10-24T23:00:00Z", "2026-10-25T00:00:00Z"); // 00:00-01:00 WEST: in
        var firstOneThirty = await BookAsync(pitch.Id, "2026-10-25T00:30:00Z", "2026-10-25T01:00:00Z"); // 01:30 WEST: in
        var secondOneThirty = await BookAsync(pitch.Id, "2026-10-25T01:30:00Z", "2026-10-25T02:00:00Z"); // 01:30 WET: in
        var lastHour = await BookAsync(pitch.Id, "2026-10-25T23:00:00Z", "2026-10-26T00:00:00Z"); // 23:00-00:00 WET: in
        var nextDay = await BookAsync(pitch.Id, "2026-10-26T00:00:00Z", "2026-10-26T01:00:00Z"); // 00:00 on the 26th: out

        var calendar = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-10-25", "2026-10-25"), token), HttpStatusCode.OK);

        BookingIds(calendar).ShouldBe([firstMinute, firstOneThirty, secondOneThirty, lastHour]);
        BookingIds(calendar).ShouldNotContain(endsAtMidnight);
        BookingIds(calendar).ShouldNotContain(nextDay);
        // The pitch is open on this Sunday: the hours are the local ones whatever the length of the day.
        calendar.GetProperty("pitches").EnumerateArray().Single().GetProperty("days").EnumerateArray().Single().GetProperty("opens").GetString().ShouldBe("09:00");
    }

    [Fact]
    public async Task A_week_across_the_clock_change_has_each_day_in_local_time()
    {
        var (club, pitch, token) = await OwnerWithClubAsync((DayOfWeek.Saturday, "09:00", "13:00"), (DayOfWeek.Sunday, "10:00", "12:00"));
        var saturday = await BookAsync(pitch.Id, "2026-10-24T08:00:00Z", "2026-10-24T09:00:00Z");
        var sunday = await BookAsync(pitch.Id, "2026-10-25T10:00:00Z", "2026-10-25T11:00:00Z");

        var calendar = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-10-24", "2026-10-30"), token), HttpStatusCode.OK);

        BookingIds(calendar).ShouldBe([saturday, sunday]);
        var days = calendar.GetProperty("pitches").EnumerateArray().Single().GetProperty("days").EnumerateArray().Select(Hours).ToList();
        days.ShouldBe(["09:00-13:00", "10:00-12:00", "closed", "closed", "closed", "closed", "closed"]);
    }

    // ---- Cancelled bookings

    [Fact]
    public async Task Cancelled_bookings_are_hidden_by_default_and_shown_with_the_flag_and_a_no_show_is_not_a_cancellation()
    {
        var (club, pitch, token) = await OwnerWithClubAsync();
        var confirmed = await BookAsync(pitch.Id, "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z");
        var noShow = await BookAsync(pitch.Id, "2026-05-04T09:00:00Z", "2026-05-04T10:00:00Z", "NoShow");
        var byPlayer = await BookAsync(pitch.Id, "2026-05-04T10:00:00Z", "2026-05-04T11:00:00Z", "CancelledByPlayer");
        var byClub = await BookAsync(pitch.Id, "2026-05-04T11:00:00Z", "2026-05-04T12:00:00Z", "CancelledByClub");

        var byDefault = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-04"), token), HttpStatusCode.OK);
        var explicitFalse = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-04", "false"), token), HttpStatusCode.OK);
        var all = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-04", "true"), token), HttpStatusCode.OK);
        var upperCase = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-04", "True"), token), HttpStatusCode.OK);

        BookingIds(byDefault).ShouldBe([confirmed, noShow]);
        BookingIds(explicitFalse).ShouldBe([confirmed, noShow]);
        BookingIds(all).ShouldBe([confirmed, noShow, byPlayer, byClub]);
        BookingIds(upperCase).ShouldBe([confirmed, noShow, byPlayer, byClub]);
        all.GetProperty("bookings").EnumerateArray().Select(booking => booking.GetProperty("status").GetString())
            .ShouldBe(["confirmed", "no_show", "cancelled_by_player", "cancelled_by_club"]);
    }

    // ---- Inactive pitches

    [Fact]
    public async Task An_inactive_pitch_is_listed_only_if_it_has_bookings_in_the_range()
    {
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner, "Dono do Clube");
        var club = await factory.CreateClubAsync(owner.Id);
        var active = await factory.CreatePitchAsync(club.Id, "A ativo");
        var withBooking = await factory.CreatePitchAsync(club.Id, "B com reserva");
        var withoutBooking = await factory.CreatePitchAsync(club.Id, "C sem reserva");
        var bookedElsewhereInTime = await factory.CreatePitchAsync(club.Id, "D reserva noutra semana");
        await BookAsync(withBooking.Id, "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z");
        await BookAsync(bookedElsewhereInTime.Id, "2026-06-01T08:00:00Z", "2026-06-01T09:00:00Z");
        foreach (var pitch in new[] { withBooking, withoutBooking, bookedElsewhereInTime })
        {
            await factory.DeactivatePitchAsync(pitch.Id);
        }

        var calendar = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-10"), tokens.AccessToken), HttpStatusCode.OK);

        PitchIds(calendar).ShouldBe([active.Id, withBooking.Id]);
        calendar.GetProperty("pitches").EnumerateArray().Select(pitch => pitch.GetProperty("status").GetString()).ShouldBe(["active", "inactive"]);
    }

    [Fact]
    public async Task An_inactive_pitch_whose_only_booking_is_cancelled_is_listed_only_when_cancelled_bookings_are()
    {
        var (club, pitch, token) = await OwnerWithClubAsync();
        await BookAsync(pitch.Id, "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z", "CancelledByClub");
        await factory.DeactivatePitchAsync(pitch.Id);

        var hidden = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-10"), token), HttpStatusCode.OK);
        var shown = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-10", "true"), token), HttpStatusCode.OK);

        PitchIds(hidden).ShouldBeEmpty();
        PitchIds(shown).ShouldBe([pitch.Id]);
    }

    [Fact]
    public async Task The_blocks_of_an_inactive_pitch_that_is_listed_are_listed_too()
    {
        var (club, pitch, token) = await OwnerWithClubAsync();
        await BookAsync(pitch.Id, "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z");
        var blockId = await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-04T10:00:00Z"), TestVenues.Utc("2026-05-04T11:00:00Z"));
        await factory.DeactivatePitchAsync(pitch.Id);

        var calendar = await JsonAsync(await GetAsync(factory, CalendarPath(club.Id, "2026-05-04", "2026-05-04"), token), HttpStatusCode.OK);

        calendar.GetProperty("blocks").EnumerateArray().Single().GetProperty("id").GetGuid().ShouldBe(blockId);
    }

    // ---- Who may ask

    [Fact]
    public async Task Another_owners_club_is_404_club_not_found_whatever_the_query_says()
    {
        var (club, _, _) = await OwnerWithClubAsync();
        var (_, intruder) = await factory.CreateSignedInAsync(Profiles.Owner);

        foreach (var path in new[]
                 {
                     CalendarPath(club.Id, "2026-05-04", "2026-05-10"),
                     CalendarPath(club.Id, null, null), // would be a 400 for the owner: the club comes first
                     CalendarPath(club.Id, "2026-05-10", "2026-01-01", "maybe"),
                 })
        {
            await ProblemAsync(await GetAsync(factory, path, intruder.AccessToken), HttpStatusCode.NotFound, "club_not_found");
        }
    }

    [Fact]
    public async Task An_unknown_club_is_the_same_404()
    {
        var (_, owner) = await factory.CreateSignedInAsync(Profiles.Owner);

        await ProblemAsync(await GetAsync(factory, CalendarPath(Guid.CreateVersion7(), "2026-05-04", "2026-05-10"), owner.AccessToken), HttpStatusCode.NotFound, "club_not_found");
    }

    [Fact]
    public async Task Players_and_admins_get_403_and_visitors_401()
    {
        var (club, _, _) = await OwnerWithClubAsync();
        var path = CalendarPath(club.Id, "2026-05-04", "2026-05-10");
        var player = await factory.CreateSignedInPlayerAsync();
        var admin = await factory.CreateSignedInAsync(Profiles.Admin);

        await ProblemAsync(await GetAsync(factory, path, player.Tokens.AccessToken), HttpStatusCode.Forbidden, "forbidden");
        await ProblemAsync(await GetAsync(factory, path, admin.Tokens.AccessToken), HttpStatusCode.Forbidden, "forbidden");
        await ProblemAsync(await GetAsync(factory, path, accessToken: null), HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    public async Task The_calendar_follows_the_fake_clock_not_the_range_it_does_not_depend_on_today()
    {
        // The same range answers the same whatever "today" is: nothing in it is relative to now.
        var (club, pitch, token) = await OwnerWithClubAsync();
        var booking = await BookAsync(pitch.Id, "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z");
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse("2031-01-01T10:00:00Z", CultureInfo.InvariantCulture)))));

        var calendar = await JsonAsync(await GetAsync(app, CalendarPath(club.Id, "2026-05-04", "2026-05-10"), token), HttpStatusCode.OK);

        BookingIds(calendar).ShouldBe([booking]);
    }
}
