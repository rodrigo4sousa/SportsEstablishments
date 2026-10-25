using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Admin;

/// <summary>
/// The admin's numbers (US24, US25, US26) are totals of the whole platform, so each test gets its own database and a fixed
/// clock instead of sharing the assembly's. The admin's session comes from the shared factory: a token is checked against the
/// system clock and holds no database state, so it opens any host that shares the signing key.
/// </summary>
public sealed class AdminTestApp(WebApplicationFactory<Program> app, string accessToken, string connectionString) : IAsyncDisposable
{
    /// <summary>
    /// Every host has its own connection pool, and PostgreSQL allows 100 connections to the whole suite: few of these at once, each
    /// with a small pool that is closed with the host.
    /// </summary>
    private static readonly SemaphoreSlim Hosts = new(4);

    private static long _nextHour;

    public WebApplicationFactory<Program> App { get; } = app;

    /// <summary>Lisbon: Wednesday 28 October 2026, 12:00 (WET, UTC+0).</summary>
    public static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    public static async Task<AdminTestApp> CreateAsync(ApiFactory factory)
    {
        await Hosts.WaitAsync(TestContext.Current.CancellationToken);
        var connectionString = factory.ConnectionStringFor($"admin_{Guid.NewGuid():N}") + ";Maximum Pool Size=4;Minimum Pool Size=0";
        var app = factory.CreateIsolatedApp(
                new NpgsqlConnectionStringBuilder(connectionString).Database!,
                new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString })
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now))));
        _ = app.Server;
        var (_, tokens) = await factory.CreateSignedInAsync(Profiles.Admin, "Administrador de Teste");
        return new AdminTestApp(app, tokens.AccessToken, connectionString);
    }

    public async Task<HttpResponseMessage> GetAsync(string path)
    {
        using var client = App.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path).WithCookies((SessionCookies.AccessToken, accessToken));
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async Task<JsonElement> GetJsonAsync(string path)
    {
        using var response = await GetAsync(path);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    public async Task<JsonElement> GetProblemAsync(string path)
    {
        using var response = await GetAsync(path);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>A booking by a player or, with no player, for a customer, <em>created</em> at the given instant; every call takes a new hour of its own on the pitch.</summary>
    public async Task<Guid> AddBookingAsync(Guid pitchId, string createdAtUtc, Guid? playerId = null, string status = "Confirmed")
    {
        var startsAt = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(Interlocked.Increment(ref _nextHour));
        var createdAt = TestVenues.Utc(createdAtUtc);
        var booking = playerId is { } player
            ? Booking.ForPlayer(pitchId, player, startsAt, startsAt.AddHours(1), 20m, createdAt)
            : Booking.ForCustomer(pitchId, "Cliente de Teste", "912345678", startsAt, startsAt.AddHours(1), 20m, createdAt);

        await using (var scope = App.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Bookings.Add(booking);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        if (status != "Confirmed")
        {
            await App.SetBookingStatusAsync(booking.Id, status);
        }

        return booking.Id;
    }

    public async Task<Guid> AddPlayerAsync(string name = "Jogador de Teste", string? email = null) =>
        (await App.CreateAccountAsync(Profiles.Player, password: null, email: email, name: name)).Id;

    public async Task<Guid> AddOwnerAsync(string name = "Dono de Teste", string? email = null, DateTime? registeredAt = null)
    {
        var owner = await App.CreateAccountAsync(Profiles.Owner, password: null, email: email, name: name);
        if (registeredAt is { } when)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.ExecuteSqlAsync($"UPDATE users SET registered_at = {when} WHERE id = {owner.Id}", TestContext.Current.CancellationToken);
        }

        return owner.Id;
    }

    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync();
        NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        Hosts.Release();
    }
}
