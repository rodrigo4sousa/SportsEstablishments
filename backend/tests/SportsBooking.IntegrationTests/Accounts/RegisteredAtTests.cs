using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Accounts;

/// <summary>An account remembers when it was created (the admin's sign-up lists and charts, US26 and US28 read it).</summary>
public sealed class RegisteredAtTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<DateTime> RegisteredAtAsync(WebApplicationFactory<Program> app, string email)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking()
            .Where(user => user.Email == email)
            .Select(user => user.RegisteredAt)
            .SingleAsync(Ct);
    }

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/register/owner")]
    public async Task A_registration_stores_the_moment_it_happened(string path)
    {
        var now = new DateTimeOffset(2026, 7, 14, 9, 30, 15, TimeSpan.Zero);
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(now))));
        var email = $"nova-{Guid.NewGuid():N}@exemplo.test";
        using var client = app.CreateSiteClient();

        using var response = await client.PostAsJsonAsync(path, new { name = "Conta Nova", email, password = "palavra-passe-segura", acceptedTermsVersion = "1.0" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        (await RegisteredAtAsync(app, email)).ShouldBe(now.UtcDateTime);
    }

    [Fact]
    public async Task The_seeded_admin_has_a_registration_date_too()
    {
        var now = new DateTimeOffset(2026, 7, 14, 9, 30, 15, TimeSpan.Zero);
        await using var app = factory.CreateIsolatedApp(
                $"admin_{Guid.NewGuid():N}",
                new Dictionary<string, string?> { ["AdminSeed:Email"] = "admin@exemplo.test", ["AdminSeed:Password"] = "palavra-passe-admin" })
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(now))));
        _ = app.Server;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            // The host seeds at startup on the real clock; seed again on an empty state would be a no-op, so read what it wrote.
            var registeredAt = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking()
                .Where(user => user.Email == "admin@exemplo.test").Select(user => user.RegisteredAt).SingleAsync(Ct);

            registeredAt.Kind.ShouldBe(DateTimeKind.Utc);
            registeredAt.ShouldBeGreaterThan(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }
    }
}
