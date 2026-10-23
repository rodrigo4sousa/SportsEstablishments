using SportsBooking.Application.Auth;

namespace SportsBooking.Api.Auth;

/// <summary>
/// The two session cookies from docs/stack.md. Both are httpOnly, so browser JavaScript never sees a token,
/// and Secure (browsers accept Secure cookies on http://localhost during development).
/// </summary>
internal static class AuthCookies
{
    public const string AccessToken = "access_token";
    public const string RefreshToken = "refresh_token";

    /// <summary>The refresh token only travels to the refresh endpoint.</summary>
    public const string RefreshTokenPath = "/api/auth/refresh";

    public static void Append(HttpResponse response, SessionTokens tokens)
    {
        response.Cookies.Append(AccessToken, tokens.AccessToken, AccessTokenOptions(tokens.AccessTokenExpiresAt));
        response.Cookies.Append(RefreshToken, tokens.RefreshToken, RefreshTokenOptions(tokens.RefreshTokenExpiresAt));
    }

    public static void Delete(HttpResponse response)
    {
        response.Cookies.Delete(AccessToken, AccessTokenOptions(expires: null));
        response.Cookies.Delete(RefreshToken, RefreshTokenOptions(expires: null));
    }

    private static CookieOptions AccessTokenOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Expires = expires,
        IsEssential = true,
    };

    private static CookieOptions RefreshTokenOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = RefreshTokenPath,
        Expires = expires,
        IsEssential = true,
    };
}
