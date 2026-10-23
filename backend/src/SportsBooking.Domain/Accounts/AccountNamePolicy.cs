namespace SportsBooking.Domain.Accounts;

/// <summary>Name shown for an account (US01). Required; surrounding whitespace is not part of it.</summary>
public static class AccountNamePolicy
{
    /// <summary>Length of the <c>users.name</c> column.</summary>
    public const int MaximumLength = 100;
}
