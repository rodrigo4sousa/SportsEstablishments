using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using SportsBooking.Application.Auth;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Auth;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Auth;

/// <summary>
/// US03 logout: the refresh cookie reaches <c>/api/auth/logout</c> (Path=/api/auth), its token is revoked in the
/// database, and both cookies are cleared with the paths and attributes they were set with.
/// </summary>
public sealed class LogoutTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> PostAsync(string path, params (string Name, string Value)[] cookies)
    {
        using var client = factory.CreateSiteClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (cookies.Length > 0)
        {
            request.WithCookies(cookies);
        }

        return await client.SendAsync(request, Ct);
    }

    private async Task<List<RefreshToken>> RefreshTokensOfAsync(Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().RefreshTokens.AsNoTracking()
            .Where(token => token.UserId == userId)
            .ToListAsync(Ct);
    }

    /// <summary>An access token that is genuine (right key, issuer and audience) but expired.</summary>
    private static string ExpiredAccessToken(Guid userId) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = "sportsbooking-api",
        Audience = "sportsbooking-app",
        IssuedAt = DateTime.UtcNow.AddMinutes(-30),
        NotBefore = DateTime.UtcNow.AddMinutes(-30),
        Expires = DateTime.UtcNow.AddMinutes(-15),
        Claims = new Dictionary<string, object> { ["sub"] = userId.ToString(), ["role"] = Profiles.Player },
        SigningCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.SigningKey)),
            SecurityAlgorithms.HmacSha256),
    });

    private static void ShouldClearBothCookiesWithTheirAttributes(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cookies = response.SetCookies();

        var access = cookies[SessionCookies.AccessToken];
        access.Value.ToString().ShouldBeEmpty();
        access.Expires.ShouldNotBeNull().ShouldBeLessThan(DateTimeOffset.UtcNow);
        access.Path.ToString().ShouldBe("/");
        access.HttpOnly.ShouldBeTrue();
        access.Secure.ShouldBeTrue();
        access.SameSite.ShouldBe(SameSiteMode.Lax);

        var refresh = cookies[SessionCookies.RefreshToken];
        refresh.Value.ToString().ShouldBeEmpty();
        refresh.Expires.ShouldNotBeNull().ShouldBeLessThan(DateTimeOffset.UtcNow);
        refresh.Path.ToString().ShouldBe("/api/auth");
        refresh.HttpOnly.ShouldBeTrue();
        refresh.Secure.ShouldBeTrue();
        refresh.SameSite.ShouldBe(SameSiteMode.Strict);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_clears_both_cookies()
    {
        var (user, tokens) = await factory.CreateSignedInPlayerAsync();

        using var response = await PostAsync(
            "/api/auth/logout",
            (SessionCookies.AccessToken, tokens.AccessToken),
            (SessionCookies.RefreshToken, tokens.RefreshToken));

        ShouldClearBothCookiesWithTheirAttributes(response);
        (await RefreshTokensOfAsync(user.Id)).ShouldHaveSingleItem().RevokedAt.ShouldNotBeNull();
        using var refresh = await PostAsync("/api/auth/refresh", (SessionCookies.RefreshToken, tokens.RefreshToken));
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_works_with_an_expired_access_token()
    {
        var (user, tokens) = await factory.CreateSignedInPlayerAsync();

        using var response = await PostAsync(
            "/api/auth/logout",
            (SessionCookies.AccessToken, ExpiredAccessToken(user.Id)),
            (SessionCookies.RefreshToken, tokens.RefreshToken));

        ShouldClearBothCookiesWithTheirAttributes(response);
        (await RefreshTokensOfAsync(user.Id)).ShouldHaveSingleItem().RevokedAt.ShouldNotBeNull();
        using var refresh = await PostAsync("/api/auth/refresh", (SessionCookies.RefreshToken, tokens.RefreshToken));
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-real-refresh-token")]
    public async Task Logout_without_a_known_refresh_token_is_still_204_and_clears_the_cookies(string? refreshToken)
    {
        using var response = refreshToken is null
            ? await PostAsync("/api/auth/logout")
            : await PostAsync("/api/auth/logout", (SessionCookies.RefreshToken, refreshToken));

        ShouldClearBothCookiesWithTheirAttributes(response);
    }

    [Fact]
    public async Task Logout_only_ends_this_session()
    {
        var (user, first) = await factory.CreateSignedInPlayerAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISessionService>().StartAsync(user.Id, Profiles.Player, Ct);
        }

        using var response = await PostAsync("/api/auth/logout", (SessionCookies.RefreshToken, first.RefreshToken));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await RefreshTokensOfAsync(user.Id)).Count(token => token.RevokedAt is null).ShouldBe(1);
    }

    [Fact]
    public async Task Logout_with_an_already_used_refresh_token_ends_every_session_of_the_account()
    {
        var (user, tokens) = await factory.CreateSignedInPlayerAsync();
        using (var rotated = await PostAsync("/api/auth/refresh", (SessionCookies.RefreshToken, tokens.RefreshToken)))
        {
            rotated.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var response = await PostAsync("/api/auth/logout", (SessionCookies.RefreshToken, tokens.RefreshToken));

        ShouldClearBothCookiesWithTheirAttributes(response);
        (await RefreshTokensOfAsync(user.Id)).ShouldAllBe(token => token.RevokedAt != null);
    }

    [Fact]
    public async Task After_logout_the_browser_has_no_session()
    {
        var user = await factory.CreateAccountAsync(Profiles.Player, "palavra-passe");
        using var browser = factory.CreateBrowserClient();
        using (var login = await browser.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = "palavra-passe" }, Ct))
        {
            login.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using var logout = await browser.PostAsync("/api/auth/logout", content: null, Ct);

        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var me = await browser.GetAsync("/api/auth/me", Ct);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var refresh = await browser.PostAsync("/api/auth/refresh", content: null, Ct);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_from_another_site_is_rejected_and_keeps_the_session()
    {
        var (user, tokens) = await factory.CreateSignedInPlayerAsync();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri(ApiFactory.SiteOrigin), HandleCookies = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
            .WithCookies((SessionCookies.RefreshToken, tokens.RefreshToken));
        request.Headers.Add(HeaderNames.Origin, "https://outro-site.example");

        using var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await RefreshTokensOfAsync(user.Id)).ShouldHaveSingleItem().RevokedAt.ShouldBeNull();
    }
}
