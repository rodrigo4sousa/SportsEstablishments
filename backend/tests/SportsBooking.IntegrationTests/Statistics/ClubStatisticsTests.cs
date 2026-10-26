using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Domain.Accounts;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Statistics;

/// <summary>
/// US14: occupation and revenue for the owner, on hand-computed data. The tests share the assembly's database (every club is new) and a
/// clock on Wednesday 13 May 2026; sessions come from the shared app.
/// </summary>
public sealed class ClubStatisticsTests(ApiFactory factory)
{
    private const string Now = "2026-05-13T10:00:00Z";

    // Monday 4 to Sunday 10 May 2026, Lisbon (WEST, UTC+1): from 3 May 23:00 UTC to 10 May 23:00 UTC.
    private const string Week = "from=2026-05-04&to=2026-05-10";

    private static readonly (DayOfWeek, string, string)[] EveryDayAllDay = Enum.GetValues<DayOfWeek>().Select(day => (day, "00:00", "00:00")).ToArray();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> AppAt(string utcNow = Now) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private static async Task<HttpResponseMessage> GetAsync(WebApplicationFactory<Program> app, string path, string token)
    {
        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path).WithCookies((SessionCookies.AccessToken, token));
        return await client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static void ShouldHave(JsonElement figures, decimal booked, decimal open, decimal occupation, decimal revenue, int confirmed, int noShows)
    {
        figures.GetProperty("bookedHours").GetDecimal().ShouldBe(booked);
        figures.GetProperty("openHours").GetDecimal().ShouldBe(open);
        figures.GetProperty("occupation").GetDecimal().ShouldBe(occupation);
        figures.GetProperty("revenue").GetDecimal().ShouldBe(revenue);
        figures.GetProperty("confirmedBookings").GetInt32().ShouldBe(confirmed);
        figures.GetProperty("noShows").GetInt32().ShouldBe(noShows);
    }

    private static JsonElement PitchNamed(JsonElement statistics, string name) =>
        statistics.GetProperty("pitches").EnumerateArray().Single(pitch => pitch.GetProperty("name").GetString() == name);

    private sealed record Venue(Guid OwnerId, string Token, TestClub Club, TestPitch Hourly, TestPitch NinetyMinutes);

    /// <summary>
    /// Pitch «A» (60-minute slots) is open on Mondays 09:00-13:00: 4 open hours in the week. Its bookings: one confirmed 1 h, one
    /// no-show 1 h, one cancelled 1 h (left out), and two that cross the week's edges by 30 minutes each (clipped to 30 minutes, priced
    /// in full). Pitch «B» (90-minute slots) is open Mondays and Tuesdays 09:00-13:00: 8 open hours, one confirmed 90-minute booking.
    /// Every booking costs 20 €. A booking outside the week is not counted at all.
    /// </summary>
    private async Task<Venue> VenueAsync()
    {
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner, "Dona Clube");
        var club = await factory.CreateClubAsync(owner.Id);
        var hourly = await factory.CreatePitchAsync(club.Id, "A");
        var ninety = await factory.CreatePitchAsync(club.Id, "B", slotMinutes: 90, open: [(DayOfWeek.Monday, "09:00", "13:00"), (DayOfWeek.Tuesday, "09:00", "13:00")]);

