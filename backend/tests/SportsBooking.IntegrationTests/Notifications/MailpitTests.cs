using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Notifications;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Notifications;

/// <summary>US22: the real SMTP sender, through the outbox worker, to a real Mailpit (the development mail catcher).</summary>
public sealed class MailpitTests(ApiFactory factory) : IAsyncLifetime
{
    private const string MailpitImage = "axllent/mailpit:v1.31.6";

    private readonly IContainer _mailpit = new ContainerBuilder(MailpitImage)
        .WithPortBinding(1025, assignRandomHostPort: true)
        .WithPortBinding(8025, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(8025).ForPath("/livez")))
        .Build();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _mailpit.StartAsync();

    public async ValueTask DisposeAsync() => await _mailpit.DisposeAsync();

    [Fact]
    public async Task A_booking_email_arrives_at_mailpit_over_smtp_with_text_and_html()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));
        await using var app = factory.CreateIsolatedApp(
                $"outbox_{Guid.NewGuid():N}",
                new Dictionary<string, string?>
                {
                    ["Email:Host"] = _mailpit.Hostname,
                    ["Email:Port"] = _mailpit.GetMappedPublicPort(1025).ToString(CultureInfo.InvariantCulture),
                    ["Email:Security"] = "None",
                    ["Email:FromAddress"] = "reservas@sportsbooking.local",
                })
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        var player = await app.CreateAccountAsync(Profiles.Player, password: null, email: "rita@exemplo.test", name: "Rita Jogadora");
        var payload = new BookingEmailPayload(
            player.Id, "Clube da Foz", "Rua do Mar 1", "Porto", "Europe/Lisbon", "Padel 1",
            new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc), new DateTime(2026, 10, 25, 1, 0, 0, DateTimeKind.Utc), 18.5m, null);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.OutboxMessages.Add(OutboxMessage.Create(OutboxMessageTypes.BookingConfirmed, payload.Serialize(), clock.GetUtcNow().UtcDateTime));
            await dbContext.SaveChangesAsync(Ct);
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessDueAsync(Ct)).ShouldBe(1);
        }

        using var http = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}") };
        var list = await http.GetFromJsonAsync<JsonElement>("/api/v1/messages", Ct);
        list.GetProperty("total").GetInt32().ShouldBe(1);
        var summary = list.GetProperty("messages")[0];
        summary.GetProperty("Subject").GetString().ShouldBe("Reserva confirmada: Clube da Foz, 25/10/2026 às 01:30 (verão)");
        summary.GetProperty("To")[0].GetProperty("Address").GetString().ShouldBe("rita@exemplo.test");
        summary.GetProperty("To")[0].GetProperty("Name").GetString().ShouldBe("Rita Jogadora");
        summary.GetProperty("From").GetProperty("Address").GetString().ShouldBe("reservas@sportsbooking.local");
        summary.GetProperty("From").GetProperty("Name").GetString().ShouldBe("Reservas de Campos");

        var message = await http.GetFromJsonAsync<JsonElement>($"/api/v1/message/{summary.GetProperty("ID").GetString()}", Ct);
        message.GetProperty("Text").GetString()!.ShouldContain("Hora: 01:30 (verão) – 01:00 (inverno)");
        message.GetProperty("Text").GetString()!.ShouldContain("Preço: 18,50 €");
        message.GetProperty("HTML").GetString()!.ShouldContain("<td>Clube da Foz</td>");
        message.GetProperty("HTML").GetString()!.ShouldContain("Olá Rita Jogadora,");
    }

    [Fact]
    public async Task A_closed_smtp_port_is_a_failed_attempt_not_a_crash()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));
        await using var app = factory.CreateIsolatedApp(
                $"outbox_{Guid.NewGuid():N}",
                new Dictionary<string, string?> { ["Email:Host"] = "127.0.0.1", ["Email:Port"] = "1", ["Email:Security"] = "None", ["Email:TimeoutSeconds"] = "5" })
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock)));
        var player = await app.CreateAccountAsync(Profiles.Player, password: null, email: "rita@exemplo.test", name: "Rita");
        var payload = new BookingEmailPayload(
            player.Id, "Clube", "Rua 1", "Porto", "Europe/Lisbon", "Padel 1",
            new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc), 20m, null);
        Guid id;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var outbox = OutboxMessage.Create(OutboxMessageTypes.BookingConfirmed, payload.Serialize(), clock.GetUtcNow().UtcDateTime);
            id = outbox.Id;
            dbContext.OutboxMessages.Add(outbox);
            await dbContext.SaveChangesAsync(Ct);
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessDueAsync(Ct);
        }

        await using var check = app.Services.CreateAsyncScope();
        var message = await check.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.FindAsync([id], Ct);
        message!.Attempts.ShouldBe(1);
        message.SentAt.ShouldBeNull();
        message.LastError.ShouldNotBeNullOrWhiteSpace();
        message.LastError.ShouldNotContain("rita@exemplo.test");
    }
}
