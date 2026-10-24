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

/// <summary>US11 and US12: an owner reads and cancels a booking of their own club, through the T02 mechanism.</summary>
public sealed class ClubBookingTests(ApiFactory factory)
{
    // Monday 4 May 2026, 09:00 in Lisbon (WEST). The clock is the Saturday before.
    private const string Start = "2026-05-04T08:00:00Z";
    private const string End = "2026-05-04T09:00:00Z";
    private const string Now = "2026-05-02T10:00:00Z";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> AppAt(string utcNow) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture)))));

    private static string BookingPath(Guid clubId, Guid bookingId, string suffix = "") => $"/api/manage/clubs/{clubId}/bookings/{bookingId}{suffix}";

    private static async Task<HttpResponseMessage> SendAsync(
        WebApplicationFactory<Program> app,
        HttpMethod method,
        string path,
        string? accessToken,
        object? body = null)
    {
        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(method, path);
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

    private static Task<HttpResponseMessage> GetBookingAsync(WebApplicationFactory<Program> app, Guid clubId, Guid bookingId, string? token) =>
        SendAsync(app, HttpMethod.Get, BookingPath(clubId, bookingId), token);

    private static Task<HttpResponseMessage> OwnerCancelAsync(WebApplicationFactory<Program> app, Guid clubId, Guid bookingId, string? token, object? body = null) =>
        SendAsync(app, HttpMethod.Post, BookingPath(clubId, bookingId, "/cancel"), token, body ?? new { reason = "Campo em obras" });

    private static Task<HttpResponseMessage> PlayerCancelAsync(WebApplicationFactory<Program> app, Guid bookingId, string token) =>
        SendAsync(app, HttpMethod.Post, $"/api/bookings/{bookingId}/cancel", token);

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

    private sealed record Setup(TestClub Club, TestPitch Pitch, string OwnerToken, AppUser Player, string PlayerToken, Guid BookingId);

    /// <summary>An owner with a club (24-hour window) and a pitch; a player with a confirmed booking at <see cref="Start"/>.</summary>
    private async Task<Setup> BookedAsync(string start = Start, string end = End)
    {
        var (owner, ownerTokens) = await factory.CreateSignedInAsync(Profiles.Owner, "Dono do Clube");
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id);
        var player = await factory.CreateSignedInPlayerAsync("Rita Jogadora");
        var booking = await factory.AddBookingAsync(pitch.Id, TestVenues.Utc(start), TestVenues.Utc(end), player.User);
        return new Setup(club, pitch, ownerTokens.AccessToken, player.User, player.Tokens.AccessToken, booking.Id);
    }

    private async Task<(string Status, string? Reason, DateTime? CancelledAt)> StoredAsync(Guid bookingId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var booking = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == bookingId, Ct);
        return (booking.Status.ToString(), booking.CancellationReason, booking.CancelledAt);
    }

    // ---- Details

    [Fact]
    public async Task The_details_have_the_booking_and_the_players_name_and_nothing_else_about_the_player()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();

        using var response = await GetBookingAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken);
        var text = await response.Content.ReadAsStringAsync(Ct);
        var booking = JsonDocument.Parse(text).RootElement;

        response.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        booking.GetProperty("id").GetGuid().ShouldBe(setup.BookingId);
        booking.GetProperty("pitchId").GetGuid().ShouldBe(setup.Pitch.Id);
        booking.GetProperty("pitchName").GetString().ShouldBe("Campo 1");
        booking.GetProperty("sport").GetString().ShouldBe("padel");
        booking.GetProperty("startsAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc(Start));
        booking.GetProperty("endsAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc(End));
        booking.GetProperty("price").GetDecimal().ShouldBe(20m);
        booking.GetProperty("status").GetString().ShouldBe("confirmed");
        booking.GetProperty("playerName").GetString().ShouldBe("Rita Jogadora");
        booking.GetProperty("canCancel").GetBoolean().ShouldBeTrue();
        // A player's booking: not made by the club, and no phone (US13 only shows the number the owner typed for a customer).
        booking.GetProperty("createdByClub").GetBoolean().ShouldBeFalse();
        booking.GetProperty("customerPhone").ValueKind.ShouldBe(JsonValueKind.Null);
        booking.GetProperty("cancelledAt").ValueKind.ShouldBe(JsonValueKind.Null);
        booking.GetProperty("cancellationReason").ValueKind.ShouldBe(JsonValueKind.Null);
        text.ShouldNotContain(setup.Player.Email!);
        text.ShouldNotContain(setup.Player.Id.ToString());
        string[] expected = ["canCancel", "cancellationReason", "cancelledAt", "createdAt", "createdByClub", "customerPhone", "endsAt", "id", "pitchId", "pitchName", "playerName", "price", "sport", "startsAt", "status"];
        booking.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ShouldBe(expected.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("2026-05-04T08:00:00Z")] // starting now
    [InlineData("2026-05-04T08:30:00Z")] // in progress
    public async Task A_booking_that_has_started_cannot_be_cancelled_says_the_details(string now)
    {
        await using var app = AppAt(now);
        var setup = await BookedAsync();

        var booking = await JsonAsync(await GetBookingAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.OK);

        booking.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task The_details_of_a_cancelled_booking_show_when_and_why()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();
        await JsonAsync(await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken, new { reason = "Avaria da rede" }), HttpStatusCode.OK);

        var booking = await JsonAsync(await GetBookingAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.OK);

        booking.GetProperty("status").GetString().ShouldBe("cancelled_by_club");
        booking.GetProperty("cancellationReason").GetString().ShouldBe("Avaria da rede");
        booking.GetProperty("cancelledAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc(Now));
        booking.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task A_booking_the_player_cancelled_shows_when_and_no_reason()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();
        (await PlayerCancelAsync(app, setup.BookingId, setup.PlayerToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var booking = await JsonAsync(await GetBookingAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.OK);

        booking.GetProperty("status").GetString().ShouldBe("cancelled_by_player");
        booking.GetProperty("cancelledAt").GetDateTime().ToUniversalTime().ShouldBe(TestVenues.Utc(Now));
        booking.GetProperty("cancellationReason").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ---- Through another owner's club, another club, or nothing: 404

    [Fact]
    public async Task Details_and_cancel_through_another_owners_club_are_404_club_not_found_and_change_nothing()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();
        var (_, intruder) = await factory.CreateSignedInAsync(Profiles.Owner);

        await ProblemAsync(await GetBookingAsync(app, setup.Club.Id, setup.BookingId, intruder.AccessToken), HttpStatusCode.NotFound, "club_not_found");
        await ProblemAsync(await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, intruder.AccessToken), HttpStatusCode.NotFound, "club_not_found");
        // The club is checked before the body: an empty one is not a 400 for someone who has no such club.
        await ProblemAsync(await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, intruder.AccessToken, new { reason = "" }), HttpStatusCode.NotFound, "club_not_found");

        (await StoredAsync(setup.BookingId)).Status.ShouldBe("Confirmed");
    }

    [Fact]
    public async Task A_booking_of_another_club_of_the_same_owner_is_404_booking_not_found()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();
        var otherClub = await factory.CreateClubAsync(setup.Club.OwnerId);

        await ProblemAsync(await GetBookingAsync(app, otherClub.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.NotFound, "booking_not_found");
        await ProblemAsync(await OwnerCancelAsync(app, otherClub.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.NotFound, "booking_not_found");
        await ProblemAsync(await OwnerCancelAsync(app, otherClub.Id, setup.BookingId, setup.OwnerToken, new { reason = "" }), HttpStatusCode.NotFound, "booking_not_found");

        (await StoredAsync(setup.BookingId)).Status.ShouldBe("Confirmed");
    }

    [Fact]
    public async Task An_unknown_booking_and_an_unknown_club_are_404()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();

        await ProblemAsync(await GetBookingAsync(app, setup.Club.Id, Guid.CreateVersion7(), setup.OwnerToken), HttpStatusCode.NotFound, "booking_not_found");
        await ProblemAsync(await OwnerCancelAsync(app, setup.Club.Id, Guid.CreateVersion7(), setup.OwnerToken), HttpStatusCode.NotFound, "booking_not_found");
        await ProblemAsync(await GetBookingAsync(app, Guid.CreateVersion7(), setup.BookingId, setup.OwnerToken), HttpStatusCode.NotFound, "club_not_found");
    }

    // ---- Cancelling

    [Fact]
    public async Task Cancelling_stores_the_status_the_trimmed_reason_and_the_moment_and_keeps_the_booking()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();

        var cancelled = await JsonAsync(
            await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken, new { reason = "  Campo em obras  " }), HttpStatusCode.OK);

        cancelled.GetProperty("id").GetGuid().ShouldBe(setup.BookingId);
        cancelled.GetProperty("status").GetString().ShouldBe("cancelled_by_club");
        cancelled.GetProperty("cancellationReason").GetString().ShouldBe("Campo em obras");
        cancelled.GetProperty("canCancel").GetBoolean().ShouldBeFalse();
        cancelled.GetProperty("price").GetDecimal().ShouldBe(20m);
        var stored = await StoredAsync(setup.BookingId);
        stored.Status.ShouldBe("CancelledByClub");
        stored.Reason.ShouldBe("Campo em obras");
        stored.CancelledAt.ShouldBe(TestVenues.Utc(Now));
    }

    [Fact]
    public async Task The_slot_is_free_again_another_player_can_book_it_and_the_player_sees_it_as_cancelled_by_the_club()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();
        await JsonAsync(await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.OK);
        var other = await factory.CreateSignedInPlayerAsync("Bruno Jogador");

        using var rebooked = await SendAsync(app, HttpMethod.Post, "/api/bookings", other.Tokens.AccessToken, new { pitchId = setup.Pitch.Id, startsAt = Start });

        rebooked.StatusCode.ShouldBe(HttpStatusCode.Created, await rebooked.Content.ReadAsStringAsync(Ct));
        var mine = await JsonAsync(await SendAsync(app, HttpMethod.Get, "/api/me/bookings?scope=past", setup.PlayerToken), HttpStatusCode.OK);
        mine.GetProperty("items").EnumerateArray().Single().GetProperty("status").GetString().ShouldBe("cancelled_by_club");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_reason_is_400_and_changes_nothing(string reason)
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();

        var problem = await ProblemAsync(
            await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken, new { reason }), HttpStatusCode.BadRequest, "validation_failed");

        problem.GetProperty("errors").GetProperty("reason").EnumerateArray().Single().GetString().ShouldBe("Indica o motivo do cancelamento.");
        (await StoredAsync(setup.BookingId)).Status.ShouldBe("Confirmed");
    }

    [Fact]
    public async Task A_missing_reason_is_400_too_with_the_same_message()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();

        var problem = await ProblemAsync(
            await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken, new { }), HttpStatusCode.BadRequest, "validation_failed");

        problem.GetProperty("errors").GetProperty("reason").EnumerateArray().Single().GetString().ShouldBe("Indica o motivo do cancelamento.");
        (await StoredAsync(setup.BookingId)).Status.ShouldBe("Confirmed");
    }

    [Fact]
    public async Task A_reason_of_exactly_200_characters_is_fine_and_201_is_not()
    {
        await using var app = AppAt(Now);
        var tooLong = await BookedAsync();
        var justRight = await BookedAsync();

        var problem = await ProblemAsync(
            await OwnerCancelAsync(app, tooLong.Club.Id, tooLong.BookingId, tooLong.OwnerToken, new { reason = new string('a', 201) }),
            HttpStatusCode.BadRequest,
            "validation_failed");
        var cancelled = await JsonAsync(
            await OwnerCancelAsync(app, justRight.Club.Id, justRight.BookingId, justRight.OwnerToken, new { reason = new string('b', 200) }), HttpStatusCode.OK);

        problem.GetProperty("errors").GetProperty("reason").EnumerateArray().Single().GetString().ShouldBe("O motivo pode ter no máximo 200 caracteres.");
        (await StoredAsync(tooLong.BookingId)).Status.ShouldBe("Confirmed");
        cancelled.GetProperty("cancellationReason").GetString()!.Length.ShouldBe(200);
    }

    [Theory]
    [InlineData("2026-05-04T08:00:00Z")] // starting now counts as started
    [InlineData("2026-05-04T08:00:01Z")]
    [InlineData("2026-05-04T08:59:00Z")] // in progress
    [InlineData("2026-05-05T08:00:00Z")] // long over
    public async Task A_booking_that_has_started_is_422_and_stays_confirmed(string now)
    {
        await using var app = AppAt(now);
        var setup = await BookedAsync();

        var problem = await ProblemAsync(
            await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.UnprocessableEntity, "booking_already_started");

        problem.GetProperty("detail").GetString().ShouldBe("Esta reserva já começou, por isso já não pode ser cancelada.");
        var stored = await StoredAsync(setup.BookingId);
        stored.Status.ShouldBe("Confirmed");
        stored.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task The_last_moment_before_the_start_still_works_there_is_no_cancellation_window_for_the_club()
    {
        // One second before the start; the club's window (24 h by default) is long closed for a player.
        await using var app = AppAt("2026-05-04T07:59:59Z");
        var setup = await BookedAsync();
        (await PlayerCancelAsync(app, setup.BookingId, setup.PlayerToken)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var cancelled = await JsonAsync(await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.OK);

        cancelled.GetProperty("status").GetString().ShouldBe("cancelled_by_club");
    }

    [Theory]
    [InlineData("CancelledByClub")]
    [InlineData("CancelledByPlayer")]
    [InlineData("NoShow")]
    public async Task A_booking_that_is_not_confirmed_is_422_and_keeps_what_it_had(string status)
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();
        await factory.SetBookingStatusAsync(setup.BookingId, status);

        await ProblemAsync(
            await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken, new { reason = "Outro motivo" }),
            HttpStatusCode.UnprocessableEntity,
            "booking_not_confirmed");

        var stored = await StoredAsync(setup.BookingId);
        stored.Status.ShouldBe(status);
        stored.Reason.ShouldBeNull();
    }

    [Fact]
    public async Task Cancelling_twice_the_second_is_422_and_the_first_reason_stays()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();
        await JsonAsync(await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken, new { reason = "Primeiro" }), HttpStatusCode.OK);

        await ProblemAsync(
            await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken, new { reason = "Segundo" }),
            HttpStatusCode.UnprocessableEntity,
            "booking_not_confirmed");

        (await StoredAsync(setup.BookingId)).Reason.ShouldBe("Primeiro");
    }

    [Fact]
    public async Task A_booking_the_player_cancelled_cannot_be_cancelled_again_by_the_club()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();
        (await PlayerCancelAsync(app, setup.BookingId, setup.PlayerToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await ProblemAsync(await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.UnprocessableEntity, "booking_not_confirmed");

        (await StoredAsync(setup.BookingId)).Status.ShouldBe("CancelledByPlayer");
    }

    [Fact]
    public async Task A_big_cancellation_window_does_not_matter_to_the_club()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();
        await factory.ChangeCancellationWindowAsync(setup.Club.Id, 168);
        (await PlayerCancelAsync(app, setup.BookingId, setup.PlayerToken)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var cancelled = await JsonAsync(await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), HttpStatusCode.OK);

        cancelled.GetProperty("status").GetString().ShouldBe("cancelled_by_club");
    }

    [Fact]
    public async Task An_owner_cancel_and_a_player_cancel_at_the_same_time_exactly_one_wins_and_none_is_a_500()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();

        var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(() => OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), Ct))
            .Concat(Enumerable.Range(0, 6).Select(_ => Task.Run(() => PlayerCancelAsync(app, setup.BookingId, setup.PlayerToken), Ct)))
            .ToList();
        var responses = await Task.WhenAll(tasks);

        var statuses = responses.Select(response => response.StatusCode).ToList();
        statuses.Count(status => status == HttpStatusCode.OK).ShouldBe(1, string.Join(", ", statuses));
        statuses.Count(status => status == HttpStatusCode.UnprocessableEntity).ShouldBe(11, string.Join(", ", statuses));
        foreach (var response in responses.Where(response => response.StatusCode == HttpStatusCode.UnprocessableEntity))
        {
            (await JsonAsync(response, HttpStatusCode.UnprocessableEntity)).GetProperty("code").GetString().ShouldBe("booking_not_confirmed");
        }

        // Whoever won, the booking is in one consistent state: a club cancel has its reason, a player cancel has none.
        var stored = await StoredAsync(setup.BookingId);
        stored.CancelledAt.ShouldBe(TestVenues.Utc(Now));
        switch (stored.Status)
        {
            case "CancelledByClub":
                stored.Reason.ShouldBe("Campo em obras");
                break;
            case "CancelledByPlayer":
                stored.Reason.ShouldBeNull();
                break;
            default:
                Assert.Fail($"Unexpected status {stored.Status}.");
                break;
        }

        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Concurrent_owner_cancels_give_one_200_and_the_rest_422()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(() => OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, setup.OwnerToken), Ct)));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.UnprocessableEntity).ShouldBe(9);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // ---- Who may ask

    [Fact]
    public async Task Players_and_admins_get_403_and_visitors_401_on_details_and_cancel_and_nothing_changes()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();
        var admin = await factory.CreateSignedInAsync(Profiles.Admin);

        foreach (var token in new string?[] { setup.PlayerToken, admin.Tokens.AccessToken })
        {
            await ProblemAsync(await GetBookingAsync(app, setup.Club.Id, setup.BookingId, token), HttpStatusCode.Forbidden, "forbidden");
            await ProblemAsync(await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, token), HttpStatusCode.Forbidden, "forbidden");
        }

        await ProblemAsync(await GetBookingAsync(app, setup.Club.Id, setup.BookingId, token: null), HttpStatusCode.Unauthorized, "unauthorized");
        await ProblemAsync(await OwnerCancelAsync(app, setup.Club.Id, setup.BookingId, token: null), HttpStatusCode.Unauthorized, "unauthorized");
        (await StoredAsync(setup.BookingId)).Status.ShouldBe("Confirmed");
    }

    [Fact]
    public async Task The_players_own_booking_is_not_readable_through_the_owner_endpoint_with_the_players_session()
    {
        await using var app = AppAt(Now);
        var setup = await BookedAsync();

        // The player has the booking, but the owner area is not theirs.
        using var response = await GetBookingAsync(app, setup.Club.Id, setup.BookingId, setup.PlayerToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
