using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Auth;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Auth;

/// <summary>
/// Cookie sessions (docs/stack.md, Autenticação) and the Épico 1 rule: <c>POST /api/auth/refresh</c> always checks
/// that the account is still active before issuing new tokens.
/// </summary>
public sealed class SessionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<(AppUser User, SessionTokens Tokens)> SignedInPlayerAsync() => factory.CreateSignedInPlayerAsync();

    private async Task<HttpResponseMessage> RefreshAsync(string? refreshToken)
    {
        using var client = factory.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        if (refreshToken is not null)
        {
            request.WithCookies((SessionCookies.RefreshToken, refreshToken));
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<HttpResponseMessage> MeAsync(string? accessToken)
    {
        using var client = factory.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        if (accessToken is not null)
        {
            request.WithCookies((SessionCookies.AccessToken, accessToken));
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<List<RefreshToken>> RefreshTokensOfAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.AsNoTracking()
            .Where(token => token.UserId == userId)
            .OrderBy(token => token.CreatedAt)
            .ToListAsync(Ct);
    }

    private static async Task ShouldBeProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        problem.GetProperty("code").GetString().ShouldBe(code);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    private static void ShouldClearSessionCookies(HttpResponseMessage response)
    {
        var cookies = response.SetCookies();
        cookies[SessionCookies.AccessToken].Expires.ShouldNotBeNull().ShouldBeLessThan(DateTimeOffset.UtcNow);
        cookies[SessionCookies.RefreshToken].Expires.ShouldNotBeNull().ShouldBeLessThan(DateTimeOffset.UtcNow);
        cookies[SessionCookies.RefreshToken].Path.ToString().ShouldBe("/api/auth");
    }

    [Fact]
    public async Task Me_returns_the_signed_in_account()
    {
        var (user, tokens) = await SignedInPlayerAsync();

        using var response = await MeAsync(tokens.AccessToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        me.GetProperty("id").GetGuid().ShouldBe(user.Id);
        me.GetProperty("name").GetString().ShouldBe("Jogadora de Teste");
        me.GetProperty("email").GetString().ShouldBe(user.Email);
        me.GetProperty("profile").GetString().ShouldBe(Profiles.Player);
    }

    [Fact]
    public async Task Me_without_a_session_is_401_problem_details()
    {
        using var response = await MeAsync(accessToken: null);

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Access_token_signed_with_another_key_is_rejected()
    {
        var (user, _) = await SignedInPlayerAsync();
        var forged = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "sportsbooking-api",
            Audience = "sportsbooking-app",
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = user.Id.ToString(), ["role"] = Profiles.Admin },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes("another-key-that-is-long-enough-0123456789")),
                SecurityAlgorithms.HmacSha256),
        });

        using var response = await MeAsync(forged);

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthorized);
    }

    [Fact]
    public async Task Access_token_lasts_15_minutes_and_carries_only_the_account_id_and_profile()
    {
        var (user, tokens) = await SignedInPlayerAsync();

        var jwt = new JsonWebToken(tokens.AccessToken);

        jwt.Subject.ShouldBe(user.Id.ToString());
        jwt.GetClaim("role").Value.ShouldBe(Profiles.Player);
        (jwt.ValidTo - jwt.IssuedAt).ShouldBe(TimeSpan.FromMinutes(15));
        jwt.Claims.Select(claim => claim.Type).ShouldBe(["sub", "role", "jti", "nbf", "exp", "iat", "iss", "aud"], ignoreOrder: true);
        tokens.AccessToken.ShouldNotContain(user.Email!);
    }

    [Fact]
    public async Task Refresh_rotates_the_refresh_token_and_sets_both_cookies_with_the_documented_attributes()
    {
        var (user, tokens) = await SignedInPlayerAsync();

        using var response = await RefreshAsync(tokens.RefreshToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
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
        refresh.Value.ToString().ShouldNotBe(tokens.RefreshToken);

        using var me = await MeAsync(access.Value.ToString());
        me.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Stored only as hashes: the old one is used up, the new one is active.
        var stored = await RefreshTokensOfAsync(user.Id);
        stored.Count.ShouldBe(2);
        stored[0].RevokedAt.ShouldNotBeNull();
        stored[1].RevokedAt.ShouldBeNull();
        stored.ShouldAllBe(token => token.TokenHash.Length == 32);
    }

    [Fact]
    public async Task Session_lifetime_is_absolute_rotation_keeps_the_original_expiry()
    {
        var (user, tokens) = await SignedInPlayerAsync();
        tokens.RefreshTokenExpiresAt.ShouldBe(DateTimeOffset.UtcNow.AddDays(7), TimeSpan.FromMinutes(1));

        using var response = await RefreshAsync(tokens.RefreshToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var stored = await RefreshTokensOfAsync(user.Id);
        stored[1].ExpiresAt.ShouldBe(stored[0].ExpiresAt);
    }

    [Fact]
    public async Task Deactivated_account_gets_no_new_tokens_and_the_refresh_is_a_401()
    {
        var (user, tokens) = await SignedInPlayerAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString())).ShouldNotBeNull();
            stored.Deactivate(DateTime.UtcNow);
            (await users.UpdateAsync(stored)).Succeeded.ShouldBeTrue();
        }

        using var response = await RefreshAsync(tokens.RefreshToken);

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
        ShouldClearSessionCookies(response);
        (await RefreshTokensOfAsync(user.Id)).ShouldAllBe(token => token.RevokedAt != null);
    }

    [Fact]
    public async Task A_used_refresh_token_presented_again_ends_every_session_of_the_account()
    {
        var (user, tokens) = await SignedInPlayerAsync();
        using var first = await RefreshAsync(tokens.RefreshToken);
        var rotated = first.SetCookies()[SessionCookies.RefreshToken].Value.ToString();

        using var replay = await RefreshAsync(tokens.RefreshToken);
        using var afterReplay = await RefreshAsync(rotated);

        await ShouldBeProblemAsync(replay, HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
        await ShouldBeProblemAsync(afterReplay, HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
        (await RefreshTokensOfAsync(user.Id)).ShouldAllBe(token => token.RevokedAt != null);
    }

    [Fact]
    public async Task Concurrent_refreshes_with_the_same_token_succeed_at_most_once()
    {
        var (_, tokens) = await SignedInPlayerAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RefreshAsync(tokens.RefreshToken)));

        responses.Count(response => response.StatusCode == HttpStatusCode.NoContent).ShouldBe(1);
        responses.Count(response => response.StatusCode == HttpStatusCode.Unauthorized).ShouldBe(4);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Expired_refresh_token_is_refused()
    {
        var (user, _) = await SignedInPlayerAsync();
        var (expired, value) = RefreshToken.Issue(user.Id, DateTime.UtcNow.AddDays(-8), DateTime.UtcNow.AddSeconds(-1));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.RefreshTokens.Add(expired);
            await dbContext.SaveChangesAsync(Ct);
        }

        using var response = await RefreshAsync(value);

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-real-refresh-token")]
    public async Task Missing_or_unknown_refresh_token_is_401_problem_details_and_clears_the_cookies(string? refreshToken)
    {
        using var response = await RefreshAsync(refreshToken);

        await ShouldBeProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.InvalidRefreshToken);
        ShouldClearSessionCookies(response);
    }

    [Fact]
    public async Task Refresh_from_another_site_is_rejected_before_touching_the_session()
    {
        var (user, tokens) = await SignedInPlayerAsync();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(ApiFactory.SiteOrigin), HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh").WithCookies((SessionCookies.RefreshToken, tokens.RefreshToken));
        request.Headers.Add(HeaderNames.Origin, "https://evil.example");

        using var response = await client.SendAsync(request, Ct);

        await ShouldBeProblemAsync(response, HttpStatusCode.Forbidden, ErrorCodes.OriginNotAllowed);
        (await RefreshTokensOfAsync(user.Id)).ShouldHaveSingleItem().RevokedAt.ShouldBeNull();
    }
}
