namespace SportsBooking.Infrastructure.Auth;

/// <summary>Claims in the access token: the account id and its profile, never personal data.</summary>
public static class SessionClaims
{
    public const string Subject = "sub";
    public const string Role = "role";
}

/// <summary>Session lifetimes from docs/stack.md.</summary>
public static class SessionLifetimes
{
    public static readonly TimeSpan AccessToken = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Absolute: rotation hands out a new token with the same expiry, so a session lasts at most 7 days
    /// after sign-in or registration (provisional, see US01 in docs/backlog.md).
    /// </summary>
    public static readonly TimeSpan RefreshToken = TimeSpan.FromDays(7);
}
