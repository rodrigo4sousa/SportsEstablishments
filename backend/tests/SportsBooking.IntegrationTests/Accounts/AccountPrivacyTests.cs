using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Common;
using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Notifications;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Accounts;

/// <summary>US30: a person downloads their own data and deletes their account. Real PostgreSQL, real password hashes.</summary>
public sealed class AccountPrivacyTests(ApiFactory factory)
{
    private const string Password = "password-de-teste-1";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Person(AppUser User, string Email, SessionTokens Tokens);

    private async Task<Person> PersonAsync(string profile = Profiles.Player, WebApplicationFactory<Program>? app = null)
    {
        var host = app ?? factory;
        var email = $"pessoa-{Guid.NewGuid():N}@exemplo.test";
        var user = await host.CreateAccountAsync(profile, Password, email, "Rita Sousa");
        await using var scope = host.Services.CreateAsyncScope();
        var tokens = await scope.ServiceProvider.GetRequiredService<ISessionService>().StartAsync(user.Id, profile, Ct);
        return new Person(user, email, tokens);
    }

    private async Task<HttpResponseMessage> ExportAsync(Person person, WebApplicationFactory<Program>? app = null)
    {
        using var client = (app ?? factory).CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me/data-export").WithCookies((SessionCookies.AccessToken, person.Tokens.AccessToken));
        return await client.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> DeleteAsync(Person person, string password = Password, WebApplicationFactory<Program>? app = null)
    {
        using var client = (app ?? factory).CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/delete-account") { Content = JsonContent.Create(new { password }) }
            .WithCookies((SessionCookies.AccessToken, person.Tokens.AccessToken));
        return await client.SendAsync(request, Ct);
    }

    private async Task<AppUser> ReloadAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking().SingleAsync(user => user.Id == id, Ct);
    }

