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

/// <summary>US15: an owner marks a booking whose end has passed as a no-show, and takes it back.</summary>
public sealed class NoShowTests(ApiFactory factory)
{
    // Monday 4 May 2026, 09:00-10:00 in Lisbon (WEST) = 08:00-09:00 UTC.
    private const string Start = "2026-05-04T08:00:00Z";
    private const string End = "2026-05-04T09:00:00Z";
    private const string AfterTheEnd = "2026-05-04T09:30:00Z";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> AppAt(string utcNow) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private static string BookingPath(Guid clubId, Guid bookingId, string suffix = "") => $"/api/manage/clubs/{clubId}/bookings/{bookingId}{suffix}";

    private static async Task<HttpResponseMessage> SendAsync(WebApplicationFactory<Program> app, HttpMethod method, string path, string? accessToken)
    {
        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(method, path);
        if (accessToken is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, accessToken));
        }

        return await client.SendAsync(request, Ct);
    }

    private static Task<HttpResponseMessage> MarkAsync(WebApplicationFactory<Program> app, Guid clubId, Guid bookingId, string? token) =>
        SendAsync(app, HttpMethod.Post, BookingPath(clubId, bookingId, "/no-show"), token);

    private static Task<HttpResponseMessage> UndoAsync(WebApplicationFactory<Program> app, Guid clubId, Guid bookingId, string? token) =>
        SendAsync(app, HttpMethod.Post, BookingPath(clubId, bookingId, "/no-show/undo"), token);

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

    private sealed record Setup(TestClub Club, TestPitch Pitch, string OwnerToken, Guid OwnerId, AppUser Player, Guid BookingId);

    private async Task<Setup> BookedAsync(string start = Start, string end = End)
    {
        var (owner, ownerTokens) = await factory.CreateSignedInAsync(Profiles.Owner, "Dono do Clube");
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id);
        var player = await factory.CreateAccountAsync(Profiles.Player, password: null, name: "Rita Jogadora");
        var booking = await factory.AddBookingAsync(pitch.Id, TestVenues.Utc(start), TestVenues.Utc(end), player);
        return new Setup(club, pitch, ownerTokens.AccessToken, owner.Id, player, booking.Id);
    }

    private async Task<string> StatusAsync(Guid bookingId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking().SingleAsync(booking => booking.Id == bookingId, Ct)).Status.ToString();
    }

    // ---- Marking

    [Theory]
    [InlineData("2026-05-04T08:00:00Z")] // starting
    [InlineData("2026-05-04T08:59:59Z")] // one second before the end
    [InlineData("2026-05-03T10:00:00Z")] // the day before
    public async Task A_booking_that_has_not_ended_is_refused_with_422_booking_not_ended(string now)
    {
        await using var app = AppAt(now);
        var setup = await BookedAsync();

        await ProblemAsync(await MarkAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.UnprocessableEntity, "booking_not_ended");

        (await StatusAsync(setup.BookingId)).ShouldBe("Confirmed");
    }

    [Theory]
    [InlineData("2026-05-04T09:00:00Z")] // the end itself counts as ended
    [InlineData(AfterTheEnd)]
    [InlineData("2026-06-04T09:00:00Z")]
    public async Task A_confirmed_booking_whose_end_has_passed_becomes_a_no_show(string now)
    {
        await using var app = AppAt(now);
        var setup = await BookedAsync();

        var booking = await JsonAsync(await MarkAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.OK);

        booking.GetProperty("id").GetGuid().ShouldBe(setup.BookingId);
        booking.GetProperty("status").GetString().ShouldBe("no_show");
        booking.GetProperty("canMarkNoShow").GetBoolean().ShouldBeFalse();
        booking.GetProperty("canUndoNoShow").GetBoolean().ShouldBeTrue();
        booking.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
        (await StatusAsync(setup.BookingId)).ShouldBe("NoShow");
    }

    [Fact]
    public async Task The_details_say_whether_a_booking_can_be_marked_or_taken_back()
    {
        var setup = await BookedAsync();
        await using var before = AppAt("2026-05-04T08:30:00Z");
        await using var after = AppAt(AfterTheEnd);

        var inProgress = await JsonAsync(await SendAsync(before, HttpMethod.Get, BookingPath(setup.Club.Id, setup.BookingId), setup.OwnerToken), HttpStatusCode.OK);
        var ended = await JsonAsync(await SendAsync(after, HttpMethod.Get, BookingPath(setup.Club.Id, setup.BookingId), setup.OwnerToken), HttpStatusCode.OK);

        inProgress.GetProperty("canMarkNoShow").GetBoolean().ShouldBeFalse();
        inProgress.GetProperty("canUndoNoShow").GetBoolean().ShouldBeFalse();
        ended.GetProperty("canMarkNoShow").GetBoolean().ShouldBeTrue();
        ended.GetProperty("canUndoNoShow").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_cancelled_booking_or_one_already_marked_is_refused_with_422_booking_not_confirmed()
    {
        await using var app = AppAt(AfterTheEnd);
        var byPlayer = await BookedAsync();
        var byClub = await BookedAsync();
        var marked = await BookedAsync();
        await factory.SetBookingStatusAsync(byPlayer.BookingId, "CancelledByPlayer");
        await factory.SetBookingStatusAsync(byClub.BookingId, "CancelledByClub");
        (await JsonAsync(await MarkAsync(app, marked.Club.Id, marked.BookingId, marked.OwnerToken), HttpStatusCode.OK)).GetProperty("status").GetString().ShouldBe("no_show");

        foreach (var setup in new[] { byPlayer, byClub, marked })
        {
            await ProblemAsync(await MarkAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.UnprocessableEntity, "booking_not_confirmed");
        }

        (await StatusAsync(byPlayer.BookingId)).ShouldBe("CancelledByPlayer");
        (await StatusAsync(byClub.BookingId)).ShouldBe("CancelledByClub");
    }

    [Fact]
    public async Task Not_confirmed_comes_before_not_ended()
    {
        await using var app = AppAt("2026-05-04T08:30:00Z");
        var setup = await BookedAsync();
        await factory.SetBookingStatusAsync(setup.BookingId, "CancelledByPlayer");

        await ProblemAsync(await MarkAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.UnprocessableEntity, "booking_not_confirmed");
    }

    [Fact]
    public async Task A_customers_booking_can_be_marked_too_and_has_no_count_per_person()
    {
        await using var app = AppAt(AfterTheEnd);
        var (owner, ownerTokens) = await factory.CreateSignedInAsync(Profiles.Owner, "Dono do Clube");
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id);
        var booking = await factory.AddCustomerBookingAsync(pitch.Id, TestVenues.Utc(Start), TestVenues.Utc(End), "Cliente da Rua", "912345678");

        var marked = await JsonAsync(await MarkAsync(app, club.Id, booking.Id, ownerTokens.AccessToken), HttpStatusCode.OK);

        marked.GetProperty("status").GetString().ShouldBe("no_show");
        marked.GetProperty("createdByClub").GetBoolean().ShouldBeTrue();
        marked.GetProperty("playerNoShows").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ---- Undoing

    [Fact]
    public async Task A_no_show_can_be_taken_back_and_marked_again()
    {
        await using var app = AppAt(AfterTheEnd);
        var setup = await BookedAsync();
        (await MarkAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken)).Dispose();

        var undone = await JsonAsync(await UndoAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.OK);

        undone.GetProperty("status").GetString().ShouldBe("confirmed");
        undone.GetProperty("canMarkNoShow").GetBoolean().ShouldBeTrue();
        undone.GetProperty("canUndoNoShow").GetBoolean().ShouldBeFalse();
        undone.GetProperty("playerNoShows").GetInt32().ShouldBe(0);
        (await StatusAsync(setup.BookingId)).ShouldBe("Confirmed");
        (await JsonAsync(await MarkAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.OK)).GetProperty("status").GetString().ShouldBe("no_show");
    }

    [Fact]
    public async Task Only_a_no_show_can_be_taken_back_422_booking_not_no_show()
    {
        await using var app = AppAt(AfterTheEnd);
        var confirmed = await BookedAsync();
        var cancelled = await BookedAsync();
        await factory.SetBookingStatusAsync(cancelled.BookingId, "CancelledByClub");

        await ProblemAsync(await UndoAsync(app, confirmed.Club.Id, confirmed.BookingId, confirmed.OwnerToken), HttpStatusCode.UnprocessableEntity, "booking_not_no_show");
        await ProblemAsync(await UndoAsync(app, cancelled.Club.Id, cancelled.BookingId, cancelled.OwnerToken), HttpStatusCode.UnprocessableEntity, "booking_not_no_show");

        (await StatusAsync(cancelled.BookingId)).ShouldBe("CancelledByClub");
    }

    [Fact]
    public async Task Taking_a_no_show_back_when_another_confirmed_booking_took_the_slot_is_409_not_500()
    {
        await using var app = AppAt(AfterTheEnd);
        var setup = await BookedAsync();
        (await MarkAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken)).Dispose();
        // The slot is free (a no-show does not hold it), so someone books it, and the exclusion constraint now stands in the way.
        var other = await factory.AddBookingAsync(setup.Pitch.Id, TestVenues.Utc(Start), TestVenues.Utc(End));

        await ProblemAsync(await UndoAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.Conflict, "slot_taken");

        (await StatusAsync(setup.BookingId)).ShouldBe("NoShow");
        (await StatusAsync(other.Id)).ShouldBe("Confirmed");
        // And nothing is left half done: the next request on the pitch works.
        (await JsonAsync(await SendAsync(app, HttpMethod.Get, BookingPath(setup.Club.Id, setup.BookingId), setup.OwnerToken), HttpStatusCode.OK))
            .GetProperty("status").GetString().ShouldBe("no_show");
    }

    // ---- The player's count

    [Fact]
    public async Task The_details_count_the_players_no_shows_in_all_of_this_owners_clubs_and_not_another_owners()
    {
        await using var app = AppAt("2026-06-01T10:00:00Z");
        var (owner, ownerTokens) = await factory.CreateSignedInAsync(Profiles.Owner, "Dono do Clube");
        var firstClub = await factory.CreateClubAsync(owner.Id);
        var secondClub = await factory.CreateClubAsync(owner.Id);
        var firstPitch = await factory.CreatePitchAsync(firstClub.Id);
        var secondPitch = await factory.CreatePitchAsync(secondClub.Id);
        var otherOwnersPitch = await factory.CreatePitchAsync((await factory.CreateClubAsync()).Id);
        var player = await factory.CreateAccountAsync(Profiles.Player, password: null, name: "Rita Jogadora");
        var anotherPlayer = await factory.CreateAccountAsync(Profiles.Player, password: null, name: "Rui Jogador");

        var bookings = new List<Guid>();
        foreach (var (pitch, day) in new[] { (firstPitch, 4), (firstPitch, 5), (secondPitch, 6), (otherOwnersPitch, 7) })
        {
            var start = new DateTime(2026, 5, day, 8, 0, 0, DateTimeKind.Utc);
            var booking = await factory.AddBookingAsync(pitch.Id, start, start.AddHours(1), player);
            await factory.SetBookingStatusAsync(booking.Id, "NoShow");
            bookings.Add(booking.Id);
        }

        // Not counted: the player's cancelled and confirmed bookings, and another player's no-show.
        var cancelled = await factory.AddBookingAsync(firstPitch.Id, new DateTime(2026, 5, 8, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 8, 9, 0, 0, DateTimeKind.Utc), player);
        await factory.SetBookingStatusAsync(cancelled.Id, "CancelledByPlayer");
        await factory.AddBookingAsync(secondPitch.Id, new DateTime(2026, 5, 9, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 9, 9, 0, 0, DateTimeKind.Utc), player);
        var others = await factory.AddBookingAsync(firstPitch.Id, new DateTime(2026, 5, 10, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 10, 9, 0, 0, DateTimeKind.Utc), anotherPlayer);
        await factory.SetBookingStatusAsync(others.Id, "NoShow");

        var details = await JsonAsync(await SendAsync(app, HttpMethod.Get, BookingPath(firstClub.Id, bookings[0]), ownerTokens.AccessToken), HttpStatusCode.OK);
        var fromTheOtherClub = await JsonAsync(await SendAsync(app, HttpMethod.Get, BookingPath(secondClub.Id, bookings[2]), ownerTokens.AccessToken), HttpStatusCode.OK);
        var anotherPlayersDetails = await JsonAsync(await SendAsync(app, HttpMethod.Get, BookingPath(firstClub.Id, others.Id), ownerTokens.AccessToken), HttpStatusCode.OK);

        // Three at this owner's two clubs; the fourth, at another owner's club, is not theirs to count.
        details.GetProperty("playerNoShows").GetInt32().ShouldBe(3);
        fromTheOtherClub.GetProperty("playerNoShows").GetInt32().ShouldBe(3);
        anotherPlayersDetails.GetProperty("playerNoShows").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Marking_updates_the_count_in_the_answer()
    {
        await using var app = AppAt("2026-06-01T10:00:00Z");
        var setup = await BookedAsync();
        var earlier = await factory.AddBookingAsync(setup.Pitch.Id, new DateTime(2026, 5, 5, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 5, 9, 0, 0, DateTimeKind.Utc), setup.Player);
        await factory.SetBookingStatusAsync(earlier.Id, "NoShow");

        var marked = await JsonAsync(await MarkAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.OK);

        marked.GetProperty("playerNoShows").GetInt32().ShouldBe(2);
    }

    // ---- Access

    [Fact]
    public async Task Another_owners_club_and_a_booking_of_another_club_are_404_like_everything_else_in_the_owner_area()
    {
        await using var app = AppAt(AfterTheEnd);
        var setup = await BookedAsync();
        var (otherOwner, otherTokens) = await factory.CreateSignedInAsync(Profiles.Owner, "Outro Dono");
        var otherClub = await factory.CreateClubAsync(otherOwner.Id);

        // Their token, my club.
        await ProblemAsync(await MarkAsync(app, setup.Club.Id, setup.BookingId, otherTokens.AccessToken), HttpStatusCode.NotFound, "club_not_found");
        await ProblemAsync(await UndoAsync(app, setup.Club.Id, setup.BookingId, otherTokens.AccessToken), HttpStatusCode.NotFound, "club_not_found");
        // Their club, my booking.
        await ProblemAsync(await MarkAsync(app, otherClub.Id, setup.BookingId, otherTokens.AccessToken), HttpStatusCode.NotFound, "booking_not_found");
        await ProblemAsync(await UndoAsync(app, otherClub.Id, setup.BookingId, otherTokens.AccessToken), HttpStatusCode.NotFound, "booking_not_found");
        // An unknown booking.
        await ProblemAsync(await MarkAsync(app, setup.Club.Id, Guid.CreateVersion7(), setup.OwnerToken), HttpStatusCode.NotFound, "booking_not_found");

        (await StatusAsync(setup.BookingId)).ShouldBe("Confirmed");
    }

    [Theory]
    [InlineData(Profiles.Player)]
    [InlineData(Profiles.Admin)]
    public async Task Another_profile_gets_403_and_no_session_401(string profile)
    {
        await using var app = AppAt(AfterTheEnd);
        var setup = await BookedAsync();
        var (_, tokens) = await factory.CreateSignedInAsync(profile);

        using (var forbidden = await MarkAsync(app, setup.Club.Id, setup.BookingId, tokens.AccessToken))
        {
            forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var forbiddenUndo = await UndoAsync(app, setup.Club.Id, setup.BookingId, tokens.AccessToken))
        {
            forbiddenUndo.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using var anonymous = await MarkAsync(app, setup.Club.Id, setup.BookingId, token: null);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await StatusAsync(setup.BookingId)).ShouldBe("Confirmed");
    }

    // ---- Concurrency

    [Fact]
    public async Task Two_marks_at_the_same_moment_give_one_200_and_one_422()
    {
        await using var app = AppAt(AfterTheEnd);
        var setup = await BookedAsync();

        var responses = await Task.WhenAll(
            MarkAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken),
            MarkAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken));

        responses.Select(response => response.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.UnprocessableEntity]);
        foreach (var response in responses)
        {
            response.Dispose();
        }

        (await StatusAsync(setup.BookingId)).ShouldBe("NoShow");
    }
}
