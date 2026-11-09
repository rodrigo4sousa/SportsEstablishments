using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Auth;

/// <summary>
/// US02 owner registration: <c>POST /api/auth/register/owner</c> works like player registration, with the Owner
/// profile fixed by the endpoint.
/// </summary>
public sealed class OwnerRegistrationTests(ApiFactory factory)
{
    private const string OwnerPath = "/api/auth/register/owner";
    private const string PlayerPath = "/api/auth/register";
    private const string Password = "palavra-passe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail(string prefix = "dono") => $"{prefix}-{Guid.NewGuid():N}@exemplo.pt";

    private static object Registration(string email, string name = "Rui Dono", string password = Password, string? phone = "912 345 678") =>
        new { name, email, password, phone, acceptedTermsVersion = "1.0" };

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, string? connectionAddress = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (connectionAddress is not null)
        {
            request.From(connectionAddress);
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> RegisterOwnerAsync(object body, HttpClient? client = null)
    {
        using var own = client is null ? factory.CreateSiteClient() : null;
        return await PostAsync(client ?? own!, OwnerPath, body);
    }

    private async Task<AppUser?> FindUserAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email);
    }

    private async Task<int> AccountCountAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var normalized = email.Trim().ToUpperInvariant();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync(user => user.NormalizedEmail == normalized, Ct);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        return problem;
    }

    [Fact]
    public async Task Registration_creates_an_owner_and_signs_them_in()
    {
        var email = NewEmail();
        using var browser = factory.CreateBrowserClient();

        using var response = await RegisterOwnerAsync(Registration(email), browser);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/api/auth/me");
        var account = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        account.GetProperty("name").GetString().ShouldBe("Rui Dono");
        account.GetProperty("email").GetString().ShouldBe(email);
        account.GetProperty("profile").GetString().ShouldBe(Profiles.Owner);

        var cookies = response.SetCookies();
        var access = cookies[SessionCookies.AccessToken];
        access.HttpOnly.ShouldBeTrue();
        access.Secure.ShouldBeTrue();
        access.Path.ToString().ShouldBe("/");
        var refresh = cookies[SessionCookies.RefreshToken];
        refresh.HttpOnly.ShouldBeTrue();
        refresh.Secure.ShouldBeTrue();
        refresh.Path.ToString().ShouldBe("/api/auth");

        // The session is real: /me answers from the cookie, as the owner.
        var me = await browser.GetFromJsonAsync<JsonElement>("/api/auth/me", Ct);
        me.GetProperty("profile").GetString().ShouldBe(Profiles.Owner);

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var stored = (await FindUserAsync(email)).ShouldNotBeNull();
        (await users.GetRolesAsync(stored)).ShouldBe([Profiles.Owner]);
        (await users.CheckPasswordAsync(stored, Password)).ShouldBeTrue();
        stored.PasswordHash.ShouldNotBeNull().ShouldNotContain(Password);
    }

    [Fact]
    public async Task The_new_owner_can_sign_in_again_and_gets_the_owner_area()
    {
        var email = NewEmail();
        using var created = await RegisterOwnerAsync(Registration(email));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        using var client = factory.CreateSiteClient();

        using var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct);

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("profile").GetString().ShouldBe(Profiles.Owner);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    [InlineData("Player")]
    public async Task The_profile_is_fixed_by_the_endpoint_and_never_taken_from_the_body(string requested)
    {
        var email = NewEmail("quer-ser-admin");

        using var response = await RegisterOwnerAsync(new { name = "Rui", email, password = Password, acceptedTermsVersion = "1.0", profile = requested, role = requested, roles = new[] { requested } });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("profile").GetString().ShouldBe(Profiles.Owner);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await users.GetRolesAsync((await FindUserAsync(email)).ShouldNotBeNull())).ShouldBe([Profiles.Owner]);
    }

    [Fact]
    public async Task No_registration_path_can_create_an_admin_even_when_asked_directly()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountStore>();
        var email = NewEmail("admin-direto");

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() =>
            accounts.CreateAsync(new NewAccount("Rui", email, Password, null, "1.0"), Profiles.Admin, Ct));

        (await AccountCountAsync(email)).ShouldBe(0);
    }

    [Theory]
    [InlineData(Profiles.Player, "same")]
    [InlineData(Profiles.Player, "upper")]
    [InlineData(Profiles.Owner, "mixed")]
    [InlineData(Profiles.Admin, "upper")]
    [InlineData(Profiles.Admin, "mixed")]
    public async Task Email_used_by_any_profile_in_any_casing_is_409_email_taken_and_creates_nothing(string existingProfile, string casing)
    {
        var email = NewEmail("existente");
        var existing = await factory.CreateAccountAsync(existingProfile, Password, email, name: "Titular Original");
        var again = casing switch
        {
            "upper" => email.ToUpperInvariant(),
            "mixed" => $"  {char.ToUpperInvariant(email[0])}{email[1..]}  ",
            _ => email,
        };
        using var browser = factory.CreateBrowserClient();

        using var response = await RegisterOwnerAsync(Registration(again, name: "Outra Pessoa", password: "outra-palavra"), browser);

        var problem = await ProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.EmailTaken);
        problem.GetProperty("detail").GetString().ShouldBe("Já existe uma conta com este email.");
        response.SetCookies().ShouldBeEmpty();
        (await AccountCountAsync(email)).ShouldBe(1);
        var stored = (await FindUserAsync(email)).ShouldNotBeNull();
        stored.Id.ShouldBe(existing.Id);
        stored.Name.ShouldBe("Titular Original");
        await using var scope = factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().GetRolesAsync(stored)).ShouldBe([existingProfile]);
    }

    [Fact]
    public async Task A_player_cannot_register_with_an_email_an_owner_already_registered_and_the_answer_is_the_same()
    {
        var email = NewEmail();
        (await RegisterOwnerAsync(Registration(email))).StatusCode.ShouldBe(HttpStatusCode.Created);
        using var client = factory.CreateSiteClient();

        using var asPlayer = await PostAsync(client, PlayerPath, Registration(email.ToUpperInvariant()));
        using var asOwner = await RegisterOwnerAsync(Registration(email));

        var playerProblem = await ProblemAsync(asPlayer, HttpStatusCode.Conflict, ErrorCodes.EmailTaken);
        var ownerProblem = await ProblemAsync(asOwner, HttpStatusCode.Conflict, ErrorCodes.EmailTaken);
        playerProblem.GetProperty("detail").GetString().ShouldBe(ownerProblem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Concurrent_owner_registrations_with_the_same_email_create_exactly_one_account()
    {
        var email = NewEmail("corrida");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            RegisterOwnerAsync(Registration(index % 2 == 0 ? email : email.ToUpperInvariant()))));

        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        (await AccountCountAsync(email)).ShouldBe(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Invalid_fields_are_400_validation_failed_with_a_pt_pt_message_per_field()
    {
        var email = $"invalido-{Guid.NewGuid():N}";

        using var response = await RegisterOwnerAsync(new { name = "  ", email, password = "curta", phone = "123" });

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        var errors = problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;
        errors.Keys.ShouldBe(["name", "email", "password", "phone"], ignoreOrder: true);
        errors["name"].ShouldBe(["Indica o teu nome."]);
        errors["email"].ShouldBe(["Indica um email válido, por exemplo nome@exemplo.pt."]);
        errors["password"].ShouldBe(["A palavra-passe tem de ter pelo menos 8 caracteres."]);
        errors["phone"].ShouldBe(["Indica um número de telemóvel válido, com 9 a 15 algarismos."]);
        response.SetCookies().ShouldBeEmpty();
        (await AccountCountAsync(email)).ShouldBe(0);
    }

    [Fact]
    public async Task Missing_fields_get_the_same_messages_as_empty_ones()
    {
        using var response = await RegisterOwnerAsync(new { });

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!.Keys.ShouldBe(["name", "email", "password"], ignoreOrder: true);
    }

    [Fact]
    public async Task Registration_from_another_site_is_rejected()
    {
        var email = NewEmail("csrf");
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(ApiFactory.SiteOrigin) });
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");

        using var response = await PostAsync(client, OwnerPath, Registration(email));

        await ProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.OriginNotAllowed);
        (await AccountCountAsync(email)).ShouldBe(0);
    }

    [Fact]
    public async Task Owner_and_player_registration_share_one_limit_per_client_address()
    {
        const int limit = 3;
        await using var app = factory.CreateIsolatedApp($"us02_rl_{Guid.NewGuid():N}", new Dictionary<string, string?>
        {
            ["RateLimiting:Auth:RegisterPermitLimit"] = $"{limit}",
        });
        _ = app.Server;
        using var client = app.CreateBrowserClient();
        var address = "203.0.113.50";

        // Two owners and one player use up the three registrations this address may make in the window.
        using var first = await PostAsync(client, OwnerPath, Registration(NewEmail()), address);
        using var second = await PostAsync(client, PlayerPath, Registration(NewEmail("jogador")), address);
        using var third = await PostAsync(client, OwnerPath, Registration(NewEmail()), address);
        using var ownerOverLimit = await PostAsync(client, OwnerPath, Registration(NewEmail()), address);
        using var playerOverLimit = await PostAsync(client, PlayerPath, Registration(NewEmail("jogador")), address);
        using var otherAddress = await PostAsync(client, OwnerPath, Registration(NewEmail()), "203.0.113.51");

        new[] { first, second, third }.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Created);
        var problem = await ProblemAsync(ownerOverLimit, HttpStatusCode.TooManyRequests, ErrorCodes.TooManyRequests);
        problem.GetProperty("status").GetInt32().ShouldBe(429);
        await ProblemAsync(playerOverLimit, HttpStatusCode.TooManyRequests, ErrorCodes.TooManyRequests);
        otherAddress.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
