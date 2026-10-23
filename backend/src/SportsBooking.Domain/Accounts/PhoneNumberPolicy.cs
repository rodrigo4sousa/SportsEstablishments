using System.Text.RegularExpressions;

namespace SportsBooking.Domain.Accounts;

/// <summary>
/// Optional mobile number (US01). Stored without spaces: an optional leading "+" and 9 to 15 digits,
/// so Portuguese numbers ("912 345 678") and international ones ("+351 912 345 678") both fit.
/// </summary>
public static partial class PhoneNumberPolicy
{
    public const int MinimumDigits = 9;
    public const int MaximumDigits = 15;

    /// <summary>Removes spaces; a blank number means "not given" and becomes null.</summary>
    public static string? Normalize(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : phone.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();

    /// <summary>Checks a number already passed through <see cref="Normalize"/>.</summary>
    public static bool IsValid(string? normalizedPhone) =>
        normalizedPhone is not null && Digits().IsMatch(normalizedPhone);

    [GeneratedRegex(@"^\+?[0-9]{9,15}$")]
    private static partial Regex Digits();
}
