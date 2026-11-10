namespace SportsBooking.Domain.Accounts;

/// <summary>
/// US30: what remains of an account that was deleted. The row stays (past bookings and the owners' history point to it) and holds
/// nothing about the person: a fixed name and an address that cannot receive email and frees the original one for a new registration.
/// </summary>
public static class DeletedAccount
{
    /// <summary>Shown wherever the name would be, e.g. in the calendar of a club, on a past booking.</summary>
    public const string PlaceholderName = "Conta apagada";

    /// <summary><c>.invalid</c> is reserved (RFC 2606): no mail can ever be delivered there.</summary>
    public const string TombstoneDomain = "deleted.invalid";

    /// <summary>Unique, because the account id is, and says nothing about the person.</summary>
    public static string TombstoneEmail(Guid accountId) => $"deleted-{accountId:N}@{TombstoneDomain}";

    public static bool IsTombstone(string email) => email.EndsWith($"@{TombstoneDomain}", StringComparison.OrdinalIgnoreCase);
}
