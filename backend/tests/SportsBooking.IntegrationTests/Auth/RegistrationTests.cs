using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Auth;

/// <summary>US01: player registration through <c>POST /api/auth/register</c>, against real PostgreSQL.</summary>
public sealed class RegistrationTests(ApiFactory factory)
{
    private const string RegisterPath = "/api/auth/register";
    private const string Password = "palavra-passe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string NewEmail(string prefix = "jogador") => $"{prefix}-{Guid.NewGuid():N}@exemplo.pt";

    private static object Registration(string email, string name = "Ana Silva", string password = Password, string? phone = "912 345 678") =>
        new { name, email, password, phone };

    private async Task<HttpResponseMessage> RegisterAsync(object body, HttpClient? client = null)
    {
        using var ownClient = client is null ? factory.CreateSiteClient() : null;
        return await (client ?? ownClient!).PostAsJsonAsync(RegisterPath, body, Ct);
    }

    private async Task<AppUser?> FindUserAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email);
    }

    private async Task<int> AccountCountAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var normalized = email.ToUpperInvariant();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync(user => user.NormalizedEmail == normalized, Ct);
    }

    private static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("status").GetInt32().ShouldBe((int)status);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        return problem;
    }

    private static Dictionary<string, string[]> FieldErrors(JsonElement problem) =>
        problem.GetProperty("errors").Deserialize<Dictionary<string, string[]>>()!;

    [Fact]
    public async Task Registration_creates_a_player_and_signs_them_in()
    {
        var email = NewEmail();
        using var browser = factory.CreateBrowserClient();

        using var response = await RegisterAsync(Registration(email), browser);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        // Relative: behind the proxy the API's own host name is internal (e.g. http://api:8080) and must not leak.
        response.Headers.Location.ShouldNotBeNull().IsAbsoluteUri.ShouldBeFalse();
        response.Headers.Location.OriginalString.ShouldBe("/api/auth/me");
        var account = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        account.GetProperty("name").GetString().ShouldBe("Ana Silva");
        account.GetProperty("email").GetString().ShouldBe(email);
        account.GetProperty("profile").GetString().ShouldBe(Profiles.Player);
        account.GetProperty("id").GetGuid().Version.ShouldBe(7);

        var cookies = response.SetCookies();
        cookies[SessionCookies.AccessToken].HttpOnly.ShouldBeTrue();
        cookies[SessionCookies.AccessToken].Secure.ShouldBeTrue();
        cookies[SessionCookies.RefreshToken].HttpOnly.ShouldBeTrue();
        cookies[SessionCookies.RefreshToken].Path.ToString().ShouldBe("/api/auth");

        // The browser now has a session: /me answers from the cookie alone, and the session can be renewed.
        var me = await browser.GetFromJsonAsync<JsonElement>("/api/auth/me", Ct);
        me.GetProperty("id").GetGuid().ShouldBe(account.GetProperty("id").GetGuid());
        me.GetProperty("profile").GetString().ShouldBe(Profiles.Player);
        using var refresh = await browser.PostAsync("/api/auth/refresh", content: null, Ct);
        refresh.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_account_has_only_the_player_profile_and_a_hashed_password()
    {
        var email = NewEmail();

        using var response = await RegisterAsync(Registration(email));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var user = (await FindUserAsync(email)).ShouldNotBeNull();
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await users.GetRolesAsync(user)).ShouldBe([Profiles.Player]);
        user.PasswordHash.ShouldNotBeNullOrWhiteSpace();
        user.PasswordHash.ShouldNotContain(Password);
        (await users.CheckPasswordAsync(user, Password)).ShouldBeTrue();
        user.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task The_profile_cannot_be_chosen_by_the_client()
    {
        var email = NewEmail("quer-ser-admin");

        using var response = await RegisterAsync(new { name = "Rui", email, password = Password, profile = Profiles.Admin, role = Profiles.Admin });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("profile").GetString().ShouldBe(Profiles.Player);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        (await users.GetRolesAsync((await FindUserAsync(email)).ShouldNotBeNull())).ShouldBe([Profiles.Player]);
    }

    [Fact]
    public async Task Name_email_and_phone_are_stored_normalized_and_the_phone_is_optional()
    {
        var withPhone = NewEmail("com-telemovel");
        var withoutPhone = NewEmail("sem-telemovel");

        using var first = await RegisterAsync(Registration($"  {withPhone} ", name: "  Ana Silva  ", phone: "+351 912 345 678"));
        using var second = await RegisterAsync(new { name = "Rui", email = withoutPhone, password = Password });

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        var stored = (await FindUserAsync(withPhone)).ShouldNotBeNull();
        stored.Email.ShouldBe(withPhone);
        stored.Name.ShouldBe("Ana Silva");
        stored.PhoneNumber.ShouldBe("+351912345678");
        (await FindUserAsync(withoutPhone)).ShouldNotBeNull().PhoneNumber.ShouldBeNull();
    }

    [Fact]
    public async Task Valid_addresses_outside_identitys_default_user_name_characters_are_accepted()
    {
        var email = $"o'neill+reservas-{Guid.NewGuid():N}@exemplo.pt";

        using var response = await RegisterAsync(Registration(email));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("upper")]
    [InlineData("mixed")]
    public async Task Email_already_used_with_any_casing_is_409_email_taken_and_creates_nothing(string casing)
    {
        var email = NewEmail();
        (await RegisterAsync(Registration(email))).StatusCode.ShouldBe(HttpStatusCode.Created);
        var again = casing switch
        {
            "upper" => email.ToUpperInvariant(),
            "mixed" => $"  {char.ToUpperInvariant(email[0])}{email[1..]}  ",
            _ => email,
        };
        using var browser = factory.CreateBrowserClient();

        using var response = await RegisterAsync(Registration(again, name: "Outra Pessoa", password: "outra-palavra"), browser);

        var problem = await ProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.EmailTaken);
        problem.GetProperty("detail").GetString().ShouldBe("Já existe uma conta com este email.");
        response.SetCookies().ShouldBeEmpty();
        (await AccountCountAsync(email)).ShouldBe(1);
        (await FindUserAsync(email)).ShouldNotBeNull().Name.ShouldBe("Ana Silva");
    }

    [Fact]
    public async Task Taken_email_response_is_the_same_whatever_profile_owns_the_email()
    {
        var playerEmail = NewEmail("jogador");
        var ownerEmail = NewEmail("dono");
        (await RegisterAsync(Registration(playerEmail))).StatusCode.ShouldBe(HttpStatusCode.Created);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var owner = new AppUser(ownerEmail, "Dono do Clube", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            (await users.CreateAsync(owner)).Succeeded.ShouldBeTrue();
            (await users.AddToRoleAsync(owner, Profiles.Owner)).Succeeded.ShouldBeTrue();
        }

        using var forPlayer = await RegisterAsync(Registration(playerEmail));
        using var forOwner = await RegisterAsync(Registration(ownerEmail));

        var playerProblem = await ProblemAsync(forPlayer, HttpStatusCode.Conflict, ErrorCodes.EmailTaken);
        var ownerProblem = await ProblemAsync(forOwner, HttpStatusCode.Conflict, ErrorCodes.EmailTaken);
        string WithoutTraceId(JsonElement problem) =>
            JsonSerializer.Serialize(problem.EnumerateObject().Where(property => property.Name != "traceId").ToDictionary(p => p.Name, p => p.Value));
        WithoutTraceId(ownerProblem).ShouldBe(WithoutTraceId(playerProblem));
        ownerProblem.GetRawText().ShouldNotContain("Owner");
        ownerProblem.GetRawText().ShouldNotContain("dono", Case.Insensitive);
    }

    [Fact]
    public async Task Concurrent_registrations_with_the_same_email_create_exactly_one_account()
    {
        var email = NewEmail("corrida");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(index =>
            RegisterAsync(Registration(index % 2 == 0 ? email : email.ToUpperInvariant()))));

        responses.Count(response => response.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        foreach (var response in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
        {
            await ProblemAsync(response, HttpStatusCode.Conflict, ErrorCodes.EmailTaken);
        }

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

        using var response = await RegisterAsync(new { name = "  ", email, password = "curta", phone = "123" });

        var problem = await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed);
        var errors = FieldErrors(problem);
        errors.Keys.ShouldBe(["name", "email", "password", "phone"], ignoreOrder: true);
        errors["name"].ShouldBe(["Indica o teu nome."]);
        errors["email"].ShouldBe(["Indica um email válido, por exemplo nome@exemplo.pt."]);
        errors["password"].ShouldBe(["A palavra-passe tem de ter pelo menos 8 caracteres."]);
        errors["phone"].ShouldBe(["Indica um número de telemóvel válido, com 9 a 15 algarismos."]);
        response.SetCookies().ShouldBeEmpty();
        (await AccountCountAsync(email)).ShouldBe(0);
    }

    [Fact]
    public async Task Missing_fields_get_the_same_pt_pt_messages_as_empty_ones()
    {
        using var response = await RegisterAsync(new { });

        var errors = FieldErrors(await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed));
        errors.Keys.ShouldBe(["name", "email", "password"], ignoreOrder: true);
        errors["email"].ShouldBe(["Indica o teu email."]);
    }

    [Fact]
    public async Task Malformed_json_is_400_problem_details_with_code_and_trace_id()
    {
        using var client = factory.CreateSiteClient();
        using var content = new StringContent("{ \"name\": ", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(RegisterPath, content, Ct);

        await ProblemAsync(response, HttpStatusCode.BadRequest, ErrorCodes.BadRequest);
    }

    [Fact]
    public async Task Registration_from_another_site_is_rejected()
    {
        var email = NewEmail("csrf");
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(ApiFactory.SiteOrigin) });
        using var request = new HttpRequestMessage(HttpMethod.Post, RegisterPath) { Content = JsonContent.Create(Registration(email)) };
        request.Headers.Add(HeaderNames.Origin, "https://evil.example");

        using var response = await client.SendAsync(request, Ct);

        await ProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.OriginNotAllowed);
        (await AccountCountAsync(email)).ShouldBe(0);
    }
}