        await factory.AddBookingAsync(hourly.Id, TestVenues.Utc("2026-05-04T08:00:00Z"), TestVenues.Utc("2026-05-04T09:00:00Z"));
        var noShow = await factory.AddBookingAsync(hourly.Id, TestVenues.Utc("2026-05-04T09:00:00Z"), TestVenues.Utc("2026-05-04T10:00:00Z"));
        await factory.SetBookingStatusAsync(noShow.Id, "NoShow");
        var cancelled = await factory.AddBookingAsync(hourly.Id, TestVenues.Utc("2026-05-04T10:00:00Z"), TestVenues.Utc("2026-05-04T11:00:00Z"));
        await factory.SetBookingStatusAsync(cancelled.Id, "CancelledByPlayer");
        var byClub = await factory.AddBookingAsync(hourly.Id, TestVenues.Utc("2026-05-05T10:00:00Z"), TestVenues.Utc("2026-05-05T11:00:00Z"));
        await factory.SetBookingStatusAsync(byClub.Id, "CancelledByClub");
        // Across the start (Lisbon midnight of the 4th is 23:00 UTC on the 3rd) and across the end (23:00 UTC on the 10th).
        await factory.AddBookingAsync(hourly.Id, TestVenues.Utc("2026-05-03T22:30:00Z"), TestVenues.Utc("2026-05-03T23:30:00Z"));
        await factory.AddBookingAsync(hourly.Id, TestVenues.Utc("2026-05-10T22:30:00Z"), TestVenues.Utc("2026-05-10T23:30:00Z"));
        // Outside the week altogether.
        await factory.AddBookingAsync(hourly.Id, TestVenues.Utc("2026-05-11T08:00:00Z"), TestVenues.Utc("2026-05-11T09:00:00Z"));
        // 90 minutes across the 60-minute marks.
        await factory.AddBookingAsync(ninety.Id, TestVenues.Utc("2026-05-05T08:00:00Z"), TestVenues.Utc("2026-05-05T09:30:00Z"));
        return new Venue(owner.Id, tokens.AccessToken, club, hourly, ninety);
    }

    // ---- The numbers

    [Fact]
    public async Task Each_pitch_and_the_total_add_up_as_computed_by_hand()
    {
        await using var app = AppAt();
        var venue = await VenueAsync();

        var statistics = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{venue.Club.Id}/statistics?{Week}", venue.Token));

        statistics.GetProperty("clubId").GetGuid().ShouldBe(venue.Club.Id);
        statistics.GetProperty("from").GetString().ShouldBe("2026-05-04");
        statistics.GetProperty("to").GetString().ShouldBe("2026-05-10");
        // A: 60 + 60 + 30 + 30 minutes booked of 240 open; four bookings at 20 € (the clipped ones in full); 3 confirmed, 1 no-show.
        ShouldHave(PitchNamed(statistics, "A"), booked: 3m, open: 4m, occupation: 0.75m, revenue: 80m, confirmed: 3, noShows: 1);
        // B: 90 minutes of 480.
        ShouldHave(PitchNamed(statistics, "B"), booked: 1.5m, open: 8m, occupation: 0.1875m, revenue: 20m, confirmed: 1, noShows: 0);
        // The total is the sum of minutes over the sum of open minutes (270 / 720), not an average of the rates.
        ShouldHave(statistics.GetProperty("total"), booked: 4.5m, open: 12m, occupation: 0.375m, revenue: 100m, confirmed: 4, noShows: 1);
        PitchNamed(statistics, "A").GetProperty("sport").GetString().ShouldBe("padel");
        PitchNamed(statistics, "A").GetProperty("status").GetString().ShouldBe("active");
    }

    [Fact]
    public async Task One_pitch_can_be_asked_for_and_a_pitch_of_another_club_is_404()
    {
        await using var app = AppAt();
        var venue = await VenueAsync();
        var otherClub = await factory.CreateClubAsync(venue.OwnerId);
        var otherPitch = await factory.CreatePitchAsync(otherClub.Id);

        var one = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{venue.Club.Id}/statistics?{Week}&pitchId={venue.NinetyMinutes.Id}", venue.Token));

        one.GetProperty("pitches").GetArrayLength().ShouldBe(1);
        ShouldHave(one.GetProperty("total"), booked: 1.5m, open: 8m, occupation: 0.1875m, revenue: 20m, confirmed: 1, noShows: 0);
        var problem = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{venue.Club.Id}/statistics?{Week}&pitchId={otherPitch.Id}", venue.Token), HttpStatusCode.NotFound);
        problem.GetProperty("code").GetString().ShouldBe("pitch_not_found");
    }

    [Fact]
    public async Task Closed_days_open_nothing_and_a_pitch_never_open_has_zero_occupation()
    {
        await using var app = AppAt();
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner);
        var club = await factory.CreateClubAsync(owner.Id);
        var closed = await factory.CreatePitchAsync(club.Id, "Fechado");
        await factory.ChangeScheduleAsync(closed.Id, 60);

        var statistics = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{club.Id}/statistics?{Week}", tokens.AccessToken));

        ShouldHave(PitchNamed(statistics, "Fechado"), booked: 0m, open: 0m, occupation: 0m, revenue: 0m, confirmed: 0, noShows: 0);
    }

    [Fact]
    public async Task An_inactive_pitch_counts_only_with_the_bookings_it_has()
    {
        await using var app = AppAt();
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner);
        var club = await factory.CreateClubAsync(owner.Id);
        var withBooking = await factory.CreatePitchAsync(club.Id, "Antigo");
        await factory.AddBookingAsync(withBooking.Id, TestVenues.Utc("2026-05-04T08:00:00Z"), TestVenues.Utc("2026-05-04T09:00:00Z"));
        await factory.DeactivatePitchAsync(withBooking.Id);
        var without = await factory.CreatePitchAsync(club.Id, "Parado");
        await factory.DeactivatePitchAsync(without.Id);

        var statistics = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{club.Id}/statistics?{Week}", tokens.AccessToken));

        statistics.GetProperty("pitches").EnumerateArray().Select(pitch => pitch.GetProperty("name").GetString()).ShouldBe(["Antigo"]);
        var old = PitchNamed(statistics, "Antigo");
        old.GetProperty("status").GetString().ShouldBe("inactive");
        ShouldHave(old, booked: 1m, open: 0m, occupation: 0m, revenue: 20m, confirmed: 1, noShows: 0);
    }

    [Theory]
    [InlineData("2026-10-25", 25)]
    [InlineData("2026-03-29", 23)]
    [InlineData("2026-10-24", 24)]
    public async Task A_pitch_open_all_day_has_the_real_length_of_the_lisbon_day(string date, int hours)
    {
        await using var app = AppAt();
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner);
        var club = await factory.CreateClubAsync(owner.Id);
        await factory.CreatePitchAsync(club.Id, "Sempre", open: EveryDayAllDay);

        var statistics = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{club.Id}/statistics?from={date}&to={date}", tokens.AccessToken));

        statistics.GetProperty("total").GetProperty("openHours").GetDecimal().ShouldBe(hours);
    }

    [Fact]
    public async Task A_week_with_the_october_change_has_one_hour_more_and_a_booking_in_the_repeated_hour_counts_once()
    {
        await using var app = AppAt();
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner);
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id, "Sempre", open: EveryDayAllDay);
        // 00:00 to 02:00 UTC on the 25th: 01:00 summer, the repeated 01:00 winter: two real hours.
        await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-10-25T00:00:00Z"), TestVenues.Utc("2026-10-25T02:00:00Z"));

        var statistics = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{club.Id}/statistics?from=2026-10-19&to=2026-10-25", tokens.AccessToken));

        statistics.GetProperty("total").GetProperty("openHours").GetDecimal().ShouldBe(6 * 24 + 25);
        statistics.GetProperty("total").GetProperty("bookedHours").GetDecimal().ShouldBe(2m);
    }

    // ---- Agreement with the calendar

    [Fact]
    public async Task For_the_same_range_booked_hours_and_revenue_equal_what_the_calendar_shows()
    {
        await using var app = AppAt();
        var venue = await VenueAsync();

        var statistics = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{venue.Club.Id}/statistics?{Week}", venue.Token));
        var calendar = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{venue.Club.Id}/calendar?{Week}&includeCancelled=false", venue.Token));

        // The calendar lists what overlaps the range; the statistics count Confirmed and NoShow, clipped to the same range.
        var rangeStart = TestVenues.Utc("2026-05-03T23:00:00Z");
        var rangeEnd = TestVenues.Utc("2026-05-10T23:00:00Z");
        var counted = calendar.GetProperty("bookings").EnumerateArray()
            .Where(booking => booking.GetProperty("status").GetString() is "confirmed" or "no_show")
            .ToList();
        var minutes = counted.Sum(booking =>
        {
            var starts = booking.GetProperty("startsAt").GetDateTime().ToUniversalTime();
            var ends = booking.GetProperty("endsAt").GetDateTime().ToUniversalTime();
            return ((ends < rangeEnd ? ends : rangeEnd) - (starts > rangeStart ? starts : rangeStart)).TotalMinutes;
        });
        var revenue = counted.Sum(booking => booking.GetProperty("price").GetDecimal());

        counted.Count.ShouldBe(5); // four on A (the cancelled ones are hidden, the one after the week is not in it), one on B
        statistics.GetProperty("total").GetProperty("bookedHours").GetDecimal().ShouldBe(Math.Round((decimal)minutes / 60m, 2));
        statistics.GetProperty("total").GetProperty("revenue").GetDecimal().ShouldBe(revenue);
        (statistics.GetProperty("total").GetProperty("confirmedBookings").GetInt32() + statistics.GetProperty("total").GetProperty("noShows").GetInt32()).ShouldBe(counted.Count);
    }

    // ---- The owner's list

    [Fact]
    public async Task The_owners_list_has_one_line_per_own_club_and_never_another_owners()
    {
        await using var app = AppAt();
        var venue = await VenueAsync();
        var second = await factory.CreateClubAsync(venue.OwnerId, name: $"Zeta {Guid.NewGuid():N}");
        var otherOwner = await VenueAsync();

        var list = await JsonAsync(await GetAsync(app, $"/api/manage/statistics?{Week}", venue.Token));

        var clubs = list.GetProperty("clubs").EnumerateArray().ToList();
        clubs.Select(club => club.GetProperty("clubId").GetGuid()).ShouldBe([venue.Club.Id, second.Id], ignoreOrder: true);
        clubs.ShouldNotContain(club => club.GetProperty("clubId").GetGuid() == otherOwner.Club.Id);
        var first = clubs.Single(club => club.GetProperty("clubId").GetGuid() == venue.Club.Id);
        ShouldHave(first, booked: 4.5m, open: 12m, occupation: 0.375m, revenue: 100m, confirmed: 4, noShows: 0 + 1);
        // A club with nothing open or booked is listed with zeros.
        ShouldHave(clubs.Single(club => club.GetProperty("clubId").GetGuid() == second.Id), booked: 0m, open: 0m, occupation: 0m, revenue: 0m, confirmed: 0, noShows: 0);
        ShouldHave(list.GetProperty("total"), booked: 4.5m, open: 12m, occupation: 0.375m, revenue: 100m, confirmed: 4, noShows: 1);
    }

    [Fact]
    public async Task Another_owners_club_is_404_club_not_found_before_the_dates_are_read()
    {
        await using var app = AppAt();
        var venue = await VenueAsync();
        var (_, otherTokens) = await factory.CreateSignedInAsync(Profiles.Owner);

        var problem = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{venue.Club.Id}/statistics?from=ontem", otherTokens.AccessToken), HttpStatusCode.NotFound);
        var unknown = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{Guid.CreateVersion7()}/statistics", venue.Token), HttpStatusCode.NotFound);

        problem.GetProperty("code").GetString().ShouldBe("club_not_found");
        unknown.GetProperty("code").GetString().ShouldBe("club_not_found");
    }

    [Theory]
    [InlineData(Profiles.Player)]
    [InlineData(Profiles.Admin)]
    public async Task Another_profile_gets_403_and_no_session_401(string profile)
    {
        await using var app = AppAt();
        var venue = await VenueAsync();
        var (_, tokens) = await factory.CreateSignedInAsync(profile);

        foreach (var path in new[] { $"/api/manage/clubs/{venue.Club.Id}/statistics", "/api/manage/statistics" })
        {
            using var forbidden = await GetAsync(app, path, tokens.AccessToken);
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
            using var client = app.CreateSiteClient();
            using var anonymous = await client.GetAsync(path, Ct);
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
        }
    }

    // ---- The period

    [Fact]
    public async Task Without_dates_the_period_is_the_last_30_days_in_lisbon()
    {
        await using var app = AppAt("2026-05-13T23:30:00Z"); // already 14 May in Lisbon
        var venue = await VenueAsync();

        var statistics = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{venue.Club.Id}/statistics", venue.Token));

        statistics.GetProperty("from").GetString().ShouldBe("2026-04-15");
        statistics.GetProperty("to").GetString().ShouldBe("2026-05-14");
    }

    [Theory]
    [InlineData("from=2026-05-10&to=2026-05-04", "to")]
    [InlineData("from=2025-01-01&to=2026-01-02", "to")] // 367 days
    [InlineData("from=ontem", "from")]
    [InlineData("to=2026-02-30", "to")]
    [InlineData("from=1999-12-31", "from")]
    [InlineData("from=2026-06-20", "from")] // after today, with no end: the end is today
    public async Task A_wrong_period_is_a_400_on_that_field(string query, string field)
    {
        await using var app = AppAt();
        var venue = await VenueAsync();

        var problem = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{venue.Club.Id}/statistics?{query}", venue.Token), HttpStatusCode.BadRequest);

        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").EnumerateObject().Select(property => property.Name).ShouldContain(field);
    }

    [Fact]
    public async Task A_period_of_366_days_is_the_longest_allowed()
    {
        await using var app = AppAt();
        var venue = await VenueAsync();

        var statistics = await JsonAsync(await GetAsync(app, $"/api/manage/clubs/{venue.Club.Id}/statistics?from=2024-01-01&to=2024-12-31", venue.Token));

        statistics.GetProperty("to").GetString().ShouldBe("2024-12-31");
    }

    [Fact]
    public async Task The_answers_hold_no_player_data()
    {
        await using var app = AppAt();
        var venue = await VenueAsync();
        var playerEmail = $"jogadora-secreta-{Guid.NewGuid():N}@exemplo.test";
        var player = await factory.CreateAccountAsync(Profiles.Player, null, playerEmail, "Jogadora Secretíssima");
        await factory.AddBookingAsync(venue.Hourly.Id, TestVenues.Utc("2026-05-04T11:00:00Z"), TestVenues.Utc("2026-05-04T12:00:00Z"), player);

        foreach (var path in new[] { $"/api/manage/clubs/{venue.Club.Id}/statistics?{Week}", $"/api/manage/statistics?{Week}" })
        {
            using var response = await GetAsync(app, path, venue.Token);
            var body = await response.Content.ReadAsStringAsync(Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            body.ShouldNotContain("Secret", Case.Insensitive);
            body.ShouldNotContain(playerEmail, Case.Insensitive);
            body.ShouldNotContain(player.Id.ToString(), Case.Insensitive);
        }
    }
}