    private async Task<Booking> BookingAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Bookings.AsNoTracking().SingleAsync(booking => booking.Id == id, Ct);
    }

    private async Task<Guid> EnqueueAsync(string type, string payload)
    {
        var message = OutboxMessage.Create(type, payload, DateTime.UtcNow);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.OutboxMessages.Add(message);
        await dbContext.SaveChangesAsync(Ct);
        return message.Id;
    }

    private async Task<bool> OutboxHasAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages.AnyAsync(message => message.Id == id, Ct);
    }

    private static BookingEmailPayload EmailPayload(Guid playerId) => new(
        playerId, "Clube", "Rua 1", "Porto", "Europe/Lisbon", "Campo", DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1), 20m, null);

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe(code);
    }

    private static DateTime Days(int days) => new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day, 10, 0, 0, DateTimeKind.Utc).AddDays(days);

    private async Task<(TestClub Club, TestPitch Pitch)> VenueAsync()
    {
        var club = await factory.CreateClubAsync(name: $"Clube Export {Guid.NewGuid():N}");
        return (club, await factory.CreatePitchAsync(club.Id, "Campo Export"));
    }

    [Fact]
    public async Task Export_returns_only_the_callers_own_data_as_a_file()
    {
        var person = await PersonAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByIdAsync(person.User.Id.ToString())).ShouldNotBeNull();
            user.RecordTermsAcceptance("1.0", new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc));
            user.PhoneNumber = "912345678";
            (await users.UpdateAsync(user)).Succeeded.ShouldBeTrue();
        }

        var (club, pitch) = await VenueAsync();
        var booking = await factory.AddBookingAsync(pitch.Id, Days(3), Days(3).AddHours(1), person.User);
        var other = await PersonAsync();
        var otherBooking = await factory.AddBookingAsync(pitch.Id, Days(4), Days(4).AddHours(1), other.User);

        var response = await ExportAsync(person);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        response.Content.Headers.ContentDisposition?.DispositionType.ShouldBe("attachment");
        response.Content.Headers.ContentDisposition?.FileName.ShouldBe("os-meus-dados.json");
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();

        var body = await response.Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var account = root.GetProperty("account");
        account.GetProperty("name").GetString().ShouldBe("Rita Sousa");
        account.GetProperty("email").GetString().ShouldBe(person.Email);
        account.GetProperty("phone").GetString().ShouldBe("912345678");
        account.GetProperty("profile").GetString().ShouldBe(Profiles.Player);
        root.GetProperty("legalAcceptance").GetProperty("version").GetString().ShouldBe("1.0");
        root.GetProperty("legalAcceptance").GetProperty("acceptedAt").GetDateTime().ShouldBe(new DateTime(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc));

        var exported = root.GetProperty("bookings").EnumerateArray().ShouldHaveSingleItem();
        exported.GetProperty("clubName").GetString().ShouldBe(club.Name);
        exported.GetProperty("pitchName").GetString().ShouldBe("Campo Export");
        exported.GetProperty("sport").GetString().ShouldBe("padel");
        exported.GetProperty("startsAt").GetDateTime().ShouldBe(Days(3));
        exported.GetProperty("status").GetString().ShouldBe("confirmed");

        var stored = await ReloadAsync(person.User.Id);
        foreach (var secret in new[]
        {
            stored.PasswordHash!, stored.SecurityStamp!, person.Tokens.AccessToken, person.Tokens.RefreshToken,
            person.User.Id.ToString(), booking.Id.ToString(), pitch.Id.ToString(), club.Id.ToString(),
            other.Email, other.User.Id.ToString(), otherBooking.Id.ToString(),
        })
        {
            body.ShouldNotContain(secret, Case.Insensitive);
        }
    }

    [Fact]
    public async Task Export_and_delete_need_a_session()
    {
        using var client = factory.CreateSiteClient();

        (await client.GetAsync("/api/me/data-export", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/me/delete-account", new { password = Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Delete_needs_the_sites_origin()
    {
        var person = await PersonAsync();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(ApiFactory.SiteOrigin), HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/delete-account") { Content = JsonContent.Create(new { password = Password }) }
            .WithCookies((SessionCookies.AccessToken, person.Tokens.AccessToken));

        await ShouldBeProblemAsync(await client.SendAsync(request, Ct), HttpStatusCode.Forbidden, ErrorCodes.OriginNotAllowed);

        (await ReloadAsync(person.User.Id)).DeletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Delete_anonymizes_the_account_and_clears_the_cookies()
    {
        var person = await PersonAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByIdAsync(person.User.Id.ToString())).ShouldNotBeNull();
            user.PhoneNumber = "912345678";
            user.RecordTermsAcceptance("1.0", DateTime.UtcNow);
            (await users.UpdateAsync(user)).Succeeded.ShouldBeTrue();
        }

        var before = await ReloadAsync(person.User.Id);

        var response = await DeleteAsync(person);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cookies = response.SetCookies();
        cookies[SessionCookies.AccessToken].Expires.ShouldNotBeNull().ShouldBeLessThan(DateTimeOffset.UtcNow);
        cookies[SessionCookies.RefreshToken].Expires.ShouldNotBeNull().ShouldBeLessThan(DateTimeOffset.UtcNow);

        var after = await ReloadAsync(person.User.Id);
        var tombstone = DeletedAccount.TombstoneEmail(person.User.Id);
        after.Name.ShouldBe(DeletedAccount.PlaceholderName);
        after.Email.ShouldBe(tombstone);
        after.UserName.ShouldBe(tombstone);
        after.NormalizedEmail.ShouldBe(tombstone.ToUpperInvariant());
        after.NormalizedUserName.ShouldBe(tombstone.ToUpperInvariant());
        after.PasswordHash.ShouldBeNull();
        after.SecurityStamp.ShouldNotBe(before.SecurityStamp);
        after.PhoneNumber.ShouldBeNull();
        after.AcceptedTermsVersion.ShouldBeNull();
        after.TermsAcceptedAt.ShouldBeNull();
        after.DeletedAt.ShouldNotBeNull();
        after.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_cancels_future_bookings_and_frees_their_slots()
    {
        var person = await PersonAsync();
        var (_, pitch) = await VenueAsync();
        var future = await factory.AddBookingAsync(pitch.Id, Days(5), Days(5).AddHours(1), person.User);

        (await DeleteAsync(person)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var cancelled = await BookingAsync(future.Id);
        cancelled.Status.ShouldBe(BookingStatus.CancelledByPlayer);
        cancelled.CancelledAt.ShouldNotBeNull();
        // The exclusion constraint would refuse this one if the slot were still taken.
        var again = await factory.AddBookingAsync(pitch.Id, Days(5), Days(5).AddHours(1));
        (await BookingAsync(again.Id)).Status.ShouldBe(BookingStatus.Confirmed);
    }

    [Fact]
    public async Task Delete_ignores_the_cancellation_window()
    {
        var person = await PersonAsync();
        var (_, pitch) = await VenueAsync();
        var soon = await factory.AddBookingAsync(pitch.Id, DateTime.UtcNow.AddHours(1), DateTime.UtcNow.AddHours(2), person.User);

        (await DeleteAsync(person)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await BookingAsync(soon.Id)).Status.ShouldBe(BookingStatus.CancelledByPlayer);
    }

    [Fact]
    public async Task Delete_keeps_past_bookings_with_no_personal_data()
    {
        var person = await PersonAsync();
        var (_, pitch) = await VenueAsync();
        var past = await factory.AddBookingAsync(pitch.Id, Days(-5), Days(-5).AddHours(1), person.User);

        (await DeleteAsync(person)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var kept = await BookingAsync(past.Id);
        kept.Status.ShouldBe(BookingStatus.Confirmed);
        kept.PlayerId.ShouldBe(person.User.Id);
        await using var scope = factory.Services.CreateAsyncScope();
        var name = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AccountNames.SingleAsync(account => account.Id == person.User.Id, Ct);
        name.Name.ShouldBe(DeletedAccount.PlaceholderName);
    }

    [Fact]
    public async Task A_wrong_password_changes_nothing()
    {
        var person = await PersonAsync();
        var (_, pitch) = await VenueAsync();
        var future = await factory.AddBookingAsync(pitch.Id, Days(6), Days(6).AddHours(1), person.User);

        var response = await DeleteAsync(person, "outra-password-qualquer");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(ErrorCodes.InvalidPassword);
        problem.GetProperty("errors").GetProperty("password")[0].GetString().ShouldBe("A palavra-passe está incorreta.");
        response.SetCookies().ShouldBeEmpty();
        var after = await ReloadAsync(person.User.Id);
        after.Email.ShouldBe(person.Email);
        after.DeletedAt.ShouldBeNull();
        after.AccessFailedCount.ShouldBe(1);
        (await BookingAsync(future.Id)).Status.ShouldBe(BookingStatus.Confirmed);
    }

    [Fact]
    public async Task A_missing_password_is_a_field_error()
    {
        var person = await PersonAsync();

        var response = await DeleteAsync(person, string.Empty);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(ErrorCodes.ValidationFailed);
        problem.GetProperty("errors").GetProperty("password")[0].GetString().ShouldBe("Indica a palavra-passe.");
    }

    [Fact]
    public async Task An_owner_with_clubs_cannot_delete_the_account()
    {
        var owner = await PersonAsync(Profiles.Owner);
        await factory.CreateClubAsync(owner.User.Id);

        await ShouldBeProblemAsync(await DeleteAsync(owner), HttpStatusCode.UnprocessableEntity, ErrorCodes.OwnerHasClubs);

        (await ReloadAsync(owner.User.Id)).Email.ShouldBe(owner.Email);
    }

    [Fact]
    public async Task An_owner_without_clubs_can_delete_the_account_and_leaves_the_admin_lists()
    {
        var owner = await PersonAsync(Profiles.Owner);

        (await DeleteAsync(owner)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ReloadAsync(owner.User.Id)).Name.ShouldBe(DeletedAccount.PlaceholderName);
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await dbContext.OwnerAccounts.AnyAsync(account => account.Id == owner.User.Id, Ct)).ShouldBeFalse();
        (await dbContext.AccountProfiles.AnyAsync(account => account.Id == owner.User.Id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task An_admin_cannot_delete_the_account()
    {
        // An isolated database: the platform has one admin, and the shared one may hold it.
        await using var app = factory.CreateIsolatedApp($"us30_admin_{Guid.NewGuid():N}");
        _ = app.Server;
        var admin = await PersonAsync(Profiles.Admin, app);

        await ShouldBeProblemAsync(await DeleteAsync(admin, app: app), HttpStatusCode.UnprocessableEntity, ErrorCodes.AdminCannotDeleteAccount);
    }

    [Fact]
    public async Task After_deleting_every_session_stops_working_at_once()
    {
        var person = await PersonAsync();
        // A second device, signed in with its own tokens.
        SessionTokens otherDevice;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            otherDevice = await scope.ServiceProvider.GetRequiredService<ISessionService>().StartAsync(person.User.Id, Profiles.Player, Ct);
        }

        (await DeleteAsync(person)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var client = factory.CreateSiteClient();
        foreach (var accessToken in new[] { person.Tokens.AccessToken, otherDevice.AccessToken })
        {
            using var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me").WithCookies((SessionCookies.AccessToken, accessToken));
            (await client.SendAsync(me, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        foreach (var refreshToken in new[] { person.Tokens.RefreshToken, otherDevice.RefreshToken })
        {
            using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh").WithCookies((SessionCookies.RefreshToken, refreshToken));
            (await client.SendAsync(refresh, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        await using var check = factory.Services.CreateAsyncScope();
        (await check.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.AnyAsync(token => token.UserId == person.User.Id, Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_removes_the_pending_emails_of_the_account_and_no_others()
    {
        var person = await PersonAsync();
        var other = await PersonAsync();
        var (_, pitch) = await VenueAsync();
        var future = await factory.AddBookingAsync(pitch.Id, Days(7), Days(7).AddHours(1), person.User);
        var otherBooking = await factory.AddBookingAsync(pitch.Id, Days(8), Days(8).AddHours(1), other.User);

        var bookingEmail = await EnqueueAsync(OutboxMessageTypes.BookingConfirmed, EmailPayload(person.User.Id).Serialize());
        var accountEmail = await EnqueueAsync(OutboxMessageTypes.PasswordChanged, new AccountEmailPayload(person.User.Id).Serialize());
        var reminder = await EnqueueAsync(OutboxMessageTypes.BookingReminder, new BookingRefPayload(future.Id).Serialize());
        var ofOther = await EnqueueAsync(OutboxMessageTypes.BookingConfirmed, EmailPayload(other.User.Id).Serialize());
        var reminderOfOther = await EnqueueAsync(OutboxMessageTypes.BookingReminder, new BookingRefPayload(otherBooking.Id).Serialize());

        (await DeleteAsync(person)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await OutboxHasAsync(bookingEmail)).ShouldBeFalse();
        (await OutboxHasAsync(accountEmail)).ShouldBeFalse();
        (await OutboxHasAsync(reminder)).ShouldBeFalse();
        (await OutboxHasAsync(ofOther)).ShouldBeTrue();
        (await OutboxHasAsync(reminderOfOther)).ShouldBeTrue();
    }

    [Fact]
    public async Task An_email_written_after_the_deletion_is_never_sent()
    {
        var sender = new FakeEmailSender();
        await using var app = factory.CreateIsolatedApp($"us30_outbox_{Guid.NewGuid():N}")
            .WithWebHostBuilder(builder => Microsoft.AspNetCore.TestHost.WebHostBuilderExtensions.ConfigureTestServices(builder, services => services.AddSingleton<IEmailSender>(sender)));
        _ = app.Server;
        var person = await PersonAsync(app: app);
        (await DeleteAsync(person, app: app)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var message = OutboxMessage.Create(OutboxMessageTypes.BookingConfirmed, EmailPayload(person.User.Id).Serialize(), DateTime.UtcNow);
        await using var scope = app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.OutboxMessages.Add(message);
        await dbContext.SaveChangesAsync(Ct);

        await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessDueAsync(Ct);

        sender.Sent.ShouldBeEmpty();
        var closed = await dbContext.OutboxMessages.AsNoTracking().SingleAsync(row => row.Id == message.Id, Ct);
        closed.SkippedAt.ShouldNotBeNull();
        closed.LastError.ShouldBe(OutboxProcessor.SkippedAccountDeleted);
    }

    [Fact]
    public async Task The_old_email_cannot_log_in_and_can_register_again()
    {
        var person = await PersonAsync();
        (await DeleteAsync(person)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var client = factory.CreateSiteClient();
        await ShouldBeProblemAsync(
            await client.PostAsJsonAsync("/api/auth/login", new { email = person.Email, password = Password }, Ct),
            HttpStatusCode.Unauthorized,
            ErrorCodes.InvalidCredentials);

        var registered = await client.PostAsJsonAsync(
            "/api/auth/register",
            new { name = "Outra Pessoa", email = person.Email, password = Password, acceptedTermsVersion = "1.1" },
            Ct);

        registered.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await registered.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid().ShouldNotBe(person.User.Id);
    }

    [Theory]
    [InlineData("RateLimiting:Auth:AccountExportPermitLimit")]
    [InlineData("RateLimiting:Auth:AccountDeletePermitLimit")]
    public async Task Export_and_delete_are_rate_limited(string setting)
    {
        await using var app = factory.CreateIsolatedApp($"us30_rl_{Guid.NewGuid():N}", new Dictionary<string, string?> { [setting] = "1" });
        _ = app.Server;
        var person = await PersonAsync(app: app);
        Func<Task<HttpResponseMessage>> call = setting.Contains("Export", StringComparison.Ordinal)
            ? () => ExportAsync(person, app)
            : () => DeleteAsync(person, "password-errada-1", app);

        (await call()).StatusCode.ShouldNotBe(HttpStatusCode.TooManyRequests);

        var limited = await call();
        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        limited.Headers.Contains(HeaderNames.RetryAfter).ShouldBeTrue();
    }
}
