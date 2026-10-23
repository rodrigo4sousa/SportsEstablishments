namespace SportsBooking.Infrastructure.Identity;

/// <summary>
/// US03: Identity's account lockout after repeated wrong passwords (provisional values, see US03 in docs/backlog.md).
/// A locked-out account gets the same generic login failure as a wrong password.
/// </summary>
public static class LoginLockout
{
    public const int MaxFailedAttempts = 5;

    public static readonly TimeSpan Duration = TimeSpan.FromMinutes(15);
}
