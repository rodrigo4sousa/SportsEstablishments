using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Notifications;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Auth;

/// <summary>
/// US05: a forgotten password is reset with a link sent by email. Each test has its own database (so the outbox holds only
/// its messages), the fake email sender and a fake clock that starts at the real time: Identity's reset token reads the
/// system clock, so the two must agree.
/// </summary>
public sealed partial class PasswordResetTests(ApiFactory factory)
{
    private const string OldPassword = "palavra-passe-antiga";
    private const string NewPassword = "palavra-passe-nova-1";
    private const string RequestPath = "/api/auth/password-reset/request";
    private const string ConfirmPath = "/api/auth/password-reset/confirm";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Every host has its own connection pool and PostgreSQL allows 100 connections to the whole suite: few at once, small pools.</summary>
    private static readonly SemaphoreSlim Hosts = new(4);

    private sealed record Site(WebApplicationFactory<Program> App, FakeEmailSender Sender, FakeTimeProvider Clock, string ConnectionString) : IAsyncDisposable
    {
        public async Task<int> SendDueEmailsAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessDueAsync(Ct);
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            NpgsqlConnection.ClearPool(new NpgsqlConnection(ConnectionString));
            Hosts.Release();
        }
    }

    private Site Start(string? database = null)
    {
        Hosts.Wait(Ct);
        var sender = new FakeEmailSender();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        database ??= $"us05_{Guid.NewGuid():N}";
        var connectionString = factory.ConnectionStringFor(database) + ";Maximum Pool Size=6;Minimum Pool Size=0";
        var app = factory.CreateIsolatedApp(database, new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString })
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IEmailSender>(sender);
                services.AddSingleton<TimeProvider>(clock);
            }));
        _ = app.Server;
        return new Site(app, sender, clock, connectionString);
    }

    private static string NewEmail(string who = "conta") => $"{who}-{Guid.NewGuid():N}@exemplo.test";

    private static async Task<AppUser> AccountAsync(Site site, string profile, string? email = null, string name = "Rita Silva") =>
        await site.App.CreateAccountAsync(profile, OldPassword, email ?? NewEmail(), name);

    private static async Task<HttpResponseMessage> PostAsync(Site site, string path, object body)
    {
        using var client = site.App.CreateSiteClient();
        return await client.PostAsJsonAsync(path, body, Ct);
    }

    private static async Task<HttpResponseMessage> RequestAsync(Site site, string email) => await PostAsync(site, RequestPath, new { email });

    private static async Task<HttpResponseMessage> ConfirmAsync(Site site, string? email, string? token, string newPassword = NewPassword) =>
        await PostAsync(site, ConfirmPath, new { email, token, newPassword });

    private static async Task<HttpStatusCode> LoginAsync(Site site, string email, string password)
    {
        using var response = await PostAsync(site, "/api/auth/login", new { email, password });
        return response.StatusCode;
    }

    private static async Task<List<OutboxMessage>> OutboxAsync(Site site, string? type = null)
    {
        await using var scope = site.App.Services.CreateAsyncScope();
        var messages = await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AsNoTracking().OrderBy(message => message.CreatedAt).ToListAsync(Ct);
        return messages.Where(message => type is null || message.Type == type).ToList();
    }

    [GeneratedRegex(@"\S*/reset-password\?\S*")]
    private static partial Regex LinkPattern();

    private sealed record Link(string Url, string Email, string Token);

    /// <summary>The link of the last reset email sent to <paramref name="email"/>, with its query read back.</summary>
    private static Link LastLink(Site site, string email)
    {
        var message = site.Sender.Sent.Last(sent => sent.ToAddress == email && sent.Subject == "Definir uma nova palavra-passe");
        var url = LinkPattern().Match(message.TextBody).Value;
        var query = new Uri(url).Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
        return new Link(url, query["email"], query["token"]);
    }

    /// <summary>Asks for a reset and has the worker send it; returns the link.</summary>
    private static async Task<Link> EmailedLinkAsync(Site site, string email)
    {
        using var response = await RequestAsync(site, email);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await site.SendDueEmailsAsync()).ShouldBeGreaterThan(0);
        return LastLink(site, email);
    }

    /// <summary>
    /// A reset token made exactly as Identity's <c>DataProtectorTokenProvider</c> makes one, but created at
    /// <paramref name="createdAt"/>: the provider takes its time from the system clock, so this is how a test gets an old link.
    /// </summary>
    private static async Task<string> TokenCreatedAtAsync(Site site, AppUser account, DateTimeOffset createdAt)
    {
        await using var scope = site.App.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await userManager.FindByIdAsync(account.Id.ToString())).ShouldNotBeNull();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("DataProtectorTokenProvider");
        using var stream = new MemoryStream();
        await using (var writer = new BinaryWriter(stream, new UTF8Encoding(false, true), leaveOpen: true))
        {
            writer.Write(createdAt.UtcTicks);
            writer.Write(user.Id.ToString());
            writer.Write(UserManager<AppUser>.ResetPasswordTokenPurpose);
            writer.Write(await userManager.GetSecurityStampAsync(user));
        }

        return Convert.ToBase64String(protector.Protect(stream.ToArray()));
    }

    private static async Task<string> GenericFailureAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        var problem = JsonDocument.Parse(body).RootElement;
        problem.GetProperty("code").GetString().ShouldBe("invalid_or_expired_reset_link");
        problem.TryGetProperty("errors", out _).ShouldBeFalse();
        return Regex.Replace(body, "\"traceId\":\"[^\"]*\"", string.Empty);
    }

    // ---- Asking for a link

    [Fact]
    public async Task Every_email_gets_the_same_answer_whether_it_has_an_account_or_not()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);
        var owner = await AccountAsync(site, Profiles.Owner);
        var deactivatedOwner = await AccountAsync(site, Profiles.Owner);
        await site.App.DeactivateAccountAsync(deactivatedOwner.Id);
        var admin = await AccountAsync(site, Profiles.Admin);

        var answers = new List<string>();
        foreach (var email in new[] { player.Email!, owner.Email!, NewEmail("ninguem"), deactivatedOwner.Email!, admin.Email! })
        {
            using var response = await RequestAsync(site, email);
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted, email);
            response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
            response.Headers.Contains("Set-Cookie").ShouldBeFalse();
            answers.Add(await response.Content.ReadAsStringAsync(Ct));
        }

        answers.Distinct().ShouldHaveSingleItem();
        JsonDocument.Parse(answers[0]).RootElement.GetProperty("message").GetString().ShouldBe(PasswordResetRequested.Always.Message);
    }

    [Fact]
    public async Task Only_active_players_and_owners_get_an_email_and_the_outbox_holds_their_id_and_nothing_else()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);
        var owner = await AccountAsync(site, Profiles.Owner);
        var deactivatedOwner = await AccountAsync(site, Profiles.Owner);
        await site.App.DeactivateAccountAsync(deactivatedOwner.Id);
        var deactivatedPlayer = await AccountAsync(site, Profiles.Player);
        await site.App.DeactivateAccountAsync(deactivatedPlayer.Id);
        var admin = await AccountAsync(site, Profiles.Admin);

        foreach (var email in new[] { player.Email!, owner.Email!, NewEmail("ninguem"), deactivatedOwner.Email!, deactivatedPlayer.Email!, admin.Email! })
        {
            (await RequestAsync(site, email)).Dispose();
        }

        var queued = await OutboxAsync(site);
        queued.ShouldAllBe(message => message.Type == OutboxMessageTypes.PasswordReset);
        queued.Select(message => AccountEmailPayload.Deserialize(message.Payload).AccountId).ShouldBe([player.Id, owner.Id], ignoreOrder: true);
        foreach (var message in queued)
        {
            // Only the id: no token, no email, no link.
            using var payload = JsonDocument.Parse(message.Payload);
            payload.RootElement.EnumerateObject().Select(property => property.Name).ShouldBe(["accountId"]);
        }

        (await site.SendDueEmailsAsync()).ShouldBe(2);
        site.Sender.Sent.Select(sent => sent.ToAddress).ShouldBe([player.Email!, owner.Email!], ignoreOrder: true);
    }

    [Fact]
    public async Task The_email_is_found_whatever_its_case_and_the_spaces_around_it()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);

        (await RequestAsync(site, $"  {player.Email!.ToUpperInvariant()} ")).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        (await OutboxAsync(site, OutboxMessageTypes.PasswordReset)).ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("não-é-um-email")]
    public async Task A_missing_or_malformed_email_is_a_400_on_the_field(string email)
    {
        await using var site = Start();

        using var response = await RequestAsync(site, email);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").EnumerateObject().Select(property => property.Name).ShouldBe(["email"]);
    }

    [Fact]
    public async Task One_reset_email_per_account_every_five_minutes_with_the_same_answer_meanwhile()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);
        var other = await AccountAsync(site, Profiles.Player);

        var answers = new List<string>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await RequestAsync(site, player.Email!);
            response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
            answers.Add(await response.Content.ReadAsStringAsync(Ct));
        }

        // Another account is not held back by the first one.
        (await RequestAsync(site, other.Email!)).Dispose();
        site.Clock.Advance(PasswordResetRules.ThrottleInterval - TimeSpan.FromSeconds(1));
        (await RequestAsync(site, player.Email!)).Dispose();
        site.Clock.Advance(TimeSpan.FromSeconds(1));
        (await RequestAsync(site, player.Email!)).Dispose();

        answers.Distinct().ShouldHaveSingleItem();
        var queued = await OutboxAsync(site, OutboxMessageTypes.PasswordReset);
        queued.Count(message => AccountEmailPayload.Deserialize(message.Payload).AccountId == player.Id).ShouldBe(2);
        queued.Count(message => AccountEmailPayload.Deserialize(message.Payload).AccountId == other.Id).ShouldBe(1);
    }

    [Fact]
    public async Task Requests_at_the_same_moment_queue_one_email()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RequestAsync(site, player.Email!)));

        responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Accepted);
        (await OutboxAsync(site, OutboxMessageTypes.PasswordReset)).ShouldHaveSingleItem();
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    // ---- The email and the link

    [Fact]
    public async Task The_worker_sends_a_link_to_the_reset_page_that_sets_the_new_password()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player, name: "Rita Silva");

        var link = await EmailedLinkAsync(site, player.Email!);

        var email = site.Sender.Sent.Single();
        email.ToName.ShouldBe("Rita Silva");
        email.TextBody.ShouldContain("Olá Rita Silva,");
        email.TextBody.ShouldContain("1 hora");
        email.HtmlBody.ShouldContain("Definir nova palavra-passe</a>");
        link.Url.ShouldStartWith($"{ApiFactory.SiteOrigin}/reset-password?");
        link.Email.ShouldBe(player.Email);

        using var confirmed = await ConfirmAsync(site, link.Email, link.Token);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirmed.Content.ReadAsStringAsync(Ct));
        // No automatic login.
        confirmed.Headers.Contains("Set-Cookie").ShouldBeFalse();
        (await LoginAsync(site, player.Email!, OldPassword)).ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginAsync(site, player.Email!, NewPassword)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_owner_resets_too()
    {
        await using var site = Start();
        var owner = await AccountAsync(site, Profiles.Owner);

        var link = await EmailedLinkAsync(site, owner.Email!);

        (await ConfirmAsync(site, link.Email, link.Token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await LoginAsync(site, owner.Email!, NewPassword)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_link_works_once()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);
        var link = await EmailedLinkAsync(site, player.Email!);
        (await ConfirmAsync(site, link.Email, link.Token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await GenericFailureAsync(await ConfirmAsync(site, link.Email, link.Token, "outra-palavra-passe"));

        (await LoginAsync(site, player.Email!, NewPassword)).ShouldBe(HttpStatusCode.OK);
        (await LoginAsync(site, player.Email!, "outra-palavra-passe")).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_link_sent_before_the_api_restarted_still_works_with_the_new_host()
    {
        // Two hosts, one after the other, on the same database: what the second one knows about the keys it reads from there.
        var database = $"us05_{Guid.NewGuid():N}";
        string email;
        Link link;
        await using (var before = Start(database))
        {
            var player = await AccountAsync(before, Profiles.Player);
            email = player.Email!;
            link = await EmailedLinkAsync(before, email);
        }

        await using var after = Start(database);
        await using (var scope = after.App.Services.CreateAsyncScope())
        {
            // The keys are in PostgreSQL, not in a directory of the first host.
            (await scope.ServiceProvider.GetRequiredService<AppDbContext>().DataProtectionKeys.CountAsync(Ct)).ShouldBeGreaterThanOrEqualTo(1);
        }

        using var confirmed = await ConfirmAsync(after, link.Email, link.Token);
        confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent, await confirmed.Content.ReadAsStringAsync(Ct));
        (await LoginAsync(after, email, NewPassword)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_older_link_stops_working_once_a_newer_one_is_used()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);
        var older = await EmailedLinkAsync(site, player.Email!);
        site.Clock.Advance(PasswordResetRules.ThrottleInterval);
        var newer = await EmailedLinkAsync(site, player.Email!);
        newer.Token.ShouldNotBe(older.Token);

        (await ConfirmAsync(site, newer.Email, newer.Token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await GenericFailureAsync(await ConfirmAsync(site, older.Email, older.Token, "outra-palavra-passe"));
    }

    [Fact]
    public async Task A_link_works_for_an_hour_and_not_after()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);
        var expired = await TokenCreatedAtAsync(site, player, DateTimeOffset.UtcNow - PasswordResetRules.LinkLifetime - TimeSpan.FromMinutes(1));
        var stillValid = await TokenCreatedAtAsync(site, player, DateTimeOffset.UtcNow - PasswordResetRules.LinkLifetime + TimeSpan.FromMinutes(1));

        await GenericFailureAsync(await ConfirmAsync(site, player.Email, expired));

        (await ConfirmAsync(site, player.Email, stillValid)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_account_deactivated_after_asking_gets_no_email()
    {
        await using var site = Start();
        var owner = await AccountAsync(site, Profiles.Owner);
        (await RequestAsync(site, owner.Email!)).Dispose();
        await site.App.DeactivateAccountAsync(owner.Id);

        await site.SendDueEmailsAsync();

        site.Sender.Sent.ShouldBeEmpty();
        (await OutboxAsync(site, OutboxMessageTypes.PasswordReset)).Single().FailedAt.ShouldNotBeNull();
    }

    // ---- Setting the new password

    [Theory]
    [InlineData("")]
    [InlineData("1234567")]
    public async Task A_new_password_that_breaks_the_rules_of_US01_is_a_400_on_the_field_and_the_link_still_works(string password)
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);
        var link = await EmailedLinkAsync(site, player.Email!);

        using var refused = await ConfirmAsync(site, link.Email, link.Token, password);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe("validation_failed");
        problem.GetProperty("errors").GetProperty("newPassword")[0].GetString().ShouldBe("A palavra-passe tem de ter pelo menos 8 caracteres.");
        (await ConfirmAsync(site, link.Email, link.Token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Every_bad_link_is_the_same_generic_400()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);
        var other = await AccountAsync(site, Profiles.Player);
        var link = await EmailedLinkAsync(site, player.Email!);
        var deactivatedOwner = await AccountAsync(site, Profiles.Owner);
        var ownersLink = await EmailedLinkAsync(site, deactivatedOwner.Email!);
        await site.App.DeactivateAccountAsync(deactivatedOwner.Id);
        var admin = await AccountAsync(site, Profiles.Admin);
        var adminsToken = await TokenCreatedAtAsync(site, admin, DateTimeOffset.UtcNow);

        var answers = new List<string>
        {
            await GenericFailureAsync(await ConfirmAsync(site, NewEmail("ninguem"), link.Token)),
            await GenericFailureAsync(await ConfirmAsync(site, other.Email, link.Token)),
            await GenericFailureAsync(await ConfirmAsync(site, player.Email, link.Token[..^4] + "AAAA")),
            await GenericFailureAsync(await ConfirmAsync(site, player.Email, "não-é-um-token")),
            await GenericFailureAsync(await ConfirmAsync(site, player.Email, "")),
            await GenericFailureAsync(await ConfirmAsync(site, null, link.Token)),
            await GenericFailureAsync(await ConfirmAsync(site, player.Email, null)),
            // A link sent before the owner was deactivated, and a token for the admin, who resets with set-admin-password.
            await GenericFailureAsync(await ConfirmAsync(site, ownersLink.Email, ownersLink.Token)),
            await GenericFailureAsync(await ConfirmAsync(site, admin.Email, adminsToken)),
        };

        answers.Distinct().ShouldHaveSingleItem();
        // Nothing was changed, and the real link still works.
        (await LoginAsync(site, player.Email!, OldPassword)).ShouldBe(HttpStatusCode.OK);
        (await ConfirmAsync(site, link.Email, link.Token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_reset_ends_every_session_and_clears_the_lockout()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);
        var sessions = new List<string>();
        await using (var scope = site.App.Services.CreateAsyncScope())
        {
            var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
            sessions.Add((await sessionService.StartAsync(player.Id, Profiles.Player, Ct)).RefreshToken);
            sessions.Add((await sessionService.StartAsync(player.Id, Profiles.Player, Ct)).RefreshToken);
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await LoginAsync(site, player.Email!, "palavra-passe-errada")).ShouldBe(HttpStatusCode.Unauthorized);
        }

        // Locked out: even the right password is refused now.
        (await LoginAsync(site, player.Email!, OldPassword)).ShouldBe(HttpStatusCode.Unauthorized);
        var link = await EmailedLinkAsync(site, player.Email!);

        (await ConfirmAsync(site, link.Email, link.Token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await LoginAsync(site, player.Email!, NewPassword)).ShouldBe(HttpStatusCode.OK);
        foreach (var refreshToken in sessions)
        {
            using var client = site.App.CreateSiteClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh").WithCookies((SessionCookies.RefreshToken, refreshToken));
            using var refreshed = await client.SendAsync(request, Ct);
            refreshed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task A_reset_queues_the_password_changed_email_which_holds_no_link()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player, name: "Rui Costa");
        var link = await EmailedLinkAsync(site, player.Email!);

        (await ConfirmAsync(site, link.Email, link.Token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var changed = (await OutboxAsync(site, OutboxMessageTypes.PasswordChanged)).ShouldHaveSingleItem();
        AccountEmailPayload.Deserialize(changed.Payload).AccountId.ShouldBe(player.Id);
        (await site.SendDueEmailsAsync()).ShouldBe(1);
        var notice = site.Sender.Sent.Last();
        notice.ToAddress.ShouldBe(player.Email);
        notice.Subject.ShouldBe("A tua palavra-passe foi alterada");
        notice.TextBody.ShouldContain("Olá Rui Costa,");
        notice.TextBody.ShouldNotContain("reset-password");
        notice.HtmlBody.ShouldNotContain("href");
    }

    [Fact]
    public async Task A_refused_reset_queues_no_notice()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);

        await GenericFailureAsync(await ConfirmAsync(site, player.Email, "não-é-um-token"));

        (await OutboxAsync(site, OutboxMessageTypes.PasswordChanged)).ShouldBeEmpty();
    }

    // ---- Logs

    [Fact]
    public async Task No_email_token_link_or_password_reaches_the_logs()
    {
        await using var site = Start();
        var player = await AccountAsync(site, Profiles.Player);
        var link = await EmailedLinkAsync(site, player.Email!);
        await GenericFailureAsync(await ConfirmAsync(site, player.Email, link.Token[..^4] + "AAAA", "palavra-passe-recusada"));
        (await ConfirmAsync(site, link.Email, link.Token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await site.SendDueEmailsAsync();
        (await RequestAsync(site, NewEmail("ninguem"))).Dispose();

        var logs = string.Join("\n", site.App.Services.GetFakeLogCollector().GetSnapshot().Select(record => $"{record.Message} {record.Exception}"));
        foreach (var secret in new[] { player.Email!, link.Token, Uri.EscapeDataString(link.Token), link.Url, NewPassword, OldPassword, "palavra-passe-recusada" })
        {
            logs.ShouldNotContain(secret, Case.Insensitive);
        }

        // The outbox rows hold no secret either.
        var rows = string.Join("\n", (await OutboxAsync(site)).Select(message => $"{message.Payload} {message.LastError}"));
        rows.ShouldNotContain(link.Token);
        rows.ShouldNotContain(player.Email!, Case.Insensitive);
    }
}
