using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Scheduling;
using SportsBooking.Infrastructure;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Notifications;

/// <summary>T06: Production may start without SMTP. It warns, sends nothing and never lets email get in the way of a booking.</summary>
public sealed class SmtpNotConfiguredTests(ApiFactory factory)
{
    private static readonly (DayOfWeek Day, string Opens, string Closes)[] EveryDay =
        Enum.GetValues<DayOfWeek>().Select(day => (day, "09:00", "13:00")).ToArray();

    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById(ClubTimeZones.LisbonId);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> ProductionApp(IReadOnlyDictionary<string, string?> settings) =>
        factory.CreateIsolatedApp($"t06_smtp_{Guid.NewGuid():N}", settings).WithWebHostBuilder(builder => builder.UseEnvironment("Production"));

    [Fact]
    public async Task Without_smtp_the_api_starts_warns_and_bookings_and_cancellations_still_work()
    {
        await using var app = ProductionApp(new Dictionary<string, string?> { ["Email:Host"] = null, ["Email:FromAddress"] = null });
        _ = app.Server;
        await app.Services.MigrateDatabaseAsync(Ct);
        var club = await app.CreateClubAsync();
        var pitch = await app.CreatePitchAsync(club.Id, open: EveryDay);
        var (_, tokens) = await app.CreateSignedInAsync(Profiles.Player);

        // Three days ahead: inside the booking window and well before the 24-hour cancellation deadline.
        var day = ClubTime.Today(TimeProvider.System, Lisbon).AddDays(3);
        var startsAt = ClubTime.ToUtc(day, new TimeOnly(10, 0), Lisbon).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        using var client = app.CreateBrowserClient();
        using var book = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = JsonContent.Create(new { pitchId = pitch.Id, startsAt }) };
        book.WithCookies((SessionCookies.AccessToken, tokens.AccessToken));
        using var booked = await client.SendAsync(book, Ct);
        booked.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var cancel = new HttpRequestMessage(HttpMethod.Post, $"{booked.Headers.Location}/cancel");
        cancel.WithCookies((SessionCookies.AccessToken, tokens.AccessToken));
        using var cancelled = await client.SendAsync(cancel, Ct);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);

        await using var scope = app.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.CountAsync(Ct)).ShouldBeGreaterThan(0);
        app.Services.GetFakeLogCollector().GetSnapshot()
            .ShouldContain(record => record.Level == LogLevel.Warning && record.Message.StartsWith("SMTP is not configured", StringComparison.Ordinal));
    }

    [Fact]
    public void Smtp_settings_that_are_only_half_there_still_stop_the_api()
    {
        using var app = ProductionApp(new Dictionary<string, string?> { ["Email:Host"] = "smtp.exemplo.pt", ["Email:FromAddress"] = null });

        Should.Throw<OptionsValidationException>(() => _ = app.Server).Message.ShouldContain("Email:FromAddress");
    }
}
