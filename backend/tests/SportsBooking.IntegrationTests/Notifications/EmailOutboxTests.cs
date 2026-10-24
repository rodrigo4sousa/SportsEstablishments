using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Notifications;

/// <summary>
/// US22: what goes into the outbox. A player's booking, a player's cancellation and a club's cancellation each queue
/// exactly one email, in the same transaction; a booking that is refused or rolled back queues none; customer bookings
/// (US13) never queue any. The worker is off in this host, so the messages stay as they were written.
/// </summary>
public sealed class EmailOutboxTests(ApiFactory factory)
{
    // Friday 1 May 2026, 11:00 in Lisbon. Monday 4 May is open 09:00-13:00: slots at 08:00Z, 09:00Z, 10:00Z, 11:00Z.
    private const string Now = "2026-05-01T10:00:00Z";
    private const string Start = "2026-05-04T08:00:00Z";
    private const string End = "2026-05-04T09:00:00Z";

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

    private sealed record Setup(TestClub Club, TestPitch Pitch, string OwnerToken);

    private async Task<Setup> SetupAsync()
    {
        var (owner, tokens) = await factory.CreateSignedInAsync(Profiles.Owner, "Dono do Clube");
        var club = await factory.CreateClubAsync(owner.Id);
        var pitch = await factory.CreatePitchAsync(club.Id, "Padel 1", price: 18.5m);
        return new Setup(club, pitch, tokens.AccessToken);
    }

