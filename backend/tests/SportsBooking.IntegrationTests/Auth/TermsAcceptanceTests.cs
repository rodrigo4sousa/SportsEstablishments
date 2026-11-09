using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Auth;

/// <summary>T07: every sign-up stores which version of the terms was accepted and when; accounts from before that stay empty.</summary>
public sealed class TermsAcceptanceTests(ApiFactory factory)
{
    private const string Password = "palavra-passe-segura";

    private static readonly string TooLong = new('1', TermsAcceptancePolicy.MaximumVersionLength + 1);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"termos-{Guid.NewGuid():N}@exemplo.test";

    private static async Task<(string? Version, DateTime? AcceptedAt)> AcceptanceOfAsync(WebApplicationFactory<Program> app, string email)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking()
            .SingleAsync(candidate => candidate.Email == email, Ct);
        return (user.AcceptedTermsVersion, user.TermsAcceptedAt);
    }

    [Theory]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/register/owner")]
    public async Task A_sign_up_stores_the_accepted_version_and_the_time_from_the_clock(string path)
    {
        var now = new DateTimeOffset(2026, 11, 9, 18, 45, 30, TimeSpan.Zero);
        await using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(now))));
        var email = NewEmail();
        using var client = app.CreateSiteClient();

        using var response = await client.PostAsJsonAsync(path, new { name = "Conta Nova", email, password = Password, acceptedTermsVersion = " 1.0 " }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var (version, acceptedAt) = await AcceptanceOfAsync(app, email);
        version.ShouldBe("1.0");
        acceptedAt.ShouldBe(now.UtcDateTime);
    }

    [Theory]
    [InlineData("/api/auth/register", "missing")]
    [InlineData("/api/auth/register", "null")]
    [InlineData("/api/auth/register", "blank")]
    [InlineData("/api/auth/register", "too-long")]
    [InlineData("/api/auth/register/owner", "missing")]
    [InlineData("/api/auth/register/owner", "null")]
    [InlineData("/api/auth/register/owner", "blank")]
    [InlineData("/api/auth/register/owner", "too-long")]
    public async Task A_sign_up_without_an_acceptance_is_refused_with_a_stable_code_and_creates_nothing(string path, string acceptance)
    {
        var email = NewEmail();
        var body = new JsonObject { ["name"] = "Conta Nova", ["email"] = email, ["password"] = Password };
        switch (acceptance)
        {
            case "null":
                body["acceptedTermsVersion"] = null;
                break;
            case "blank":
                body["acceptedTermsVersion"] = "   ";
                break;
            case "too-long":
                body["acceptedTermsVersion"] = TooLong;
                break;
        }

        using var client = factory.CreateSiteClient();

        using var response = await client.PostAsJsonAsync(path, body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("terms_not_accepted");
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("errors").GetProperty("acceptedTermsVersion").GetArrayLength().ShouldBe(1);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();

        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync(user => user.Email == email, Ct)).ShouldBe(0);
    }

    [Theory]
    [InlineData(Profiles.Player)]
    [InlineData(Profiles.Owner)]
    public async Task An_account_from_before_the_terms_has_no_acceptance_and_can_still_sign_in(string profile)
    {
        var email = NewEmail();
        await factory.CreateAccountAsync(profile, Password, email);
        (await AcceptanceOfAsync(factory, email)).ShouldBe((null, null));
        using var client = factory.CreateSiteClient();

        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await AcceptanceOfAsync(factory, email)).ShouldBe((null, null));
    }

    [Fact]
    public async Task The_seeded_admin_is_not_recorded_as_having_accepted_the_terms()
    {
        await using var app = factory.CreateIsolatedApp(
            $"t07_admin_{Guid.NewGuid():N}",
            new Dictionary<string, string?> { ["AdminSeed:Email"] = "admin-t07@exemplo.test", ["AdminSeed:Password"] = "palavra-passe-admin" });
        _ = app.Server;

        (await AcceptanceOfAsync(app, "admin-t07@exemplo.test")).ShouldBe((null, null));
    }

    [Fact]
    public async Task The_migration_adds_empty_columns_and_leaves_existing_accounts_as_they_were()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(factory.ConnectionStringFor($"t07_{Guid.NewGuid():N}"))
            .UseSnakeCaseNamingConvention()
            .Options;
        await using var context = new AppDbContext(options);
        var migrations = context.Database.GetMigrations().ToList();
        var target = migrations.Single(migration => migration.EndsWith("_AddTermsAcceptance", StringComparison.Ordinal));
        var migrator = context.GetService<IMigrator>();
        var id = Guid.CreateVersion7();
        var registeredAt = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        await migrator.MigrateAsync(migrations[migrations.IndexOf(target) - 1], Ct);
        await context.Database.ExecuteSqlAsync($"""
            INSERT INTO users (id, user_name, normalized_user_name, email, normalized_email, email_confirmed, phone_number_confirmed, two_factor_enabled, lockout_enabled, access_failed_count, name, registered_at)
            VALUES ({id}, 'antiga@exemplo.test', 'ANTIGA@EXEMPLO.TEST', 'antiga@exemplo.test', 'ANTIGA@EXEMPLO.TEST', false, false, false, true, 0, 'Conta Antiga', {registeredAt})
            """, Ct);

        await migrator.MigrateAsync(cancellationToken: Ct);

        var stored = await context.Database
            .SqlQuery<StoredAccount>($"SELECT id, name, registered_at, accepted_terms_version, terms_accepted_at FROM users")
            .SingleAsync(Ct);
        stored.ShouldBe(new StoredAccount(id, "Conta Antiga", registeredAt, null, null));
    }

    private sealed record StoredAccount(Guid Id, string Name, DateTime RegisteredAt, string? AcceptedTermsVersion, DateTime? TermsAcceptedAt);
}
