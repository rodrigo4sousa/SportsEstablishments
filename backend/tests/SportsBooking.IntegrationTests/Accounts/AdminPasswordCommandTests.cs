using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using SportsBooking.Application.Auth;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Accounts;

/// <summary>
/// US04: the <c>set-admin-password</c> command, which replaces the manual "delete the admin row" procedure. The command is run
/// the way <c>Program.cs</c> runs it, on an isolated database that has one admin (created by the seed at startup).
/// </summary>
public sealed class AdminPasswordCommandTests(ApiFactory factory)
{
    private const string OldPassword = "admin-palavra-passe";
    private const string NewPassword = "nova-palavra-passe-do-admin";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] CommandLine = [AdminPasswordCommand.Name];

    private sealed record Outcome(int ExitCode, string Output, string Error);

    private static string NewEmail() => $"admin-{Guid.NewGuid():N}@reservas.test";

    private WebApplicationFactory<Program> StartWithAdmin(string email)
    {
        var app = factory.CreateIsolatedApp(
            $"us04pw_{Guid.NewGuid():N}",
            new Dictionary<string, string?> { ["AdminSeed:Email"] = email, ["AdminSeed:Password"] = OldPassword });
        _ = app.Server;
        return app;
    }

    private static async Task<Outcome> RunAsync(WebApplicationFactory<Program> app, string? password, IReadOnlyList<string>? args = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await AdminPasswordCommand.RunAsync(
            app.Services,
            args ?? CommandLine,
            name => name == AdminPasswordCommand.PasswordVariable ? password : null,
            output,
            error,
            Ct);
        return new Outcome(exitCode, output.ToString(), error.ToString());
    }

    private static async Task<HttpResponseMessage> LoginAsync(WebApplicationFactory<Program> app, string email, string password)
    {
        using var client = app.CreateSiteClient();
        return await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);
    }

    private static async Task<AppUser> AdminAsync(WebApplicationFactory<Program> app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().GetUsersInRoleAsync(Profiles.Admin)).Single();
    }

    private static async Task<SessionTokens> StartSessionAsync(WebApplicationFactory<Program> app, Guid userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISessionService>().StartAsync(userId, Profiles.Admin, Ct);
    }

    private static async Task<int> OpenRefreshTokensAsync(WebApplicationFactory<Program> app, Guid userId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.AsNoTracking()
            .CountAsync(token => token.UserId == userId && token.RevokedAt == null, Ct);
    }

    private static void NothingShouldMentionTheValues(WebApplicationFactory<Program> app, Outcome outcome, params string?[] secrets)
    {
        var logs = string.Join("\n", app.Services.GetFakeLogCollector().GetSnapshot().Select(record => $"{record.Message} {record.Exception}"));
        foreach (var secret in secrets.Where(secret => !string.IsNullOrEmpty(secret)))
        {
            logs.ShouldNotContain(secret!, Case.Insensitive);
            outcome.Output.ShouldNotContain(secret!, Case.Insensitive);
            outcome.Error.ShouldNotContain(secret!, Case.Insensitive);
        }
    }

    [Fact]
    public async Task It_changes_the_password_and_the_admin_signs_in_with_the_new_one_only()
    {
        var email = NewEmail();
        await using var app = StartWithAdmin(email);
        (await LoginAsync(app, email, OldPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var outcome = await RunAsync(app, NewPassword);

        outcome.ExitCode.ShouldBe(0, outcome.Error);
        outcome.Output.ShouldContain("Admin password changed");
        using var old = await LoginAsync(app, email, OldPassword);
        old.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var current = await LoginAsync(app, email, NewPassword);
        current.StatusCode.ShouldBe(HttpStatusCode.OK, await current.Content.ReadAsStringAsync(Ct));
        (await current.Content.ReadFromJsonAsync<Dictionary<string, string>>(Ct))!["profile"].ShouldBe(Profiles.Admin);
        // It is still the same account, with its profile and its seed rules intact: no second admin.
        (await AdminAsync(app)).Email.ShouldBe(email);
    }

    [Fact]
    public async Task It_ends_every_session_of_the_admin()
    {
        await using var app = StartWithAdmin(NewEmail());
        var admin = await AdminAsync(app);
        var first = await StartSessionAsync(app, admin.Id);
        await StartSessionAsync(app, admin.Id);
        (await OpenRefreshTokensAsync(app, admin.Id)).ShouldBe(2);

        var outcome = await RunAsync(app, NewPassword);

        outcome.ExitCode.ShouldBe(0);
        (await OpenRefreshTokensAsync(app, admin.Id)).ShouldBe(0);
        using var client = app.CreateSiteClient();
        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh").WithCookies((SessionCookies.RefreshToken, first.RefreshToken));
        using var response = await client.SendAsync(refresh, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task It_clears_a_lockout_caused_by_the_old_password()
    {
        var email = NewEmail();
        await using var app = StartWithAdmin(email);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await LoginAsync(app, email, "palavra-passe-errada")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Locked out: even the right password is refused.
        (await LoginAsync(app, email, OldPassword)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        (await RunAsync(app, NewPassword)).ExitCode.ShouldBe(0);

        (await LoginAsync(app, email, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("curta")]
    [InlineData("1234567")]
    public async Task A_password_that_breaks_the_rules_of_US01_is_refused_and_nothing_changes(string password)
    {
        var email = NewEmail();
        await using var app = StartWithAdmin(email);
        var admin = await AdminAsync(app);
        await StartSessionAsync(app, admin.Id);

        var outcome = await RunAsync(app, password);

        outcome.ExitCode.ShouldBe(1);
        outcome.Error.ShouldContain("at least 8 characters");
        outcome.Output.ShouldBeEmpty();
        (await LoginAsync(app, email, OldPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await OpenRefreshTokensAsync(app, admin.Id)).ShouldBeGreaterThanOrEqualTo(1);
        NothingShouldMentionTheValues(app, outcome, password);
    }

    [Fact]
    public async Task A_password_of_exactly_eight_characters_is_accepted()
    {
        var email = NewEmail();
        await using var app = StartWithAdmin(email);

        (await RunAsync(app, "12345678")).ExitCode.ShouldBe(0);

        (await LoginAsync(app, email, "12345678")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Without_the_environment_variable_it_refuses(string? value)
    {
        var email = NewEmail();
        await using var app = StartWithAdmin(email);

        var outcome = await RunAsync(app, value);

        outcome.ExitCode.ShouldBe(1);
        outcome.Error.ShouldContain(AdminPasswordCommand.PasswordVariable);
        (await LoginAsync(app, email, OldPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_password_is_never_taken_from_the_command_line_and_the_argument_is_not_echoed()
    {
        var email = NewEmail();
        await using var app = StartWithAdmin(email);
        const string onCommandLine = "palavra-passe-na-linha-de-comandos";

        var outcome = await RunAsync(app, NewPassword, [AdminPasswordCommand.Name, onCommandLine]);

        outcome.ExitCode.ShouldBe(1);
        outcome.Error.ShouldContain("never on the command line");
        // Neither the argument nor the (valid) variable was used: the old password still works.
        (await LoginAsync(app, email, OldPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        NothingShouldMentionTheValues(app, outcome, onCommandLine, NewPassword);
    }

    [Fact]
    public async Task Without_an_admin_account_it_refuses()
    {
        await using var app = factory.CreateIsolatedApp($"us04pw_{Guid.NewGuid():N}");
        _ = app.Server;

        var outcome = await RunAsync(app, NewPassword);

        outcome.ExitCode.ShouldBe(1);
        outcome.Error.ShouldContain("no admin account");
    }

    [Fact]
    public async Task Another_account_never_changes_and_the_value_and_the_email_are_never_logged_or_printed()
    {
        var email = NewEmail();
        await using var app = StartWithAdmin(email);
        var ownerEmail = $"dono-{Guid.NewGuid():N}@exemplo.test";
        await app.CreateAccountAsync(Profiles.Owner, "palavra-passe-do-dono", ownerEmail);

        var outcome = await RunAsync(app, NewPassword);

        outcome.ExitCode.ShouldBe(0);
        (await LoginAsync(app, ownerEmail, "palavra-passe-do-dono")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await LoginAsync(app, ownerEmail, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        NothingShouldMentionTheValues(app, outcome, NewPassword, OldPassword, email);
    }

    [Theory]
    [InlineData(new[] { "set-admin-password" }, true)]
    [InlineData(new[] { "set-admin-password", "x" }, true)]
    [InlineData(new[] { "seed-demo" }, false)]
    [InlineData(new string[0], false)]
    public void The_command_is_chosen_by_its_name_alone(string[] args, bool requested)
    {
        AdminPasswordCommand.IsRequested(args).ShouldBe(requested);
    }
}
