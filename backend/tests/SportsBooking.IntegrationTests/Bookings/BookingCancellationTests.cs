using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Bookings;

/// <summary>US19: a player cancels a booking before the club's cancellation window closes; the slot is free again.</summary>
public sealed class BookingCancellationTests(ApiFactory factory)
{
    // The booking starts on Wednesday 6 May 2026 at 10:00 UTC; with the default 24-hour window the deadline is
    // Tuesday 5 May at 10:00 UTC.
    private const string Start = "2026-05-06T10:00:00Z";
    private const string Deadline = "2026-05-05T10:00:00Z";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> AppAt(string utcNow) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private static async Task<HttpResponseMessage> PostAsync(WebApplicationFactory<Program> app, string path, string? accessToken, object? body = null)
    {
        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (accessToken is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, accessToken));
        }

        return await client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> CancelAsync(WebApplicationFactory<Program> app, Guid bookingId, string? accessToken) =>
        PostAsync(app, $"/api/bookings/{bookingId}/cancel", accessToken);

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

    private async Task<string> StatusOfAsync(Guid bookingId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM bookings WHERE id = {bookingId}")
            .SingleAsync(Ct);
    }

    private async Task<int> BookingRowsAsync(Guid pitchId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.CountAsync(booking => booking.PitchId == pitchId, Ct);
    }

    /// <summary>A club (24-hour window), a pitch and a player with a confirmed booking at <see cref="Start"/>.</summary>
    private async Task<(TestClub Club, TestPitch Pitch, AppUser Player, string Token, Guid BookingId)> BookedAsync(string start = Start)
    {
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        var signedIn = await factory.CreateSignedInPlayerAsync();
        var booking = await factory.AddBookingAsync(pitch.Id, TestVenues.Utc(start), TestVenues.Utc(start).AddHours(1), signedIn.User);
        return (club, pitch, signedIn.User, signedIn.Tokens.AccessToken, booking.Id);
    }

    // ---- A deactivated owner's club (US29)

    [Fact]
    public async Task A_player_can_still_cancel_at_a_deactivated_owners_club_inside_the_normal_window_and_not_after_it()
    {
        var (club, pitch, _, token, bookingId) = await BookedAsync();
        var (_, lateToken, lateBookingId) = await BookedAtAsync(pitch.Id, "2026-05-07T10:00:00Z");
        await factory.DeactivateAccountAsync(club.OwnerId);

        // Deadline 5 May 10:00 UTC: just before it the cancel works, whoever the owner is now.
        await using (var before = AppAt("2026-05-05T09:59:59Z"))
        {
            var cancelled = await JsonAsync(await CancelAsync(before, bookingId, token), HttpStatusCode.OK);
            cancelled.GetProperty("status").GetString().ShouldBe("cancelled_by_player");
        }

        (await StatusOfAsync(bookingId)).ShouldBe("CancelledByPlayer");

        // The window is the club's normal one: the second booking (deadline 6 May 10:00 UTC) is refused after it, as at any club.
        await using var after = AppAt("2026-05-06T10:00:00Z");
        var problem = await ProblemAsync(await CancelAsync(after, lateBookingId, lateToken), HttpStatusCode.UnprocessableEntity, "outside_cancellation_window");
        problem.GetProperty("cancellationDeadline").GetDateTime().ShouldBe(TestVenues.Utc("2026-05-06T10:00:00Z"));
        (await StatusOfAsync(lateBookingId)).ShouldBe("Confirmed");
    }

    private async Task<(AppUser Player, string Token, Guid BookingId)> BookedAtAsync(Guid pitchId, string start)
    {
        var signedIn = await factory.CreateSignedInPlayerAsync();
        var booking = await factory.AddBookingAsync(pitchId, TestVenues.Utc(start), TestVenues.Utc(start).AddHours(1), signedIn.User);
        return (signedIn.User, signedIn.Tokens.AccessToken, booking.Id);
    }

    // ---- The deadline

    [Fact]
    public async Task Cancelling_just_before_the_deadline_works_and_the_booking_is_kept_as_cancelled_by_the_player()
    {
        await using var app = AppAt("2026-05-05T09:59:59Z");
        var (_, pitch, _, token, bookingId) = await BookedAsync();

        var cancelled = await JsonAsync(await CancelAsync(app, bookingId, token), HttpStatusCode.OK);

        cancelled.GetProperty("id").GetGuid().ShouldBe(bookingId);
        cancelled.GetProperty("status").GetString().ShouldBe("cancelled_by_player");
        cancelled.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
        cancelled.GetProperty("cancellationDeadline").ValueKind.ShouldBe(JsonValueKind.Null);
        cancelled.GetProperty("price").GetDecimal().ShouldBe(20m);
        (await StatusOfAsync(bookingId)).ShouldBe("CancelledByPlayer");
        (await BookingRowsAsync(pitch.Id)).ShouldBe(1); // never deleted
    }

    [Theory]
    [InlineData("2026-05-05T10:00:00Z")] // exactly at the deadline: provisional, "strictly before"
    [InlineData("2026-05-05T10:00:01Z")]
    [InlineData("2026-05-06T09:00:00Z")] // an hour before the start
    [InlineData("2026-05-06T10:00:00Z")] // starting now
    [InlineData("2026-05-06T12:00:00Z")] // over
    public async Task At_and_after_the_deadline_it_is_422_with_the_deadline_and_the_booking_stays_confirmed(string now)
    {
        await using var app = AppAt(now);
        var (_, _, _, token, bookingId) = await BookedAsync();

        var problem = await ProblemAsync(await CancelAsync(app, bookingId, token), HttpStatusCode.UnprocessableEntity, "outside_cancellation_window");

        problem.GetProperty("cancellationDeadline").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc(Deadline));
        problem.GetProperty("detail").GetString().ShouldBe("Só é possível cancelar até 24 horas antes do início, e esse prazo já passou.");
        (await StatusOfAsync(bookingId)).ShouldBe("Confirmed");
    }

    [Fact]
    public async Task The_deadline_is_a_utc_instant_in_the_response()
    {
        await using var app = AppAt("2026-05-05T12:00:00Z");
        var (_, _, _, token, bookingId) = await BookedAsync();

        using var response = await CancelAsync(app, bookingId, token);

        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("\"cancellationDeadline\":\"2026-05-05T10:00:00Z\"");
    }

    [Fact]
    public async Task A_window_changed_after_booking_applies_the_current_value_both_ways()
    {
        // "Now" is 30 hours before the start: cancelling is allowed with a 24-hour window, and with 6, but not once the
        // club has widened it to 48 (the deadline is then 48 hours before the start, which has passed).
        await using var app = AppAt("2026-05-05T04:00:00Z");
        var (club, _, _, token, bookingId) = await BookedAsync();

        await factory.ChangeCancellationWindowAsync(club.Id, 48);
        var widened = await ProblemAsync(await CancelAsync(app, bookingId, token), HttpStatusCode.UnprocessableEntity, "outside_cancellation_window");
        widened.GetProperty("cancellationDeadline").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc("2026-05-04T10:00:00Z"));
        widened.GetProperty("detail").GetString().ShouldNotBeNull().ShouldContain("48 horas");
        (await StatusOfAsync(bookingId)).ShouldBe("Confirmed");

        await factory.ChangeCancellationWindowAsync(club.Id, 6);
        await JsonAsync(await CancelAsync(app, bookingId, token), HttpStatusCode.OK);
        (await StatusOfAsync(bookingId)).ShouldBe("CancelledByPlayer");
    }

    [Fact]
    public async Task A_one_hour_window_says_hora_in_the_singular()
    {
        await using var app = AppAt("2026-05-06T09:30:00Z");
        var (club, _, _, token, bookingId) = await BookedAsync();
        await factory.ChangeCancellationWindowAsync(club.Id, 1);

        var problem = await ProblemAsync(await CancelAsync(app, bookingId, token), HttpStatusCode.UnprocessableEntity, "outside_cancellation_window");

        problem.GetProperty("detail").GetString().ShouldBe("Só é possível cancelar até 1 hora antes do início, e esse prazo já passou.");
    }

    // ---- The slot is free again

    [Fact]
    public async Task A_cancelled_slot_is_free_in_the_slot_list_and_can_be_booked_again_by_another_player()
    {
        // Friday 1 May 2026, 11:00 in Lisbon. Monday 4 May is open 09:00-13:00: the slot 08:00-09:00 UTC.
        await using var app = AppAt("2026-05-01T10:00:00Z");
        var club = await factory.CreateClubAsync();
        var pitch = await factory.CreatePitchAsync(club.Id);
        var first = await factory.CreateSignedInPlayerAsync();
        var second = await factory.CreateSignedInPlayerAsync();
        var slot = new { pitchId = pitch.Id, startsAt = "2026-05-04T08:00:00Z" };

        var booked = await JsonAsync(await PostAsync(app, "/api/bookings", first.Tokens.AccessToken, slot), HttpStatusCode.Created);
        await ProblemAsync(await PostAsync(app, "/api/bookings", second.Tokens.AccessToken, slot), HttpStatusCode.Conflict, "slot_taken");
        var bookingId = booked.GetProperty("id").GetGuid();

        await JsonAsync(await CancelAsync(app, bookingId, first.Tokens.AccessToken), HttpStatusCode.OK);

        using var client = app.CreateClient();
        var slots = JsonDocument.Parse(await client.GetStringAsync($"/api/clubs/{club.Slug}/pitches/{pitch.Id}/slots?date=2026-05-04", Ct)).RootElement;
        slots.GetProperty("slots")[0].GetProperty("available").GetBoolean().ShouldBeTrue();
        var again = await JsonAsync(await PostAsync(app, "/api/bookings", second.Tokens.AccessToken, slot), HttpStatusCode.Created);
        again.GetProperty("id").GetGuid().ShouldNotBe(bookingId);
        // The first booking is still there, cancelled, next to the new one.
        (await StatusOfAsync(bookingId)).ShouldBe("CancelledByPlayer");
        (await BookingRowsAsync(pitch.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task A_cancelled_booking_moves_from_upcoming_to_past_in_my_bookings()
    {
        await using var app = AppAt("2026-05-05T09:00:00Z");
        var (_, _, _, token, bookingId) = await BookedAsync();
        await JsonAsync(await CancelAsync(app, bookingId, token), HttpStatusCode.OK);

        using var client = app.CreateSiteClient();
        async Task<JsonElement> ListAsync(string scope)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/me/bookings?scope={scope}");
            request.WithCookies((SessionCookies.AccessToken, token));
            return await JsonAsync(await client.SendAsync(request, Ct), HttpStatusCode.OK);
        }

        (await ListAsync("upcoming")).GetProperty("totalCount").GetInt32().ShouldBe(0);
        var past = await ListAsync("past");
        past.GetProperty("items").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("cancelled_by_player");
    }

    // ---- Whose booking, and what state it is in

    [Fact]
    public async Task Another_players_booking_and_an_unknown_id_are_404_and_the_booking_is_untouched()
    {
        await using var app = AppAt("2026-05-05T09:00:00Z");
        var (_, _, _, _, bookingId) = await BookedAsync();
        var stranger = await factory.CreateSignedInPlayerAsync();

        await ProblemAsync(await CancelAsync(app, bookingId, stranger.Tokens.AccessToken), HttpStatusCode.NotFound, "booking_not_found");
        await ProblemAsync(await CancelAsync(app, Guid.CreateVersion7(), stranger.Tokens.AccessToken), HttpStatusCode.NotFound, "booking_not_found");

        (await StatusOfAsync(bookingId)).ShouldBe("Confirmed");
    }

    [Fact]
    public async Task Cancelling_twice_is_422_booking_not_confirmed_even_after_the_deadline()
    {
        await using var early = AppAt("2026-05-05T09:00:00Z");
        var (_, _, _, token, bookingId) = await BookedAsync();
        await JsonAsync(await CancelAsync(early, bookingId, token), HttpStatusCode.OK);

        await ProblemAsync(await CancelAsync(early, bookingId, token), HttpStatusCode.UnprocessableEntity, "booking_not_confirmed");

        // "Not confirmed" wins over "too late": the answer does not depend on the clock.
        await using var late = AppAt("2026-05-07T09:00:00Z");
        await ProblemAsync(await CancelAsync(late, bookingId, token), HttpStatusCode.UnprocessableEntity, "booking_not_confirmed");
        (await StatusOfAsync(bookingId)).ShouldBe("CancelledByPlayer");
    }

    [Theory]
    [InlineData("CancelledByClub")]
    [InlineData("NoShow")]
    public async Task A_booking_that_is_not_confirmed_cannot_be_cancelled_and_keeps_its_status(string status)
    {
        await using var app = AppAt("2026-05-05T09:00:00Z");
        var (_, _, _, token, bookingId) = await BookedAsync();
        await factory.SetBookingStatusAsync(bookingId, status);

        await ProblemAsync(await CancelAsync(app, bookingId, token), HttpStatusCode.UnprocessableEntity, "booking_not_confirmed");

        (await StatusOfAsync(bookingId)).ShouldBe(status);
    }

    [Fact]
    public async Task Concurrent_cancels_of_one_booking_give_one_200_and_the_rest_422_never_a_500()
    {
        await using var app = AppAt("2026-05-05T09:00:00Z");
        var (_, pitch, _, token, bookingId) = await BookedAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => CancelAsync(app, bookingId, token), Ct)));

        var statuses = responses.Select(response => response.StatusCode).ToList();
        statuses.Count(status => status == HttpStatusCode.OK).ShouldBe(1, string.Join(", ", statuses));
        statuses.Count(status => status == HttpStatusCode.UnprocessableEntity).ShouldBe(11, string.Join(", ", statuses));
        foreach (var response in responses.Where(response => response.StatusCode == HttpStatusCode.UnprocessableEntity))
        {
            (await JsonAsync(response, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().ShouldBe("booking_not_confirmed");
        }

        (await StatusOfAsync(bookingId)).ShouldBe("CancelledByPlayer");
        (await BookingRowsAsync(pitch.Id)).ShouldBe(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // ---- Who may ask

    [Fact]
    public async Task Owners_and_admins_get_403_and_visitors_401_and_nothing_changes()
    {
        await using var app = AppAt("2026-05-05T09:00:00Z");
        var (_, _, _, _, bookingId) = await BookedAsync();
        var owner = await factory.CreateSignedInAsync(Profiles.Owner);
        var admin = await factory.CreateSignedInAsync(Profiles.Admin);

        await ProblemAsync(await CancelAsync(app, bookingId, owner.Tokens.AccessToken), HttpStatusCode.Forbidden, "forbidden");
        await ProblemAsync(await CancelAsync(app, bookingId, admin.Tokens.AccessToken), HttpStatusCode.Forbidden, "forbidden");
        await ProblemAsync(await CancelAsync(app, bookingId, accessToken: null), HttpStatusCode.Unauthorized, "unauthorized");

        (await StatusOfAsync(bookingId)).ShouldBe("Confirmed");
    }
}
