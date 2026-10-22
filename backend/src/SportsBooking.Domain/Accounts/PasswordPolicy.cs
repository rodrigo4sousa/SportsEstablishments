namespace SportsBooking.Domain.Accounts;

/// <summary>Password rules from US01, shared by every account (including the seeded admin).</summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    public static bool IsSatisfiedBy(string? password) => password is not null && password.Length >= MinimumLength;
}
