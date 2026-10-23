using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Auth;

/// <summary>
/// US03 login: any profile signs in with email and password; every failure is the same generic 401, so the answer
/// never reveals whether an email has an account.
/// </summary>
public sealed class LoginTests(ApiFactory factory)
{
    private const string LoginPath = "/api/auth/login";
    private const string Password = "palavra-passe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail() => $"login-{Guid.NewGuid():N}@exemplo.pt";

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string? email, string? password) =>
        await client.PostAsJsonAsync(LoginPath, new { email, password }, Ct);

    private async Task<HttpResponseMessage> LoginAsync(string? email, string? password)
    {
        using var client = factory.CreateSiteClient();
        return await LoginAsync(client, email, password);
    }

    private async Task<AppUser> FindUserAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByIdAsync(id.ToString())).ShouldNotBeNull();
    }

    /// <summary>The generic failure, with the volatile traceId removed so two answers can be compared as text.</summary>
    private static async Task<string> GenericFailureAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        response.Headers.Contains(HeaderNames.SetCookie).ShouldBeFalse();
        var problem = (await response.Content.ReadFromJsonAsync<JsonObject>(Ct)).ShouldNotBeNull();
        problem["code"]!.GetValue<string>().ShouldBe(ErrorCodes.InvalidCredentials);
        problem["detail"]!.GetValue<string>().ShouldBe("Email ou palavra-passe incorretos.");
        problem["traceId"]!.GetValue<string>().ShouldNotBeNullOrWhiteSpace();
        problem.Remove("traceId");
        return problem.ToJsonString();
    }

    private static async Task ShouldSignInAsync(HttpResponseMessage response, AppUser user, string profile, HttpClient browser)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var account = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        account.GetProperty("id").GetGuid().ShouldBe(user.Id);
        account.GetProperty("email").GetString().ShouldBe(user.Email);
        account.GetProperty("name").GetString().ShouldBe(user.Name);
        account.GetProperty("profile").GetString().ShouldBe(profile);

        var cookies = response.SetCookies();
        var access = cookies[SessionCookies.AccessToken];
        access.HttpOnly.ShouldBeTrue();
        access.Secure.ShouldBeTrue();
        access.SameSite.ShouldBe(SameSiteMode.Lax);
        access.Path.ToString().ShouldBe("/");
        access.Expires.ShouldNotBeNull().ShouldBe(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
        var refresh = cookies[SessionCookies.RefreshToken];
        refresh.HttpOnly.ShouldBeTrue();
        refresh.Secure.ShouldBeTrue();
        refresh.SameSite.ShouldBe(SameSiteMode.Strict);
        refresh.Path.ToString().ShouldBe("/api/auth");
        refresh.Expires.ShouldNotBeNull().ShouldBe(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));

        // Same body as /me, which now answers from the cookie.
        var me = await browser.GetFromJsonAsync<JsonElement>("/api/auth/me", Ct);
        me.GetRawText().ShouldBe(account.GetRawText());
    }

    [Theory]
    [InlineData(Profiles.Player)]
    [InlineData(Profiles.Owner)]
    [InlineData(Profiles.Admin)]
    public async Task Each_profile_signs_in_and_gets_both_session_cookies(string profile)
    {
        var user = await factory.CreateAccountAsync(profile, Password, NewEmail());
        using var browser = factory.CreateBrowserClient();

        using var response = await LoginAsync(browser, user.Email, Password);

        await ShouldSignInAsync(response, user, profile, browser);
    }

    [Fact]
    public async Task The_seeded_admin_signs_in_with_the_seed_credentials()
    {
        var email = NewEmail();
        await using var app = factory.CreateIsolatedApp(
            $"us03_{Guid.NewGuid():N}",
            new Dictionary<string, string?> { ["AdminSeed:Email"] = email, ["AdminSeed:Password"] = "admin-palavra-passe" });
        using var browser = app.CreateBrowserClient();
        AppUser admin;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            admin = (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email)).ShouldNotBeNull();
        }

        using var response = await LoginAsync(browser, email, "admin-palavra-passe");

        await ShouldSignInAsync(response, admin, Profiles.Admin, browser);
    }

    [Fact]
    public async Task Email_is_matched_ignoring_case_and_surrounding_whitespace()
    {
        var user = await factory.CreateAccountAsync(Profiles.Player, Password, NewEmail());

        using var response = await LoginAsync($"  {user.Email!.ToUpperInvariant()} ", Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid().ShouldBe(user.Id);
    }

    [Fact]
    public async Task Password_is_used_exactly_as_typed()
    {
        var user = await factory.CreateAccountAsync(Profiles.Player, Password, NewEmail());

        using var response = await LoginAsync(user.Email, $" {Password} ");

        await GenericFailureAsync(response);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_identical_response()
    {
        var user = await factory.CreateAccountAsync(Profiles.Player, Password, NewEmail());

        using var wrongPassword = await LoginAsync(user.Email, "outra-palavra-passe");
        using var unknownEmail = await LoginAsync(NewEmail(), "outra-palavra-passe");
        using var malformedEmail = await LoginAsync("nao-e-um-email", "outra-palavra-passe");

        var expected = await GenericFailureAsync(wrongPassword);
        (await GenericFailureAsync(unknownEmail)).ShouldBe(expected);
        (await GenericFailureAsync(malformedEmail)).ShouldBe(expected);
    }

    [Fact]
    public async Task Account_is_locked_after_repeated_wrong_passwords_with_the_same_generic_response()
    {
        var user = await factory.CreateAccountAsync(Profiles.Player, Password, NewEmail());
        using var unknownEmail = await LoginAsync(NewEmail(), "outra-palavra-passe");
        var expected = await GenericFailureAsync(unknownEmail);

        for (var attempt = 0; attempt < LoginLockout.MaxFailedAttempts; attempt++)
        {
            using var wrong = await LoginAsync(user.Email, "outra-palavra-passe");
            (await GenericFailureAsync(wrong)).ShouldBe(expected);
        }

        // Locked: even the right password gets the generic answer, and no session.
        using var locked = await LoginAsync(user.Email, Password);
        (await GenericFailureAsync(locked)).ShouldBe(expected);
        var stored = await FindUserAsync(user.Id);
        stored.LockoutEnd.ShouldNotBeNull().ShouldBe(DateTimeOffset.UtcNow.Add(LoginLockout.Duration), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task A_successful_login_resets_the_failed_attempts()
    {
        var user = await factory.CreateAccountAsync(Profiles.Player, Password, NewEmail());
        for (var attempt = 0; attempt < LoginLockout.MaxFailedAttempts - 1; attempt++)
        {
            using var wrong = await LoginAsync(user.Email, "outra-palavra-passe");
        }

        using var success = await LoginAsync(user.Email, Password);
        success.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var oneMoreWrong = await LoginAsync(user.Email, "outra-palavra-passe");
        using var stillOpen = await LoginAsync(user.Email, Password);

        stillOpen.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await FindUserAsync(user.Id)).LockoutEnd.ShouldBeNull();
    }

    [Fact]
    public async Task Deactivated_account_cannot_sign_in_even_with_the_right_password()
    {
        var user = await factory.CreateAccountAsync(Profiles.Owner, Password, NewEmail());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString())).ShouldNotBeNull();
            stored.Deactivate(DateTime.UtcNow);
            (await users.UpdateAsync(stored)).Succeeded.ShouldBeTrue();
        }

        using var response = await LoginAsync(user.Email, Password);
        using var unknownEmail = await LoginAsync(NewEmail(), Password);

        (await GenericFailureAsync(response)).ShouldBe(await GenericFailureAsync(unknownEmail));
    }

    [Fact]
    public async Task Empty_fields_are_400_validation_failed_with_a_pt_pt_message_per_field()
    {
        using var response = await LoginAsync(" ", null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(ErrorCodes.ValidationFailed);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        var errors = problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
        errors["email"].ShouldBe(["Indica o teu email."]);
        errors["password"].ShouldBe(["Indica a palavra-passe."]);
    }

    [Fact]
    public async Task Login_from_another_site_is_rejected()
    {
        var user = await factory.CreateAccountAsync(Profiles.Player, Password, NewEmail());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(ApiFactory.SiteOrigin) });
        client.DefaultRequestHeaders.Add(HeaderNames.Origin, "https://outro-site.example");

        using var response = await LoginAsync(client, user.Email, Password);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.Contains(HeaderNames.SetCookie).ShouldBeFalse();
    }
}
