namespace SportsBooking.Domain.Pitches;

/// <summary>US07 field limits for a pitch (provisional, see US07 in docs/backlog.md). They are also the column limits.</summary>
public static class PitchRules
{
    public const int NameMaximumLength = 100;

    /// <summary>The price of one slot in euros, with at most two decimals (<c>numeric(10,2)</c>).</summary>
    public const int PriceDecimals = 2;

    public const decimal MaximumPrice = 500m;

    public static bool IsPositive(decimal price) => price > 0;

    public static bool IsWithinMaximum(decimal price) => price <= MaximumPrice;

    /// <summary>At most two decimals: 1.5 and 1.50 are fine, 1.505 is not.</summary>
    public static bool HasAllowedDecimals(decimal price) => decimal.Round(price, PriceDecimals) == price;

    public static bool IsValidPrice(decimal price) => IsPositive(price) && IsWithinMaximum(price) && HasAllowedDecimals(price);

    /// <summary>
    /// What makes two names "the same" inside a club: the trimmed name in upper case (invariant culture), so "Campo 1"
    /// and "CAMPO 1" clash. Stored in its own column with the unique index.
    /// </summary>
    public static string NameKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Trim().ToUpperInvariant();
    }
}