    private async Task<List<OutboxMessage>> MessagesForPlayerAsync(Guid playerId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .FromSql($"SELECT * FROM outbox_messages WHERE payload->>'playerId' = {playerId.ToString()}")
            .AsNoTracking()
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.Id)
            .ToListAsync(Ct);
    }

    private async Task<List<OutboxMessage>> MessagesForClubAsync(string clubName)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .FromSql($"SELECT * FROM outbox_messages WHERE payload->>'clubName' = {clubName}")
            .AsNoTracking()
            .ToListAsync(Ct);
    }

    private static Task<HttpResponseMessage> PlayerBooksAsync(WebApplicationFactory<Program> app, Setup setup, string token, string startsAt = Start) =>
        SendAsync(app, HttpMethod.Post, "/api/bookings", token, new { pitchId = setup.Pitch.Id, startsAt });

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return JsonDocument.Parse(text).RootElement.GetProperty("id").GetGuid();
    }

    // ---- One email per event

    [Fact]
    public async Task A_players_booking_queues_exactly_one_confirmation_with_what_the_email_needs_and_no_address()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var player = await factory.CreateSignedInPlayerAsync("Rita Jogadora");

        await IdOfAsync(await PlayerBooksAsync(app, setup, player.Tokens.AccessToken));

        var message = (await MessagesForPlayerAsync(player.User.Id)).ShouldHaveSingleItem();
        message.Type.ShouldBe(OutboxMessageTypes.BookingConfirmed);
        message.Attempts.ShouldBe(0);
        message.SentAt.ShouldBeNull();
        message.FailedAt.ShouldBeNull();
        message.CreatedAt.ShouldBe(TestVenues.Utc(Now));
        message.NextAttemptAt.ShouldBe(TestVenues.Utc(Now));
        var payload = BookingEmailPayload.Deserialize(message.Payload);
        payload.ShouldBe(new BookingEmailPayload(
            player.User.Id, setup.Club.Name, "Rua Principal 1", setup.Club.Locality, "Europe/Lisbon", "Padel 1", TestVenues.Utc(Start), TestVenues.Utc(End), 18.5m, null));
        // Only what the email shows: the address is read when sending, and the name too.
        message.Payload.ShouldNotContain(player.User.Email!);
        message.Payload.ShouldNotContain("Rita Jogadora");
    }

    [Fact]
    public async Task A_players_cancellation_queues_exactly_one_more_email()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var player = await factory.CreateSignedInPlayerAsync();
        var bookingId = await IdOfAsync(await PlayerBooksAsync(app, setup, player.Tokens.AccessToken));

        using var cancelled = await SendAsync(app, HttpMethod.Post, $"/api/bookings/{bookingId}/cancel", player.Tokens.AccessToken);

        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        var messages = await MessagesForPlayerAsync(player.User.Id);
        messages.Select(message => message.Type).ShouldBe([OutboxMessageTypes.BookingConfirmed, OutboxMessageTypes.BookingCancelledByPlayer]);
        BookingEmailPayload.Deserialize(messages[1].Payload).Reason.ShouldBeNull();
    }

    [Fact]
    public async Task The_clubs_cancellation_queues_exactly_one_email_with_the_owners_reason()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var player = await factory.CreateSignedInPlayerAsync();
        var bookingId = await IdOfAsync(await PlayerBooksAsync(app, setup, player.Tokens.AccessToken));

        using var cancelled = await SendAsync(
            app, HttpMethod.Post, $"/api/manage/clubs/{setup.Club.Id}/bookings/{bookingId}/cancel", setup.OwnerToken, new { reason = "  Campo em obras  " });

        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        var messages = await MessagesForPlayerAsync(player.User.Id);
        messages.Select(message => message.Type).ShouldBe([OutboxMessageTypes.BookingConfirmed, OutboxMessageTypes.BookingCancelledByClub]);
        BookingEmailPayload.Deserialize(messages[1].Payload).Reason.ShouldBe("Campo em obras");
    }

    [Fact]
    public async Task A_customer_booking_and_its_cancellation_queue_nothing()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();

        using var created = await SendAsync(
            app, HttpMethod.Post, $"/api/manage/clubs/{setup.Club.Id}/bookings", setup.OwnerToken,
            new { pitchId = setup.Pitch.Id, startsAt = Start, customerName = "Maria Cliente", customerPhone = "912345678" });
        var bookingId = await IdOfAsync(created);
        using var cancelled = await SendAsync(
            app, HttpMethod.Post, $"/api/manage/clubs/{setup.Club.Id}/bookings/{bookingId}/cancel", setup.OwnerToken, new { reason = "Desistiu" });

        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await MessagesForClubAsync(setup.Club.Name)).ShouldBeEmpty();
    }

    // ---- Nothing for what did not happen

    [Fact]
    public async Task A_booking_that_loses_to_the_overlap_constraint_queues_nothing()
    {
        // The slot is already booked: the insert fails with 23P01 and the transaction is rolled back.
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        await factory.AddBookingAsync(setup.Pitch.Id, TestVenues.Utc(Start), TestVenues.Utc(End));
        var player = await factory.CreateSignedInPlayerAsync();

        using var response = await PlayerBooksAsync(app, setup, player.Tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await MessagesForPlayerAsync(player.User.Id)).ShouldBeEmpty();
        (await MessagesForClubAsync(setup.Club.Name)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Of_players_racing_for_one_slot_only_the_winner_gets_a_confirmation()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var players = new List<(Guid Id, string Token)>();
        for (var i = 0; i < 8; i++)
        {
            var player = await factory.CreateSignedInPlayerAsync($"Jogador {i}");
            players.Add((player.User.Id, player.Tokens.AccessToken));
        }

        var responses = await Task.WhenAll(players.Select(player => Task.Run(() => PlayerBooksAsync(app, setup, player.Token), Ct)));

        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(7);
        var winner = players[Array.FindIndex(responses, response => response.StatusCode == HttpStatusCode.Created)];
        var message = (await MessagesForClubAsync(setup.Club.Name)).ShouldHaveSingleItem();
        BookingEmailPayload.Deserialize(message.Payload).PlayerId.ShouldBe(winner.Id);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task A_booking_refused_by_a_slot_rule_queues_nothing()
    {
        await using var app = AppAt("2026-05-04T08:30:00Z"); // the slot has started
        var setup = await SetupAsync();
        var player = await factory.CreateSignedInPlayerAsync();

        using var response = await PlayerBooksAsync(app, setup, player.Tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await MessagesForPlayerAsync(player.User.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_cancellation_queues_nothing()
    {
        var setup = await SetupAsync();
        var player = await factory.CreateSignedInPlayerAsync();
        var booking = await factory.AddBookingAsync(setup.Pitch.Id, TestVenues.Utc(Start), TestVenues.Utc(End), player.User);
        await using var late = AppAt("2026-05-04T07:30:00Z"); // half an hour before: the 24-hour window has closed

        using var byPlayer = await SendAsync(late, HttpMethod.Post, $"/api/bookings/{booking.Id}/cancel", player.Tokens.AccessToken);
        await using var started = AppAt("2026-05-04T08:30:00Z");
        using var byClub = await SendAsync(started, HttpMethod.Post, $"/api/manage/clubs/{setup.Club.Id}/bookings/{booking.Id}/cancel", setup.OwnerToken, new { reason = "Tarde" });

        byPlayer.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        byClub.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await MessagesForPlayerAsync(player.User.Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Concurrent_cancels_of_one_booking_queue_exactly_one_email()
    {
        await using var app = AppAt(Now);
        var setup = await SetupAsync();
        var player = await factory.CreateSignedInPlayerAsync();
        var booking = await factory.AddBookingAsync(setup.Pitch.Id, TestVenues.Utc(Start), TestVenues.Utc(End), player.User);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => Task.Run(() => SendAsync(app, HttpMethod.Post, $"/api/bookings/{booking.Id}/cancel", player.Tokens.AccessToken), Ct))
                .Concat(Enumerable.Range(0, 4).Select(_ => Task.Run(() => SendAsync(
                    app, HttpMethod.Post, $"/api/manage/clubs/{setup.Club.Id}/bookings/{booking.Id}/cancel", setup.OwnerToken, new { reason = "Avaria" }), Ct))));

        responses.Count(response => response.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        var message = (await MessagesForPlayerAsync(player.User.Id)).ShouldHaveSingleItem();
        message.Type.ShouldBeOneOf(OutboxMessageTypes.BookingCancelledByPlayer, OutboxMessageTypes.BookingCancelledByClub);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }
}
