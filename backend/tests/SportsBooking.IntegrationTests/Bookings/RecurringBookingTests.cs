using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Bookings;

/// <summary>US20: the same slot for up to four weeks, previewed, then booked date by date with the rules of a single booking.</summary>
public sealed class RecurringBookingTests(ApiFactory factory)
{
    // Friday 1 May 2026, 11:00 in Lisbon (WEST). Mondays are open 09:00-13:00 (TestVenues.MondayMorning); the window ends on 31 May.
    private const string MayClock = "2026-05-01T10:00:00Z";
    private const string FirstMonday = "2026-05-04T09:00:00Z"; // 10:00 in Lisbon

    private const string PreviewPath = "/api/bookings/recurring/preview";
    private const string CreatePath = "/api/bookings/recurring";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> AppAt(string utcNow) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private static async Task<HttpResponseMessage> SendAsync(WebApplicationFactory<Program> app, HttpMethod method, string path, string? token, object? body = null)
    {
        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (token is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, token));
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<string> PlayerAsync() => (await factory.CreateSignedInPlayerAsync()).Tokens.AccessToken;

    private async Task<(TestClub Club, TestPitch Pitch)> VenueAsync(params (DayOfWeek Day, string Opens, string Closes)[] open)
    {
        var club = await factory.CreateClubAsync();
        return (club, await factory.CreatePitchAsync(club.Id, open: open));
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, text);
        var problem = JsonDocument.Parse(text).RootElement.Clone();
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        return problem;
    }

    private static string Utc(JsonElement instant) => instant.GetDateTime().ToUniversalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"05-04 09:00 free 20" per date.</summary>
    private static string[] Lines(JsonElement preview) =>
        preview.GetProperty("occurrences").EnumerateArray()
            .Select(occurrence =>
                $"{Utc(occurrence.GetProperty("startsAt"))} {occurrence.GetProperty("status").GetString()}" +
                $"{(occurrence.GetProperty("reason").ValueKind == JsonValueKind.Null ? string.Empty : "/" + occurrence.GetProperty("reason").GetString())}" +
                $" {occurrence.GetProperty("price").GetDecimal().ToString("0.##", CultureInfo.InvariantCulture)}")
            .ToArray();

    private async Task<List<Booking>> BookingsOfAsync(Guid pitchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking()
            .Where(booking => booking.PitchId == pitchId).OrderBy(booking => booking.StartsAt).ToListAsync(Ct);
    }

    // ---- Preview

    [Fact]
    public async Task The_preview_lists_every_date_as_free_with_its_end_and_price()
    {
        var (_, pitch) = await VenueAsync();
        var app = AppAt(MayClock);

        var preview = await OkAsync(await SendAsync(app, HttpMethod.Post, PreviewPath, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = FirstMonday, weeks = 4 }));

        preview.GetProperty("weeks").GetInt32().ShouldBe(4);
        Lines(preview).ShouldBe(["05-04 09:00 free 20", "05-11 09:00 free 20", "05-18 09:00 free 20", "05-25 09:00 free 20"]);
        preview.GetProperty("occurrences")[0].GetProperty("endsAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc("2026-05-04T10:00:00Z"));
    }

    [Fact]
    public async Task The_preview_says_which_date_is_taken_blocked_or_past_the_window_without_saying_who()
    {
        var (_, pitch) = await VenueAsync();
        var booked = await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-11T09:00:00Z"), TestVenues.Utc("2026-05-11T10:00:00Z"));
        await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-18T09:30:00Z"), TestVenues.Utc("2026-05-18T10:30:00Z")); // partly over the slot
        var app = AppAt(MayClock);

        using var response = await SendAsync(app, HttpMethod.Post, PreviewPath, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = "2026-05-18T09:00:00Z", weeks = 3 });
        var text = await response.Content.ReadAsStringAsync(Ct);
        var preview = await OkAsync(response);

        Lines(preview).ShouldBe(["05-18 09:00 unavailable/blocked 20", "05-25 09:00 free 20", "06-01 09:00 unavailable/outside_window 20"]);
        using var taken = await SendAsync(app, HttpMethod.Post, PreviewPath, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = FirstMonday, weeks = 2 });
        var takenText = await taken.Content.ReadAsStringAsync(Ct);
        Lines(await OkAsync(taken)).ShouldBe(["05-04 09:00 free 20", "05-11 09:00 unavailable/taken 20"]);
        // Nothing about the booking or its player: no ids, no names.
        foreach (var body in new[] { text, takenText })
        {
            body.ShouldNotContain(booked.Id.ToString());
            body.ShouldNotContain(booked.PlayerId!.Value.ToString());
            body.ShouldNotContain("Jogador de Teste");
        }
    }

    [Fact]
    public async Task The_preview_tells_a_closed_weekday_from_a_time_off_the_grid()
    {
        var (_, pitch) = await VenueAsync();
        var app = AppAt(MayClock);

        // Tuesday: closed. Monday 10:30 local: open, but not a slot of the 60-minute grid that starts at 09:00.
        var closed = await OkAsync(await SendAsync(app, HttpMethod.Post, PreviewPath, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = "2026-05-05T09:00:00Z", weeks = 1 }));
        var offGrid = await OkAsync(await SendAsync(app, HttpMethod.Post, PreviewPath, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = "2026-05-04T09:30:00Z", weeks = 1 }));

        Lines(closed).ShouldBe(["05-05 09:00 unavailable/closed 20"]);
        Lines(offGrid).ShouldBe(["05-04 09:30 unavailable/not_a_slot 20"]);
    }

    [Fact]
    public async Task A_series_across_the_october_clock_change_keeps_the_local_time_and_prices_each_date()
    {
        // Sundays 09:00-13:00; a band on Sunday 10:00-11:00 at 33 €. The series starts on 18 October at 10:00 (WEST, 09:00 UTC).
        var (_, pitch) = await VenueAsync((DayOfWeek.Sunday, "09:00", "13:00"));
        await factory.AddPriceBandAsync(pitch.Id, 33m, "10:00", "11:00", DayOfWeek.Sunday);
        var app = AppAt("2026-10-15T10:00:00Z");

        var preview = await OkAsync(await SendAsync(app, HttpMethod.Post, PreviewPath, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = "2026-10-18T09:00:00Z", weeks = 4 }));

        // From 25 October Lisbon is on UTC: the same 10:00 is 10:00 UTC, and still inside the band.
        Lines(preview).ShouldBe(["10-18 09:00 free 33", "10-25 10:00 free 33", "11-01 10:00 free 33", "11-08 10:00 free 33"]);
    }

    [Fact]
    public async Task A_hidden_or_unknown_pitch_is_a_404_in_the_preview()
    {
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        await factory.DeactivateAccountAsync(club.OwnerId);
        var inactive = await factory.CreatePitchAsync((await factory.CreateClubAsync()).Id);
        await factory.DeactivatePitchAsync(inactive.Id);
        var app = AppAt(MayClock);
        var player = await PlayerAsync();

        foreach (var pitchId in new[] { pitch.Id, inactive.Id, Guid.CreateVersion7() })
        {
            using var response = await SendAsync(app, HttpMethod.Post, PreviewPath, player, new { pitchId, startsAt = FirstMonday, weeks = 2 });
            await ProblemAsync(response, HttpStatusCode.NotFound, ErrorCodes.PitchNotFound);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Weeks_must_be_between_one_and_four(int weeks)
    {
        var (_, pitch) = await VenueAsync();

        foreach (var path in new[] { PreviewPath, CreatePath })
        {
            using var response = await SendAsync(AppAt(MayClock), HttpMethod.Post, path, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = FirstMonday, weeks });
            var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
            problem.GetProperty("errors").GetProperty("weeks")[0].GetString().ShouldBe("Escolhe entre 1 e 4 semanas.");
        }
    }

    // ---- Booking

    [Fact]
    public async Task Every_free_date_is_booked_with_one_series_id_and_its_own_price()
    {
        var (club, pitch) = await VenueAsync();
        await factory.AddPriceBandAsync(pitch.Id, 35m, "10:00", "11:00", DayOfWeek.Monday);
        var app = AppAt(MayClock);

        using var response = await SendAsync(app, HttpMethod.Post, CreatePath, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = FirstMonday, weeks = 4 });

        var body = await OkAsync(response, HttpStatusCode.Created);
        var seriesId = body.GetProperty("seriesId").GetGuid();
        seriesId.Version.ShouldBe(7);
        var created = body.GetProperty("created").EnumerateArray().ToList();
        created.Select(booking => Utc(booking.GetProperty("startsAt"))).ShouldBe(["05-04 09:00", "05-11 09:00", "05-18 09:00", "05-25 09:00"]);
        created.ShouldAllBe(booking => booking.GetProperty("price").GetDecimal() == 35m && booking.GetProperty("clubId").GetGuid() == club.Id);
        body.GetProperty("skipped").GetArrayLength().ShouldBe(0);
        response.Headers.Location!.ToString().ShouldBe($"/api/bookings/{created[0].GetProperty("id").GetGuid()}");

        var stored = await BookingsOfAsync(pitch.Id);
        stored.Count.ShouldBe(4);
        stored.ShouldAllBe(booking => booking.SeriesId == seriesId && booking.Price == 35m && booking.Status == BookingStatus.Confirmed);
    }

    [Fact]
    public async Task Taken_and_blocked_dates_are_left_out_with_their_codes_and_the_others_are_still_booked()
    {
        var (_, pitch) = await VenueAsync();
        await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-11T09:00:00Z"), TestVenues.Utc("2026-05-11T10:00:00Z"));
        await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-18T09:00:00Z"), TestVenues.Utc("2026-05-18T10:00:00Z"));
        var app = AppAt(MayClock);

        var body = await OkAsync(await SendAsync(app, HttpMethod.Post, CreatePath, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = FirstMonday, weeks = 4 }), HttpStatusCode.Created);

        body.GetProperty("created").EnumerateArray().Select(booking => Utc(booking.GetProperty("startsAt"))).ShouldBe(["05-04 09:00", "05-25 09:00"]);
        body.GetProperty("skipped").EnumerateArray().Select(date => $"{Utc(date.GetProperty("startsAt"))} {date.GetProperty("reason").GetString()}")
            .ShouldBe(["05-11 09:00 taken", "05-18 09:00 blocked"]);
        var seriesId = body.GetProperty("seriesId").GetGuid();
        (await BookingsOfAsync(pitch.Id)).Count(booking => booking.SeriesId == seriesId).ShouldBe(2);
    }

    [Fact]
    public async Task Dates_past_the_window_are_left_out_and_the_first_is_still_booked()
    {
        var (_, pitch) = await VenueAsync();
        var app = AppAt(MayClock);

        var body = await OkAsync(await SendAsync(app, HttpMethod.Post, CreatePath, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = "2026-05-25T09:00:00Z", weeks = 3 }), HttpStatusCode.Created);

        body.GetProperty("created").GetArrayLength().ShouldBe(1);
        body.GetProperty("skipped").EnumerateArray().Select(date => date.GetProperty("reason").GetString()).ShouldBe(["outside_window", "outside_window"]);
    }

    [Fact]
    public async Task When_no_date_can_be_booked_it_is_a_409_with_every_reason_and_nothing_is_saved()
    {
        var (_, pitch) = await VenueAsync();
        await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-04T09:00:00Z"), TestVenues.Utc("2026-05-04T10:00:00Z"));
        await factory.AddBlockAsync(pitch.Id, TestVenues.Utc("2026-05-11T09:00:00Z"), TestVenues.Utc("2026-05-11T10:00:00Z"));
        var app = AppAt(MayClock);

        using var response = await SendAsync(app, HttpMethod.Post, CreatePath, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = FirstMonday, weeks = 2 });

        var problem = await ProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.SeriesUnavailable);
        problem.GetProperty("skipped").EnumerateArray().Select(date => date.GetProperty("reason").GetString()).ShouldBe(["taken", "blocked"]);
        (await BookingsOfAsync(pitch.Id)).Count.ShouldBe(1); // the one that was there
    }

    [Fact]
    public async Task Twenty_simultaneous_identical_series_make_each_date_once_and_never_a_500()
    {
        var (_, pitch) = await VenueAsync();
        var players = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PlayerAsync()));
        // Its own small pool: twenty requests waiting on the pitch lock must not take twenty connections from the server that the
        // rest of the suite (running alongside) needs. Requests beyond eight wait for a connection instead.
        var connectionString = factory.ConnectionString + ";Maximum Pool Size=8;Minimum Pool Size=0";
        await using var app = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(MayClock, CultureInfo.InvariantCulture))));
        });

        var answers = await Task.WhenAll(players.Select(async player =>
        {
            using var response = await SendAsync(app, HttpMethod.Post, CreatePath, player, new { pitchId = pitch.Id, startsAt = FirstMonday, weeks = 4 });
            return (response.StatusCode, Body: await response.Content.ReadAsStringAsync(Ct));
        }));

        answers.ShouldAllBe(answer => answer.StatusCode == HttpStatusCode.Created || answer.StatusCode == HttpStatusCode.Conflict);
        var createdTotal = answers.Where(answer => answer.StatusCode == HttpStatusCode.Created)
            .Sum(answer => JsonDocument.Parse(answer.Body).RootElement.GetProperty("created").GetArrayLength());
        createdTotal.ShouldBe(4);
        var stored = await BookingsOfAsync(pitch.Id);
        stored.Count.ShouldBe(4);
        stored.Select(booking => booking.StartsAt).Distinct().Count().ShouldBe(4);
        // Every date someone lost is reported as taken.
        answers.Where(answer => answer.StatusCode == HttpStatusCode.Conflict)
            .ShouldAllBe(answer => answer.Body.Contains("\"taken\"", StringComparison.Ordinal));
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
    }

    [Fact]
    public async Task A_club_whose_owner_is_deactivated_takes_no_series()
    {
        var (club, pitch) = await VenueAsync();
        await factory.DeactivateAccountAsync(club.OwnerId);

        using var response = await SendAsync(AppAt(MayClock), HttpMethod.Post, CreatePath, await PlayerAsync(), new { pitchId = pitch.Id, startsAt = FirstMonday, weeks = 2 });

        await ProblemAsync(response, HttpStatusCode.UnprocessableEntity, ErrorCodes.ClubUnavailable);
        (await BookingsOfAsync(pitch.Id)).ShouldBeEmpty();
    }

    // ---- Afterwards

    [Fact]
    public async Task Cancelling_one_booking_of_a_series_leaves_the_others_and_frees_its_slot()
    {
        var (club, pitch) = await VenueAsync();
        var app = AppAt(MayClock);
        var player = await PlayerAsync();
        var body = await OkAsync(await SendAsync(app, HttpMethod.Post, CreatePath, player, new { pitchId = pitch.Id, startsAt = FirstMonday, weeks = 3 }), HttpStatusCode.Created);
        var second = body.GetProperty("created")[1].GetProperty("id").GetGuid();

        (await OkAsync(await SendAsync(app, HttpMethod.Post, $"/api/bookings/{second}/cancel", player))).GetProperty("status").GetString().ShouldBe("cancelled_by_player");

        var stored = await BookingsOfAsync(pitch.Id);
        stored.Select(booking => booking.Status).ShouldBe([BookingStatus.Confirmed, BookingStatus.CancelledByPlayer, BookingStatus.Confirmed]);
        using var slots = await app.CreateClient().GetAsync($"/api/clubs/{club.Slug}/pitches/{pitch.Id}/slots?date=2026-05-11", Ct);
        var free = (await slots.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("slots")[1];
        Utc(free.GetProperty("startsAt")).ShouldBe("05-11 09:00");
        free.GetProperty("available").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_players_list_and_details_say_which_series_a_booking_is_in_and_where()
    {
        var (_, pitch) = await VenueAsync();
        var app = AppAt(MayClock);
        var player = await PlayerAsync();
        await factory.AddBookingAsync(pitch.Id, TestVenues.Utc("2026-05-11T09:00:00Z"), TestVenues.Utc("2026-05-11T10:00:00Z"));
        var body = await OkAsync(await SendAsync(app, HttpMethod.Post, CreatePath, player, new { pitchId = pitch.Id, startsAt = FirstMonday, weeks = 3 }), HttpStatusCode.Created);
        var seriesId = body.GetProperty("seriesId").GetGuid();
        var single = await OkAsync(await SendAsync(app, HttpMethod.Post, "/api/bookings", player, new { pitchId = pitch.Id, startsAt = "2026-05-25T09:00:00Z" }), HttpStatusCode.Created);

        var list = (await OkAsync(await SendAsync(app, HttpMethod.Get, "/api/me/bookings?scope=upcoming", player))).GetProperty("items").EnumerateArray().ToList();

        // Two of the three dates were made (11 May was taken): "1 of 2" and "2 of 2"; the single booking has no series.
        list.Select(item => item.GetProperty("series").ValueKind == JsonValueKind.Null
                ? "-"
                : $"{item.GetProperty("series").GetProperty("position").GetInt32()}/{item.GetProperty("series").GetProperty("count").GetInt32()}")
            .ShouldBe(["1/2", "2/2", "-"]);
        list[0].GetProperty("series").GetProperty("id").GetGuid().ShouldBe(seriesId);
        var details = await OkAsync(await SendAsync(app, HttpMethod.Get, $"/api/bookings/{body.GetProperty("created")[1].GetProperty("id").GetGuid()}", player));
        details.GetProperty("series").GetProperty("position").GetInt32().ShouldBe(2);
        details.GetProperty("series").GetProperty("id").GetGuid().ShouldBe(seriesId);
        (await OkAsync(await SendAsync(app, HttpMethod.Get, $"/api/bookings/{single.GetProperty("id").GetGuid()}", player))).GetProperty("series").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("Admin")]
    public async Task An_owner_or_the_admin_is_a_403(string profile)
    {
        var (_, pitch) = await VenueAsync();
        var (_, tokens) = await factory.CreateSignedInAsync(profile);

        foreach (var path in new[] { PreviewPath, CreatePath })
        {
            using var response = await SendAsync(AppAt(MayClock), HttpMethod.Post, path, tokens.AccessToken, new { pitchId = pitch.Id, startsAt = FirstMonday, weeks = 2 });
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        }

        (await BookingsOfAsync(pitch.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_session_it_is_a_401()
    {
        var (_, pitch) = await VenueAsync();

        foreach (var path in new[] { PreviewPath, CreatePath })
        {
            using var response = await SendAsync(AppAt(MayClock), HttpMethod.Post, path, token: null, new { pitchId = pitch.Id, startsAt = FirstMonday, weeks = 2 });
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, path);
        }
    }
}
