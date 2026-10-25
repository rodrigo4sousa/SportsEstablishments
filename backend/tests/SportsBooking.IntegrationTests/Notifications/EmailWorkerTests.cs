using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Scheduling;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Notifications;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Notifications;

/// <summary>
/// US22: the outbox worker. Each test has its own database (so no other test's messages are in its outbox), a fake email
/// sender and, unless it runs the real background service, a fake clock; the pass is driven by the test.
/// </summary>
public sealed class EmailWorkerTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Start = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Worker(WebApplicationFactory<Program> App, FakeEmailSender Sender, FakeTimeProvider Clock) : IAsyncDisposable
    {
        public async Task<int> RunOnceAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessDueAsync(Ct);
        }

        public ValueTask DisposeAsync() => App.DisposeAsync();
    }

    private Worker CreateWorker(Dictionary<string, string?>? settings = null)
    {
        var sender = new FakeEmailSender();
        var clock = new FakeTimeProvider(Start);
        var app = factory.CreateIsolatedApp($"outbox_{Guid.NewGuid():N}", settings)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IEmailSender>(sender);
                services.AddSingleton<TimeProvider>(clock);
            }));
        _ = app.Server; // start it: migrations on its own database
        return new Worker(app, sender, clock);
    }

    private static async Task<AppUser> PlayerAsync(WebApplicationFactory<Program> app, string name = "Rita Jogadora", string? email = null) =>
        await app.CreateAccountAsync(Profiles.Player, password: null, email: email ?? $"jogadora-{Guid.NewGuid():N}@exemplo.test", name: name);

    private static BookingEmailPayload Payload(Guid playerId) => new(
        playerId, "Clube da Foz", "Rua do Mar 1", "Porto", "Europe/Lisbon", "Padel 1",
        new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc), 18.5m, null);

    private static async Task<Guid> EnqueueAsync(WebApplicationFactory<Program> app, Guid playerId, DateTime? at = null, string type = OutboxMessageTypes.BookingConfirmed)
    {
        var message = OutboxMessage.Create(type, Payload(playerId).Serialize(), at ?? Start.UtcDateTime);
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.OutboxMessages.Add(message);
        await dbContext.SaveChangesAsync(Ct);
        return message.Id;
    }

    private static async Task<OutboxMessage> MessageAsync(WebApplicationFactory<Program> app, Guid id)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking().SingleAsync(message => message.Id == id, Ct);
    }

    private static async Task<List<OutboxMessage>> AllMessagesAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking().ToListAsync(Ct);
    }

    private static IReadOnlyList<FakeLogRecord> Logs(WebApplicationFactory<Program> app) => app.Services.GetFakeLogCollector().GetSnapshot();

    // ---- Sending

    [Fact]
    public async Task A_due_message_is_sent_to_the_players_address_and_marked_sent_once()
    {
        await using var worker = CreateWorker();
        var player = await PlayerAsync(worker.App, email: "rita@exemplo.test");
        var id = await EnqueueAsync(worker.App, player.Id);

        (await worker.RunOnceAsync()).ShouldBe(1);

        var email = worker.Sender.Sent.ShouldHaveSingleItem();
        email.ToAddress.ShouldBe("rita@exemplo.test");
        email.ToName.ShouldBe("Rita Jogadora");
        email.Subject.ShouldBe("Reserva confirmada: Clube da Foz, 04/05/2026 às 10:00");
        email.TextBody.ShouldStartWith("Olá Rita Jogadora,");
        email.TextBody.ShouldContain("Preço: 18,50 €");
        email.HtmlBody.ShouldContain("<td>Clube da Foz</td>");
        var message = await MessageAsync(worker.App, id);
        message.SentAt.ShouldBe(Start.UtcDateTime);
        message.Attempts.ShouldBe(0);
        message.LastError.ShouldBeNull();

        (await worker.RunOnceAsync()).ShouldBe(0);
        worker.Sender.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task The_address_is_the_players_current_one_read_when_sending()
    {
        await using var worker = CreateWorker();
        var player = await PlayerAsync(worker.App, email: "antigo@exemplo.test");
        await EnqueueAsync(worker.App, player.Id);
        await using (var scope = worker.App.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(player.Id.ToString());
            (await users.SetEmailAsync(user!, "novo@exemplo.test")).Succeeded.ShouldBeTrue();
        }

        await worker.RunOnceAsync();

        worker.Sender.Sent.ShouldHaveSingleItem().ToAddress.ShouldBe("novo@exemplo.test");
    }

    [Fact]
    public async Task Each_kind_of_message_becomes_its_email()
    {
        await using var worker = CreateWorker();
        var player = await PlayerAsync(worker.App);
        await EnqueueAsync(worker.App, player.Id, type: OutboxMessageTypes.BookingCancelledByPlayer);
        var clubMessage = OutboxMessage.Create(OutboxMessageTypes.BookingCancelledByClub, (Payload(player.Id) with { Reason = "Campo em obras" }).Serialize(), Start.UtcDateTime);
        await using (var scope = worker.App.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.OutboxMessages.Add(clubMessage);
            await dbContext.SaveChangesAsync(Ct);
        }

        await worker.RunOnceAsync();

        var subjects = worker.Sender.Sent.Select(email => email.Subject).Order().ToList();
        subjects.ShouldBe(["O clube cancelou a tua reserva: Clube da Foz, 04/05/2026 às 10:00", "Reserva cancelada: Clube da Foz, 04/05/2026 às 10:00"]);
        worker.Sender.Sent.Single(email => email.Subject.StartsWith("O clube", StringComparison.Ordinal)).TextBody.ShouldContain("Motivo: Campo em obras");
    }

    [Fact]
    public async Task A_message_that_is_not_due_yet_waits()
    {
        await using var worker = CreateWorker();
        var player = await PlayerAsync(worker.App);
        await EnqueueAsync(worker.App, player.Id, at: Start.UtcDateTime.AddMinutes(5));

        (await worker.RunOnceAsync()).ShouldBe(0);
        worker.Clock.Advance(TimeSpan.FromMinutes(5));
        (await worker.RunOnceAsync()).ShouldBe(1);

        worker.Sender.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_batch_is_at_most_the_batch_size_and_the_rest_waits_for_the_next_pass()
    {
        await using var worker = CreateWorker(new() { ["Outbox:BatchSize"] = "3" });
        for (var i = 0; i < 7; i++)
        {
            await EnqueueAsync(worker.App, (await PlayerAsync(worker.App)).Id);
        }

        (await worker.RunOnceAsync()).ShouldBe(3);
        (await worker.RunOnceAsync()).ShouldBe(3);
        (await worker.RunOnceAsync()).ShouldBe(1);
        (await worker.RunOnceAsync()).ShouldBe(0);

        worker.Sender.Sent.Count.ShouldBe(7);
    }

    // ---- Failures

    [Fact]
    public async Task A_failed_send_is_retried_after_the_back_off_and_then_sent()
    {
        await using var worker = CreateWorker();
        var player = await PlayerAsync(worker.App, email: "rita@exemplo.test");
        var id = await EnqueueAsync(worker.App, player.Id);
        worker.Sender.FailNext(2);

        (await worker.RunOnceAsync()).ShouldBe(1);
        var first = await MessageAsync(worker.App, id);
        first.Attempts.ShouldBe(1);
        first.NextAttemptAt.ShouldBe(Start.UtcDateTime.AddMinutes(1));
        first.LastError.ShouldBe(nameof(SmtpTestException));
        first.SentAt.ShouldBeNull();

        worker.Clock.Advance(TimeSpan.FromSeconds(59));
        (await worker.RunOnceAsync()).ShouldBe(0); // not yet

        worker.Clock.Advance(TimeSpan.FromSeconds(1));
        (await worker.RunOnceAsync()).ShouldBe(1);
        var second = await MessageAsync(worker.App, id);
        second.Attempts.ShouldBe(2);
        second.NextAttemptAt.ShouldBe(Start.UtcDateTime.AddMinutes(1 + 2)); // the delay doubled

        worker.Clock.Advance(TimeSpan.FromMinutes(2));
        (await worker.RunOnceAsync()).ShouldBe(1);
        var sent = await MessageAsync(worker.App, id);
        sent.SentAt.ShouldBe(Start.UtcDateTime.AddMinutes(3));
        sent.Attempts.ShouldBe(2);
        sent.LastError.ShouldBeNull();
        worker.Sender.Sent.ShouldHaveSingleItem().ToAddress.ShouldBe("rita@exemplo.test");
    }

    [Fact]
    public async Task After_the_maximum_attempts_the_message_is_given_up_and_logged_at_warning_without_the_address()
    {
        await using var worker = CreateWorker(new() { ["Outbox:MaxAttempts"] = "3" });
        var player = await PlayerAsync(worker.App, email: "rita.segredo@exemplo.test");
        var id = await EnqueueAsync(worker.App, player.Id);
        worker.Sender.AlwaysFail = true;

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            (await worker.RunOnceAsync()).ShouldBe(1);
            worker.Clock.Advance(TimeSpan.FromHours(1));
        }

        var message = await MessageAsync(worker.App, id);
        message.Attempts.ShouldBe(3);
        message.FailedAt.ShouldNotBeNull();
        message.SentAt.ShouldBeNull();
        message.LastError.ShouldBe(nameof(SmtpTestException));
        (await worker.RunOnceAsync()).ShouldBe(0); // never again
        worker.Sender.Calls.ShouldBe(3);

        var logs = Logs(worker.App);
        var warning = logs.Where(record => record.Level == LogLevel.Warning && record.Category == typeof(OutboxProcessor).FullName).ShouldHaveSingleItem();
        warning.Message.ShouldContain(id.ToString());
        warning.Message.ShouldContain(OutboxMessageTypes.BookingConfirmed);
        warning.Message.ShouldContain("3 failed attempts");
        logs.Where(record => record.Category == typeof(OutboxProcessor).FullName && record.Level == LogLevel.Information).Count().ShouldBe(2); // the two retries
        foreach (var record in logs)
        {
            record.Message.ShouldNotContain("rita.segredo@exemplo.test");
            (record.Exception?.ToString() ?? string.Empty).ShouldNotContain("rita.segredo@exemplo.test");
        }
    }

    [Fact]
    public async Task The_error_kept_on_the_message_never_has_the_address()
    {
        await using var worker = CreateWorker();
        var player = await PlayerAsync(worker.App, email: "rita.segredo@exemplo.test");
        var id = await EnqueueAsync(worker.App, player.Id);
        worker.Sender.AlwaysFail = true;

        await worker.RunOnceAsync();

        (await MessageAsync(worker.App, id)).LastError.ShouldBe(nameof(SmtpTestException));
        await using var scope = worker.App.Services.CreateAsyncScope();
        var raw = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<string>($"SELECT concat_ws('|', payload::text, last_error) AS \"Value\" FROM outbox_messages")
            .SingleAsync(Ct);
        raw.ShouldNotContain("rita.segredo@exemplo.test");
    }

    [Fact]
    public async Task A_message_for_an_account_that_no_longer_exists_is_given_up_at_once()
    {
        await using var worker = CreateWorker();
        var id = await EnqueueAsync(worker.App, Guid.CreateVersion7());

        (await worker.RunOnceAsync()).ShouldBe(1);

        var message = await MessageAsync(worker.App, id);
        message.FailedAt.ShouldBe(Start.UtcDateTime);
        message.Attempts.ShouldBe(0);
        message.LastError.ShouldBe(OutboxProcessor.RecipientNotFound);
        worker.Sender.Calls.ShouldBe(0);
        Logs(worker.App).ShouldContain(record => record.Level == LogLevel.Warning && record.Message.Contains(id.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task One_failing_message_does_not_hold_up_the_others_in_the_batch()
    {
        await using var worker = CreateWorker();
        var first = await EnqueueAsync(worker.App, (await PlayerAsync(worker.App)).Id);
        var second = await EnqueueAsync(worker.App, (await PlayerAsync(worker.App)).Id, at: Start.UtcDateTime.AddSeconds(-1));
        worker.Sender.FailNext(1); // the oldest-due message (second) fails

        (await worker.RunOnceAsync()).ShouldBe(2);

        (await MessageAsync(worker.App, second)).Attempts.ShouldBe(1);
        (await MessageAsync(worker.App, first)).SentAt.ShouldNotBeNull();
    }

    // ---- Two workers

    [Fact]
    public async Task Two_workers_at_once_never_send_the_same_message_twice()
    {
        await using var worker = CreateWorker(new() { ["Outbox:BatchSize"] = "4" });
        worker.Sender.Delay = TimeSpan.FromMilliseconds(15);
        var players = new List<AppUser>();
        for (var i = 0; i < 30; i++)
        {
            var player = await PlayerAsync(worker.App, email: $"jogador{i}@exemplo.test");
            players.Add(player);
            await EnqueueAsync(worker.App, player.Id);
        }

        async Task<int> DrainAsync()
        {
            var total = 0;
            while (true)
            {
                var claimed = await worker.RunOnceAsync();
                if (claimed == 0)
                {
                    return total;
                }

                total += claimed;
            }
        }

        var claimedByEach = await Task.WhenAll(Task.Run(DrainAsync, Ct), Task.Run(DrainAsync, Ct));

        claimedByEach.Sum().ShouldBe(30, $"claimed: {string.Join(", ", claimedByEach)}");
        worker.Sender.Calls.ShouldBe(30);
        worker.Sender.Sent.Select(email => email.ToAddress).Distinct().Count().ShouldBe(30);
        (await AllMessagesAsync(worker.App)).ShouldAllBe(message => message.SentAt != null);
    }

    // ---- The background service

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!await condition())
        {
            if (watch.Elapsed > timeout)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(100, Ct);
        }
    }

    private (WebApplicationFactory<Program> App, FakeEmailSender Sender) RunningWorker()
    {
        var sender = new FakeEmailSender();
        var app = factory.CreateIsolatedApp(
                $"outbox_{Guid.NewGuid():N}",
                new Dictionary<string, string?> { ["Outbox:Enabled"] = "true", ["Outbox:PollIntervalSeconds"] = "1" })
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(sender)));
        _ = app.Server;
        return (app, sender);
    }

    [Fact]
    public async Task The_background_service_sends_what_is_queued_on_its_own()
    {
        var (app, sender) = RunningWorker();
        await using var _ = app;
        var player = await PlayerAsync(app, email: "rita@exemplo.test");
        var id = await EnqueueAsync(app, player.Id, at: DateTime.UtcNow);

        await WaitUntilAsync(async () => (await MessageAsync(app, id)).SentAt is not null, TimeSpan.FromSeconds(20));

        sender.Sent.ShouldHaveSingleItem().ToAddress.ShouldBe("rita@exemplo.test");
    }

    [Fact]
    public async Task A_sender_that_throws_does_not_touch_the_booking_request_and_the_failure_is_recorded_later()
    {
        var (app, sender) = RunningWorker();
        await using var _ = app;
        sender.AlwaysFail = true;
        var (owner, _) = await app.CreateSignedInAsync(Profiles.Owner, "Dono");
        var club = await app.CreateClubAsync(owner.Id);
        var everyDay = Enum.GetValues<DayOfWeek>().Select(day => (day, "08:00", "22:00")).ToArray();
        var pitch = await app.CreatePitchAsync(club.Id, open: everyDay);
        var lisbon = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
        var tomorrow = ClubTime.Today(TimeProvider.System, lisbon).AddDays(1);
        var startsAt = ClubTime.ToUtc(tomorrow, new TimeOnly(10, 0), lisbon);
        var player = await app.CreateSignedInAsync(Profiles.Player, "Rita Jogadora");

        using var client = app.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/bookings")
        {
            Content = JsonContent.Create(new { pitchId = pitch.Id, startsAt = startsAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) }),
        };
        var watch = Stopwatch.StartNew();
        using var response = await client.SendAsync(request.WithCookies((SessionCookies.AccessToken, player.Tokens.AccessToken)), Ct);
        watch.Stop();

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        sender.Calls.ShouldBe(0, "the request never waits for the email");
        // A booking queues the player's confirmation, the owner's email (US23) and the reminder (US21, not due yet): the two due ones fail.
        await WaitUntilAsync(async () => (await AllMessagesAsync(app)).SingleOrDefault(candidate => candidate.Type == OutboxMessageTypes.BookingConfirmed)?.Attempts >= 1, TimeSpan.FromSeconds(20));
        var message = (await AllMessagesAsync(app)).Single(candidate => candidate.Type == OutboxMessageTypes.BookingConfirmed);
        message.LastError.ShouldBe(nameof(SmtpTestException));
        message.SentAt.ShouldBeNull();
        message.FailedAt.ShouldBeNull();
        (await MessageAsync(app, message.Id)).NextAttemptAt.ShouldBeGreaterThan(message.CreatedAt);
    }

    [Fact]
    public async Task The_background_service_is_off_when_disabled()
    {
        await using var worker = CreateWorker(); // Outbox:Enabled = false, as in every other test
        var player = await PlayerAsync(worker.App);
        await EnqueueAsync(worker.App, player.Id);

        await Task.Delay(1500, Ct);

        worker.Sender.Calls.ShouldBe(0);
        Logs(worker.App).ShouldContain(record => record.Message.Contains("Outbox:Enabled = false", StringComparison.Ordinal));
    }
}
