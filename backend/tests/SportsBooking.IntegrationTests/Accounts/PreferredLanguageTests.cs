using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Common;
using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Notifications;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Accounts;

/// <summary>
/// T09: the language of an account's emails (<c>users.preferred_language</c>): set at registration, changed on the account page, part of the
/// data export, cleared by a deletion, and used by the outbox worker when it writes an email.
/// </summary>
public sealed class PreferredLanguageTests(ApiFactory factory)
{
    private const string Password = "palavra-passe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"lingua-{Guid.NewGuid():N}@exemplo.test";

    private static object Registration(string email, string? preferredLanguage) =>
        new { name = "Ana Silva", email, password = Password, acceptedTermsVersion = "1.0", preferredLanguage };

    private async Task<AppUser> ReloadAsync(Guid id, WebApplicationFactory<Program>? app = null)
    {
        await using var scope = (app ?? factory).Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == id, Ct);
    }

    private async Task<AppUser?> FindByEmailAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var normalized = email.ToUpperInvariant();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleOrDefaultAsync(user => user.NormalizedEmail == normalized, Ct);
    }

    private async Task<(AppUser User, SessionTokens Tokens)> SignedInAsync(string profile = Profiles.Player)
    {
        var user = await factory.CreateAccountAsync(profile, Password, NewEmail(), "Rita Sousa");
        await using var scope = factory.Services.CreateAsyncScope();
        var tokens = await scope.ServiceProvider.GetRequiredService<ISessionService>().StartAsync(user.Id, profile, Ct);
        return (user, tokens);
    }

    private async Task<HttpResponseMessage> ChangeAsync(SessionTokens? tokens, object body, bool withOrigin = true)
    {
        using var client = withOrigin ? factory.CreateSiteClient() : factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/me/preferred-language") { Content = JsonContent.Create(body) };
        if (tokens is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, tokens.AccessToken));
        }

        return await client.SendAsync(request, Ct);
    }

    private static async Task SetLanguageAsync(WebApplicationFactory<Program> app, Guid userId, string? code)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users
            .Where(user => user.Id == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.PreferredLanguage, code), Ct);
    }

    // ---- Registration and /me

    [Theory]
    [InlineData("/api/auth/register", "en")]
    [InlineData("/api/auth/register", "pt-PT")]
    [InlineData("/api/auth/register/owner", "en")]
    public async Task Registration_stores_the_sites_language_and_me_returns_it(string path, string language)
    {
        using var browser = factory.CreateBrowserClient();
        var email = NewEmail();

        var response = await browser.PostAsJsonAsync(path, Registration(email, language), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("preferredLanguage").GetString().ShouldBe(language);
        (await FindByEmailAsync(email)).ShouldNotBeNull().PreferredLanguage.ShouldBe(language);
        var me = await browser.GetFromJsonAsync<JsonElement>("/api/auth/me", Ct);
        me.GetProperty("preferredLanguage").GetString().ShouldBe(language);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task A_registration_without_a_language_has_no_preference(string? language)
    {
        using var browser = factory.CreateBrowserClient();
        var email = NewEmail();

        var response = await browser.PostAsJsonAsync("/api/auth/register", Registration(email, language), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await FindByEmailAsync(email)).ShouldNotBeNull().PreferredLanguage.ShouldBeNull();
        var me = await browser.GetFromJsonAsync<JsonElement>("/api/auth/me", Ct);
        me.GetProperty("preferredLanguage").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("EN")]
    [InlineData("pt")]
    public async Task An_unsupported_language_is_a_field_error_and_no_account_is_made(string language)
    {
        using var client = factory.CreateSiteClient();
        var email = NewEmail();

        var response = await client.PostAsJsonAsync("/api/auth/register", Registration(email, language), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(ErrorCodes.ValidationFailed);
        problem.GetProperty("errors").GetProperty("preferredLanguage").GetArrayLength().ShouldBe(1);
        (await FindByEmailAsync(email)).ShouldBeNull();
    }

    // ---- Changing it on the account page

    [Theory]
    [InlineData(Profiles.Player)]
    [InlineData(Profiles.Owner)]
    [InlineData(Profiles.Admin)]
    public async Task Any_profile_can_change_the_language_of_its_emails(string profile)
    {
        var (user, tokens) = await SignedInAsync(profile);

        (await ChangeAsync(tokens, new { preferredLanguage = "en" })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReloadAsync(user.Id)).PreferredLanguage.ShouldBe("en");

        (await ChangeAsync(tokens, new { preferredLanguage = " pt-PT " })).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await ReloadAsync(user.Id)).PreferredLanguage.ShouldBe("pt-PT");
    }

    [Theory]
    [InlineData("")]
    [InlineData("de")]
    [InlineData("en-GB")]
    public async Task Changing_to_an_unsupported_language_is_a_field_error_and_changes_nothing(string language)
    {
        var (user, tokens) = await SignedInAsync();
        await SetLanguageAsync(factory, user.Id, "en");

        var response = await ChangeAsync(tokens, new { preferredLanguage = language });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(ErrorCodes.ValidationFailed);
        problem.GetProperty("errors").GetProperty("preferredLanguage").GetArrayLength().ShouldBe(1);
        (await ReloadAsync(user.Id)).PreferredLanguage.ShouldBe("en");
    }

    [Fact]
    public async Task Changing_the_language_needs_a_session_and_the_sites_origin()
    {
        var (user, tokens) = await SignedInAsync();

        (await ChangeAsync(null, new { preferredLanguage = "en" })).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ChangeAsync(tokens, new { preferredLanguage = "en" }, withOrigin: false)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ReloadAsync(user.Id)).PreferredLanguage.ShouldBeNull();
    }

    [Fact]
    public async Task The_database_accepts_no_other_language_code()
    {
        var user = await factory.CreateAccountAsync(Profiles.Player, password: null, email: NewEmail());

        var failure = await Should.ThrowAsync<PostgresException>(() => SetLanguageAsync(factory, user.Id, "fr"));

        failure.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
        failure.ConstraintName.ShouldBe("ck_users_preferred_language");
    }

    // ---- Export and deletion (US30)

    [Fact]
    public async Task The_export_includes_the_language()
    {
        var (user, tokens) = await SignedInAsync();
        await SetLanguageAsync(factory, user.Id, "en");
        using var client = factory.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me/data-export").WithCookies((SessionCookies.AccessToken, tokens.AccessToken));

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var account = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("account");
        account.GetProperty("preferredLanguage").GetString().ShouldBe("en");
    }

    [Fact]
    public async Task Deleting_the_account_clears_the_language_and_it_cannot_be_set_again()
    {
        var (user, tokens) = await SignedInAsync();
        await SetLanguageAsync(factory, user.Id, "en");
        using var client = factory.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/delete-account") { Content = JsonContent.Create(new { password = Password }) }
            .WithCookies((SessionCookies.AccessToken, tokens.AccessToken));

        (await client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ReloadAsync(user.Id)).PreferredLanguage.ShouldBeNull();
        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<Application.Accounts.IAccountStore>();
        (await store.ChangePreferredLanguageAsync(user.Id, "en", Ct)).ShouldBeFalse();
        (await ReloadAsync(user.Id)).PreferredLanguage.ShouldBeNull();
    }

    // ---- The emails (written when sent, in the recipient's current language)

    private sealed record Worker(WebApplicationFactory<Program> App, FakeEmailSender Sender) : IAsyncDisposable
    {
        public async Task RunOnceAsync()
        {
            await using var scope = App.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessDueAsync(Ct);
        }

        public ValueTask DisposeAsync() => App.DisposeAsync();
    }

    private static readonly DateTimeOffset Now = new(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);

    private Worker CreateWorker()
    {
        var sender = new FakeEmailSender();
        var app = factory.CreateIsolatedApp($"language_{Guid.NewGuid():N}")
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IEmailSender>(sender);
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
            }));
        _ = app.Server; // start it: migrations on its own database
        return new Worker(app, sender);
    }

    private static async Task EnqueueAsync(WebApplicationFactory<Program> app, string type, string payload)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.OutboxMessages.Add(OutboxMessage.Create(type, payload, Now.UtcDateTime));
        await dbContext.SaveChangesAsync(Ct);
    }

    private static BookingEmailPayload Booking(Guid playerId) => new(
        playerId, "Clube da Foz", "Rua do Mar 1", "Porto", "Europe/Lisbon", "Padel 1",
        new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 5, 4, 10, 0, 0, DateTimeKind.Utc), 18.5m, null);

    [Fact]
    public async Task A_booking_email_is_written_in_each_players_language()
    {
        await using var worker = CreateWorker();
        var english = await worker.App.CreateAccountAsync(Profiles.Player, password: null, email: NewEmail(), name: "Emma Player");
        var portuguese = await worker.App.CreateAccountAsync(Profiles.Player, password: null, email: NewEmail(), name: "Rita Jogadora");
        await SetLanguageAsync(worker.App, english.Id, "en");
        await EnqueueAsync(worker.App, OutboxMessageTypes.BookingConfirmed, Booking(english.Id).Serialize());
        await EnqueueAsync(worker.App, OutboxMessageTypes.BookingConfirmed, Booking(portuguese.Id).Serialize());

        await worker.RunOnceAsync();

        var sent = worker.Sender.Sent.ToList();
        var toEnglish = sent.Single(message => message.ToAddress == english.Email);
        toEnglish.Subject.ShouldBe("Booking confirmed: Clube da Foz, 04/05/2026 at 10:00");
        toEnglish.TextBody.ShouldContain("Date: Monday, 4 May 2026");
        toEnglish.TextBody.ShouldContain("Price: €18.50");
        toEnglish.HtmlBody.ShouldStartWith("<!DOCTYPE html><html lang=\"en\">");

        // No preference (every account made before T09): pt-PT, as before.
        var toPortuguese = sent.Single(message => message.ToAddress == portuguese.Email);
        toPortuguese.Subject.ShouldBe("Reserva confirmada: Clube da Foz, 04/05/2026 às 10:00");
        toPortuguese.HtmlBody.ShouldStartWith("<!DOCTYPE html><html lang=\"pt-PT\">");
    }

    [Fact]
    public async Task The_language_is_read_when_the_email_is_sent_not_when_it_was_queued()
    {
        await using var worker = CreateWorker();
        var player = await worker.App.CreateAccountAsync(Profiles.Player, password: null, email: NewEmail(), name: "Emma Player");
        await EnqueueAsync(worker.App, OutboxMessageTypes.BookingCancelledByPlayer, Booking(player.Id).Serialize());
        await SetLanguageAsync(worker.App, player.Id, "en");

        await worker.RunOnceAsync();

        worker.Sender.Sent.ShouldHaveSingleItem().Subject.ShouldBe("Booking cancelled: Clube da Foz, 04/05/2026 at 10:00");
    }

    [Fact]
    public async Task The_password_emails_follow_the_language_too()
    {
        await using var worker = CreateWorker();
        var player = await worker.App.CreateAccountAsync(Profiles.Player, password: Password, email: NewEmail(), name: "Emma Player");
        await SetLanguageAsync(worker.App, player.Id, "en");
        var payload = new AccountEmailPayload(player.Id).Serialize();
        await EnqueueAsync(worker.App, OutboxMessageTypes.PasswordChanged, payload);
        await EnqueueAsync(worker.App, OutboxMessageTypes.PasswordReset, payload);

        await worker.RunOnceAsync();

        var subjects = worker.Sender.Sent.Select(message => message.Subject).ToList();
        subjects.ShouldBe(["Your password was changed", "Set a new password"], ignoreOrder: true);
        worker.Sender.Sent.Single(message => message.Subject == "Set a new password").TextBody.ShouldContain("It works only once and for 1 hour:");
    }
}
