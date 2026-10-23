using System.Net.Mail;

namespace SportsBooking.Domain.Accounts;

/// <summary>Email rule shared by every account (US01, and the seeded admin of US04).</summary>
public static class EmailAddressPolicy
{
    /// <summary>Length of the <c>users.email</c> column.</summary>
    public const int MaximumLength = 256;

    /// <summary>
    /// A bare address only: no display name, no surrounding whitespace and a dotted domain.
    /// Callers trim surrounding whitespace before checking.
    /// </summary>
    public static bool IsValid(string? email) =>
        email is { Length: > 0 and <= MaximumLength }
        && MailAddress.TryCreate(email, out var address)
        && string.Equals(address.Address, email, StringComparison.Ordinal)
        && address.Host.Contains('.', StringComparison.Ordinal);
}
