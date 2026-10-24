using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Pitches;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Catalog;

/// <summary>US17: the slots of a pitch on a date, for anyone. The answer is one flag per slot and nothing else.</summary>
public sealed class PublicSlotsTests(ApiFactory factory)
{
    // Friday 1 May 2026, 11:00 in Lisbon. Monday 4 May is open 09:00-13:00 (TestVenues.MondayMorning): slots at 08, 09, 10 and 11 UTC.
    private const string Clock = "2026-05-01T10:00:00Z";
    private const string Monday = "2026-05-04";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> AppAt(string utcNow) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private async Task<(TestClub Club, TestPitch Pitch)> VenueAsync(
        int slotMinutes = 60,
        decimal price = 20m,
        params (DayOfWeek Day, string Opens, string Closes)[] open)
    {
        var club = await factory.CreateClubAsync();
        return (club, await factory.CreatePitchAsync(club.Id, price: price, slotMinutes: slotMinutes, open: open));
    }

    private sealed record Answer(HttpStatusCode Status, string Text, JsonElement Json);

    private static async Task<Answer> GetAsync(WebApplicationFactory<Program> app, string slug, object pitchId, string? date = Monday)
    {
        using var client = app.CreateClient(); // no session
        using var response = await client.GetAsync($"/api/clubs/{slug}/pitches/{pitchId}/slots{(date is null ? string.Empty : "?date=" + date)}", Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        return new Answer(response.StatusCode, text, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static async Task<JsonElement> SlotsOkAsync(WebApplicationFactory<Program> app, TestClub club, TestPitch pitch, string date = Monday)
    {
        var answer = await GetAsync(app, club.Slug, pitch.Id, date);
        answer.Status.ShouldBe(HttpStatusCode.OK, answer.Text);
        return answer.Json;
    }

    /// <summary>One character per slot: <c>.</c> available, <c>x</c> not available.</summary>
    private static string Pattern(JsonElement answer) =>
        string.Concat(answer.GetProperty("slots").EnumerateArray().Select(slot => slot.GetProperty("available").GetBoolean() ? '.' : 'x'));

    /// <summary>Start and end of each slot as UTC, e.g. <c>04 08:00-09:00</c>.</summary>
    private static string[] Times(JsonElement answer) =>
        answer.GetProperty("slots").EnumerateArray()
            .Select(slot => $"{slot.GetProperty("startsAt").GetDateTime().ToUniversalTime():dd HH:mm}-{slot.GetProperty("endsAt").GetDateTime().ToUniversalTime():HH:mm}")
            .ToArray();

    private static void ShouldBeProblem(Answer answer, HttpStatusCode status, string code, string context = "")
    {
        answer.Status.ShouldBe(status, $"{context} {answer.Text}");
        answer.Json.GetProperty("code").GetString().ShouldBe(code, $"{context} {answer.Text}");
        answer.Json.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    // ---- The answer

    [Fact]
    public async Task Anyone_sees_every_slot_of_the_day_with_start_end_price_and_availability()
    {
        await using var app = AppAt(Clock);
        var (club, pitch) = await VenueAsync(price: 24.5m);

        var answer = await GetAsync(app, club.Slug, pitch.Id);

        answer.Status.ShouldBe(HttpStatusCode.OK);
        answer.Json.EnumerateObject().Select(property => property.Name).ShouldBe(["date", "slots"]);
        answer.Json.GetProperty("date").GetString().ShouldBe(Monday);
        Times(answer.Json).ShouldBe(["04 08:00-09:00", "04 09:00-10:00", "04 10:00-11:00", "04 11:00-12:00"]);
        Pattern(answer.Json).ShouldBe("....");
        foreach (var slot in answer.Json.GetProperty("slots").EnumerateArray())
        {
            slot.EnumerateObject().Select(property => property.Name).ShouldBe(["startsAt", "endsAt", "price", "available"]);
            slot.GetProperty("price").GetDecimal().ShouldBe(24.5m);
        }

        // Instants with their zone, so the hour that repeats in October is never ambiguous.
        answer.Text.ShouldContain("\"startsAt\":\"2026-05-04T08:00:00Z\"");
    }

    [Fact]
    public async Task The_price_is_the_pitchs_current_price()
    {
        await using var app = AppAt(Clock);
        var (club, pitch) = await VenueAsync(price: 20m);
        await factory.ChangePriceAsync(pitch.Id, 31m);

        var answer = await SlotsOkAsync(app, club, pitch);

        answer.GetProperty("slots").EnumerateArray().ShouldAllBe(slot => slot.GetProperty("price").GetDecimal() == 31m);
    }

    [Fact]
    public async Task A_closed_day_has_no_slots()
    {
        await using var app = AppAt(Clock);
        var (club, pitch) = await VenueAsync();

        var answer = await SlotsOkAsync(app, club, pitch, date: "2026-05-05"); // Tuesday

        answer.GetProperty("slots").GetArrayLength().ShouldBe(0);
        answer.GetProperty("date").GetString().ShouldBe("2026-05-05");
    }

    // ---- What takes a slot

    [Fact]
    public async Task A_slot_with_a_confirmed_booking_is_not_available_but_cancelled_bookings_free_it()
    {
        await using var app = AppAt(Clock);
        var (club, pitch) = await VenueAsync();
        await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-04T09:00:00Z"), TestVenues.Utc("2026-05-04T10:00:00Z"));
        var byPlayer = await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-04T10:00:00Z"), TestVenues.Utc("2026-05-04T11:00:00Z"));
        var byClub = await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-04T11:00:00Z"), TestVenues.Utc("2026-05-04T12:00:00Z"));
        await factory.SetBookingStatusAsync(byPlayer.Id, "CancelledByPlayer");
        await factory.SetBookingStatusAsync(byClub.Id, "CancelledByClub");

        Pattern(await SlotsOkAsync(app, club, pitch)).ShouldBe(".x..");
    }

    [Fact]
    public async Task A_slot_that_overlaps_a_block_even_partly_is_not_available()
    {
        await using var app = AppAt(Clock);
        var (club, pitch) = await VenueAsync();
        await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-04T09:30:00Z"), TestVenues.Utc("2026-05-04T09:45:00Z")); // inside the second slot
        await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-04T10:59:00Z"), TestVenues.Utc("2026-05-04T11:30:00Z")); // the last minute of the third and part of the fourth

        Pattern(await SlotsOkAsync(app, club, pitch)).ShouldBe(".xxx");
    }

    [Fact]
    public async Task A_booking_or_block_that_only_touches_a_slot_does_not_take_it()
    {
        await using var app = AppAt(Clock);
        var (club, pitch) = await VenueAsync();
        await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-04T07:00:00Z"), TestVenues.Utc("2026-05-04T08:00:00Z"));
        await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-04T12:00:00Z"), TestVenues.Utc("2026-05-04T13:00:00Z"));

        Pattern(await SlotsOkAsync(app, club, pitch)).ShouldBe("....");
    }

    [Theory]
    [InlineData("2026-05-04T07:59:59Z", "....")] // nothing has started
    [InlineData("2026-05-04T08:00:00Z", "x...")] // starting right now counts as started
    [InlineData("2026-05-04T09:30:00Z", "xx..")]
    [InlineData("2026-05-04T11:00:00Z", "xxxx")]
    [InlineData("2026-05-04T11:59:59Z", "xxxx")]
    public async Task A_slot_that_has_started_is_not_available(string now, string expected)
    {
        await using var app = AppAt(now);
        var (club, pitch) = await VenueAsync();

        Pattern(await SlotsOkAsync(app, club, pitch)).ShouldBe(expected); // today
    }

    [Theory]
    [InlineData(new[] { "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z" }, "x.")] // one old booking overlaps the first new slot
    [InlineData(new[] { "2026-05-04T08:00:00Z", "2026-05-04T09:00:00Z", "2026-05-04T10:00:00Z", "2026-05-04T11:00:00Z" }, "xx")] // and another the second
    [InlineData(new[] { "2026-05-04T09:00:00Z", "2026-05-04T10:00:00Z" }, "xx")] // one old booking across the boundary of both
    public async Task After_a_slot_length_change_a_slot_is_taken_by_any_older_booking_that_overlaps_it(string[] bookings, string expected)
    {
        await using var app = AppAt(Clock);
        var (club, pitch) = await VenueAsync();
        // Booked on the 60-minute grid, then the pitch moves to 90-minute slots (US08): 09:00-10:30 and 10:30-12:00 Lisbon,
        // that is 08:00-09:30Z and 09:30-11:00Z. The old bookings stay as they were.
        for (var index = 0; index < bookings.Length; index += 2)
        {
            await factory.AddBookingAsync(pitch.Id, TestVenues.Utc(bookings[index]), TestVenues.Utc(bookings[index + 1]));
        }

        await factory.ChangeScheduleAsync(pitch.Id, 90, (DayOfWeek.Monday, "09:00", "12:00"));

        var answer = await SlotsOkAsync(app, club, pitch);
        Times(answer).ShouldBe(["04 08:00-09:30", "04 09:30-11:00"]);
        Pattern(answer).ShouldBe(expected);
    }

    [Fact]
    public async Task Shorter_slots_after_a_change_are_taken_where_an_older_longer_booking_covers_them()
    {
        await using var app = AppAt(Clock);
        var (club, pitch) = await VenueAsync();
        await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-04T09:00:00Z"), TestVenues.Utc("2026-05-04T10:00:00Z"));

        await factory.ChangeScheduleAsync(pitch.Id, 30, (DayOfWeek.Monday, "09:00", "13:00"));

        Pattern(await SlotsOkAsync(app, club, pitch)).ShouldBe("..xx....");
    }

    // ---- Daylight saving time (T04)

    [Fact]
    public async Task On_the_day_clocks_go_back_the_repeated_hour_has_two_different_slots()
    {
        // 25 October 2026: at 02:00 WEST the clocks return to 01:00 WET, so 01:00-02:00 Lisbon happens twice.
        await using var app = AppAt("2026-10-20T10:00:00Z");
        var (club, pitch) = await VenueAsync(open: (DayOfWeek.Sunday, "00:00", "04:00"));
        // Book the second 01:00-02:00 (WET), which is 01:00-02:00 UTC.
        await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-10-25T01:00:00Z"), TestVenues.Utc("2026-10-25T02:00:00Z"));

        var answer = await SlotsOkAsync(app, club, pitch, date: "2026-10-25");

        Times(answer).ShouldBe(["24 23:00-00:00", "25 00:00-01:00", "25 01:00-02:00", "25 02:00-03:00", "25 03:00-04:00"]);
        // The first 01:00 (00:00-01:00 UTC, still summer time) stays free; only the booked one is taken.
        Pattern(answer).ShouldBe("..x..");
    }

    [Fact]
    public async Task On_the_day_clocks_go_forward_the_missing_hour_has_no_slot()
    {
        // 29 March 2026: at 01:00 WET the clocks jump to 02:00 WEST, so 01:00-02:00 Lisbon does not exist.
        await using var app = AppAt("2026-03-20T10:00:00Z");
        var (club, pitch) = await VenueAsync(open: (DayOfWeek.Sunday, "00:00", "04:00"));

        var answer = await SlotsOkAsync(app, club, pitch, date: "2026-03-29");

        Times(answer).ShouldBe(["29 00:00-01:00", "29 01:00-02:00", "29 02:00-03:00"]);
        Pattern(answer).ShouldBe("...");
    }

    // ---- Dates

    [Fact]
    public async Task Only_dates_from_today_up_to_30_days_ahead_are_accepted()
    {
        await using var app = AppAt(Clock);
        var (club, pitch) = await VenueAsync();

        foreach (var date in new[] { "2026-05-01", "2026-05-31" }) // today and today + 30
        {
            (await GetAsync(app, club.Slug, pitch.Id, date)).Status.ShouldBe(HttpStatusCode.OK, date);
        }

        foreach (var date in new[] { "2026-04-30", "2026-06-01", "2020-01-01", "2099-12-31" })
        {
            var answer = await GetAsync(app, club.Slug, pitch.Id, date);
            ShouldBeProblem(answer, HttpStatusCode.BadRequest, "validation_failed");
            answer.Json.GetProperty("errors").TryGetProperty("date", out _).ShouldBeTrue(date);
        }
    }

    [Fact]
    public async Task Today_is_the_lisbon_date_not_the_utc_date()
    {
        // 23:30 UTC on Sunday 3 May is 00:30 on Monday 4 May in Lisbon.
        await using var app = AppAt("2026-05-03T23:30:00Z");
        var (club, pitch) = await VenueAsync();

        (await GetAsync(app, club.Slug, pitch.Id, "2026-05-04")).Status.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(app, club.Slug, pitch.Id, "2026-05-03")).Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("amanha")]
    [InlineData("2026-5-4")]
    [InlineData("04-05-2026")]
    [InlineData("2026-02-30")]
    public async Task A_missing_or_malformed_date_is_a_400_on_the_date_field(string? date)
    {
        await using var app = AppAt(Clock);
        var (club, pitch) = await VenueAsync();

        var answer = await GetAsync(app, club.Slug, pitch.Id, date);

        ShouldBeProblem(answer, HttpStatusCode.BadRequest, "validation_failed");
        answer.Json.GetProperty("errors").GetProperty("date")[0].GetString().ShouldNotBeNull().ShouldContain("AAAA-MM-DD");
    }

    // ---- What is not there

    [Fact]
    public async Task Hidden_or_unknown_clubs_and_pitches_are_404_whatever_the_date_is()
    {
        await using var app = AppAt(Clock);
        var (club, pitch) = await VenueAsync();
        var (_, otherPitch) = await VenueAsync();
        var inactive = await factory.CreatePitchAsync(club.Id, "Campo antigo");
        await factory.DeactivatePitchAsync(inactive.Id);
        var (onlyInactiveClub, onlyInactivePitch) = await VenueAsync();
        await factory.DeactivatePitchAsync(onlyInactivePitch.Id);
        var (ownerGoneClub, ownerGonePitch) = await VenueAsync();
        await factory.DeactivateAccountAsync(ownerGoneClub.OwnerId);

        var cases = new (string Name, string Slug, object PitchId, string Code)[]
        {
            ("unknown club", "clube-que-nao-existe", pitch.Id, "club_not_found"),
            ("club of a deactivated owner", ownerGoneClub.Slug, ownerGonePitch.Id, "club_not_found"),
            ("club whose only pitch is inactive", onlyInactiveClub.Slug, onlyInactivePitch.Id, "club_not_found"),
            ("inactive pitch of a visible club", club.Slug, inactive.Id, "pitch_not_found"),
            ("pitch of another club", club.Slug, otherPitch.Id, "pitch_not_found"),
            ("unknown pitch", club.Slug, Guid.CreateVersion7(), "pitch_not_found"),
            ("pitch id that is not a GUID", club.Slug, "not-a-guid", "pitch_not_found"),
        };
        foreach (var (name, slug, pitchId, code) in cases)
        {
            foreach (var date in new[] { Monday, "isto-nao-e-uma-data", null })
            {
                ShouldBeProblem(await GetAsync(app, slug, pitchId, date), HttpStatusCode.NotFound, code, $"{name}, date {date ?? "(none)"}:");
            }
        }
    }

    // ---- Nothing else is said

    [Fact]
    public async Task An_unavailable_slot_looks_the_same_whatever_the_reason()
    {
        // 10:00 UTC on Monday: the first three slots have started.
        await using var app = AppAt("2026-05-04T10:00:00Z");
        var (club, pitch) = await VenueAsync();
        var withBooking = await factory.CreatePitchAsync(club.Id, "Com reserva", slotMinutes: 60);
        await factory.AddBookingAsync(withBooking.Id, TestVenues.Utc("2026-05-04T11:00:00Z"), TestVenues.Utc("2026-05-04T12:00:00Z"));
        var withBlock = await factory.CreatePitchAsync(club.Id, "Com bloqueio", slotMinutes: 60);
        await factory.AddBlockAsync(withBlock.Id, TestVenues.Utc("2026-05-04T11:00:00Z"), TestVenues.Utc("2026-05-04T12:00:00Z"));

        var started = (await SlotsOkAsync(app, club, pitch)).GetProperty("slots")[0];
        var booked = (await SlotsOkAsync(app, club, withBooking)).GetProperty("slots")[3];
        var blocked = (await SlotsOkAsync(app, club, withBlock)).GetProperty("slots")[3];

        foreach (var slot in new[] { started, booked, blocked })
        {
            slot.GetProperty("available").GetBoolean().ShouldBeFalse();
            slot.EnumerateObject().Select(property => property.Name).ShouldBe(["startsAt", "endsAt", "price", "available"]);
        }
    }

    [Fact]
    public async Task The_answer_has_no_player_owner_booking_or_block_data()
    {
        await using var app = AppAt(Clock);
        var owner = await factory.CreateAccountAsync(Profiles.Owner, password: null, email: $"dono-{Guid.NewGuid():N}@exemplo.test", name: "Dona Privada Silva");
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id);
        var player = await factory.CreateAccountAsync(Profiles.Player, password: null, email: $"jogador-{Guid.NewGuid():N}@exemplo.test", name: "Jogador Privado Costa");
        var booking = await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-04T08:00:00Z"), TestVenues.Utc("2026-05-04T09:00:00Z"), player);
        var blockId = await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-04T10:00:00Z"), TestVenues.Utc("2026-05-04T11:00:00Z"));

        var answer = await GetAsync(app, club.Slug, pitch.Id);

        Pattern(answer.Json).ShouldBe("x.x.");
        foreach (var secret in new[]
        {
            player.Email!, player.Id.ToString(), "Privado", booking.Id.ToString(),
            blockId.ToString(), "Manutenção",
            owner.Email!, owner.Id.ToString(), "Privada", club.Id.ToString(),
        })
        {
            answer.Text.ShouldNotContain(secret);
        }
    }

    [Fact]
    public async Task The_search_and_the_slots_agree_on_whether_a_club_has_free_slots()
    {
        // Monday 09:30 UTC: the 08:00 and 09:00 UTC slots have started.
        await using var app = AppAt("2026-05-04T09:30:00Z");
        var locality = $"Cidade{Guid.NewGuid():N}";
        var free = await factory.CreateClubAsync(locality: locality);
        var freePitch = await factory.CreatePitchAsync(free.Id);
        var taken = await factory.CreateClubAsync(locality: locality);
        var takenPitch = await factory.CreatePitchAsync(taken.Id);
        await factory.AddBookingAsync(takenPitch.Id, TestVenues.Utc("2026-05-04T10:00:00Z"), TestVenues.Utc("2026-05-04T11:00:00Z"));
        await factory.AddBlockAsync(takenPitch.Id, TestVenues.Utc("2026-05-04T11:00:00Z"), TestVenues.Utc("2026-05-04T12:00:00Z"));
        var oneLeft = await factory.CreateClubAsync(locality: locality);
        var oneLeftPitch = await factory.CreatePitchAsync(oneLeft.Id);
        await factory.AddBookingAsync(oneLeftPitch.Id, TestVenues.Utc("2026-05-04T10:00:00Z"), TestVenues.Utc("2026-05-04T11:00:00Z"));

        using var client = app.CreateClient();
        var search = JsonDocument.Parse(await client.GetStringAsync($"/api/clubs?locality={locality}", Ct)).RootElement;
        var flags = search.GetProperty("items").EnumerateArray()
            .ToDictionary(item => item.GetProperty("slug").GetString()!, item => item.GetProperty("hasFreeSlots").GetBoolean());

        foreach (var (club, pitch, expected) in new[] { (free, freePitch, true), (taken, takenPitch, false), (oneLeft, oneLeftPitch, true) })
        {
            var anyAvailable = Pattern(await SlotsOkAsync(app, club, pitch, Monday)).Contains('.', StringComparison.Ordinal);
            anyAvailable.ShouldBe(expected, club.Slug);
            flags[club.Slug].ShouldBe(anyAvailable, club.Slug);
        }
    }
}
