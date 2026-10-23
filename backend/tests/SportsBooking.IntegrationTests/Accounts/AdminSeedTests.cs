using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Npgsql;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Accounts;

/// <summary>US04: the admin account is created at startup from AdminSeed settings, and only then.</summary>
public sealed class AdminSeedTests(ApiFactory factory)
{
    private const string Password = "admin-palavra-passe";

    private static string NewDatabaseName() => $"us04_{Guid.NewGuid():N}";

    private static string NewEmail() => $"admin-{Guid.NewGuid():N}@reservas.test";

    private static Dictionary<string, string?> Seed(string? email, string? password) => new()
    {
        ["AdminSeed:Email"] = email ?? string.Empty,
        ["AdminSeed:Password"] = password ?? string.Empty,
    };

    private WebApplicationFactory<Program> Start(string databaseName, string? email, string? password)
    {
        var app = factory.CreateIsolatedApp(databaseName, Seed(email, password));
        _ = app.Server; // startup: migrations, then the admin seed
        return app;
    }

    private static async Task<IList<AppUser>> AdminsAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().GetUsersInRoleAsync(Profiles.Admin);
    }

    private async Task<bool> DatabaseExistsAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = $1)", connection)
        {
            Parameters = { new() { Value = databaseName } },
        };
        return (bool)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    private static List<FakeLogRecord> SeederLogs(WebApplicationFactory<Program> app) =>
        app.Services.GetFakeLogCollector().GetSnapshot()
            .Where(record => record.Category == typeof(AdminAccountSeeder).FullName)
            .ToList();

    private static void LogsShouldNotContainSecrets(WebApplicationFactory<Program> app, params string?[] secrets)
    {
        var allLogs = string.Join("\n", app.Services.GetFakeLogCollector().GetSnapshot().Select(record => $"{record.Message} {record.Exception}"));
        foreach (var secret in secrets.Where(secret => !string.IsNullOrEmpty(secret)))
        {
            allLogs.ShouldNotContain(secret!, Case.Insensitive);
        }
    }

    [Fact]
    public async Task Creates_the_admin_account_from_the_configured_email_and_password()
    {
        var email = NewEmail();
        await using var app = Start(NewDatabaseName(), email, Password);

        var admin = (await AdminsAsync(app)).ShouldHaveSingleItem();
        admin.Email.ShouldBe(email);
        admin.EmailConfirmed.ShouldBeTrue();
        admin.Id.Version.ShouldBe(7);

        await using var scope = app.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await userManager.GetRolesAsync(admin)).ShouldBe([Profiles.Admin]);
        (await userManager.CheckPasswordAsync(admin, Password)).ShouldBeTrue();
        admin.PasswordHash.ShouldNotBeNull().ShouldNotContain(Password);

        SeederLogs(app).ShouldContain(record => record.Level == LogLevel.Information && record.Message.Contains("created", StringComparison.Ordinal));
        LogsShouldNotContainSecrets(app, Password, email);
    }

    [Fact]
    public async Task Existing_admin_is_left_unchanged_on_later_startups()
    {
        var databaseName = NewDatabaseName();
        var email = NewEmail();
        Guid firstId;
        await using (var first = Start(databaseName, email, Password))
        {
            firstId = (await AdminsAsync(first)).ShouldHaveSingleItem().Id;
        }

        // Same email with a different password: the existing admin and its password stay as they were.
        await using var second = Start(databaseName, email, "outra-palavra-passe");

        var admin = (await AdminsAsync(second)).ShouldHaveSingleItem();
        admin.Id.ShouldBe(firstId);
        await using var scope = second.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await userManager.CheckPasswordAsync(admin, Password)).ShouldBeTrue();
        (await userManager.CheckPasswordAsync(admin, "outra-palavra-passe")).ShouldBeFalse();
        SeederLogs(second).ShouldContain(record => record.Message.Contains("already exists", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Without_the_settings_the_app_starts_without_admin_and_logs_a_warning()
    {
        await using var app = Start(NewDatabaseName(), email: null, password: null);
        using var client = app.CreateClient();

        (await client.GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await AdminsAsync(app)).ShouldBeEmpty();
        SeederLogs(app).ShouldContain(record => record.Level == LogLevel.Warning && record.Message.Contains("not set", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("admin-only@reservas.test", null, "must be set together")]
    [InlineData(null, Password, "must be set together")]
    [InlineData("not-an-email", Password, "not a valid email address")]
    [InlineData("admin@reservas.test", "curta", "at least 8 characters")]
    public async Task Partial_or_invalid_settings_stop_the_api_with_an_error_that_hides_the_values(
        string? email, string? password, string expectedMessage)
    {
        var databaseName = NewDatabaseName();
        await using var app = factory.CreateIsolatedApp(databaseName, Seed(email, password));

        var exception = Should.Throw<OptionsValidationException>(() => _ = app.Server);

        exception.Message.ShouldContain(expectedMessage);
        foreach (var secret in new[] { email, password }.Where(secret => !string.IsNullOrEmpty(secret)))
        {
            exception.ToString().ShouldNotContain(secret!, Case.Insensitive);
        }

        // Validation runs before migrations: the API never touched the database.
        (await DatabaseExistsAsync(databaseName)).ShouldBeFalse();
    }

    [Fact]
    public async Task Email_of_an_existing_non_admin_account_is_never_promoted()
    {
        var databaseName = NewDatabaseName();
        var email = NewEmail();
        await using (var setup = Start(databaseName, email: null, password: null))
        {
            await using var scope = setup.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var player = new AppUser(email);
            (await userManager.CreateAsync(player, "jogador-palavra-passe")).Succeeded.ShouldBeTrue();
            (await userManager.AddToRoleAsync(player, Profiles.Player)).Succeeded.ShouldBeTrue();
        }

        await using var app = Start(databaseName, email, Password);

        (await AdminsAsync(app)).ShouldBeEmpty();
        await using var check = app.Services.CreateAsyncScope();
        var users = check.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var existing = (await users.FindByEmailAsync(email)).ShouldNotBeNull();
        (await users.GetRolesAsync(existing)).ShouldBe([Profiles.Player]);
        (await users.CheckPasswordAsync(existing, Password)).ShouldBeFalse();
        SeederLogs(app).ShouldContain(record => record.Level == LogLevel.Error && record.Message.Contains("never promoted", StringComparison.Ordinal));
        LogsShouldNotContainSecrets(app, Password, email);
    }

    [Fact]
    public async Task No_second_admin_is_created_when_an_admin_with_another_email_exists()
    {
        var databaseName = NewDatabaseName();
        var firstEmail = NewEmail();
        await using (var first = Start(databaseName, firstEmail, Password))
        {
            (await AdminsAsync(first)).ShouldHaveSingleItem();
        }

        await using var second = Start(databaseName, NewEmail(), Password);

        (await AdminsAsync(second)).ShouldHaveSingleItem().Email.ShouldBe(firstEmail);
        SeederLogs(second).ShouldContain(record => record.Level == LogLevel.Warning && record.Message.Contains("different email", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrent_startups_create_exactly_one_admin()
    {
        var databaseName = NewDatabaseName();
        var email = NewEmail();
        // Create and migrate the database first, so only the seed runs concurrently.
        await using (Start(databaseName, email: null, password: null))
        {
        }

        var first = factory.CreateIsolatedApp(databaseName, Seed(email, Password));
        var second = factory.CreateIsolatedApp(databaseName, Seed(email, Password));
        await using (first)
        await using (second)
        {
            await Task.WhenAll(Task.Run(() => _ = first.Server), Task.Run(() => _ = second.Server));

            (await AdminsAsync(first)).ShouldHaveSingleItem().Email.ShouldBe(email);
        }
    }
}
