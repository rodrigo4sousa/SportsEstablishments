namespace SportsBooking.Domain.Accounts;

/// <summary>The languages an account's emails can be written in (T09).</summary>
public enum Language
{
    /// <summary>pt-PT: the default, also for an account with no preference (every account made before T09).</summary>
    Portuguese,

    English,
}

/// <summary>
/// T09: the language an account prefers for its emails, stored as a code in <c>users.preferred_language</c> (null = no preference =
/// pt-PT). The codes are the site's: <c>pt-PT</c> and <c>en</c>.
/// </summary>
public static class PreferredLanguage
{
    public const string PortugueseCode = "pt-PT";

    public const string EnglishCode = "en";

    /// <summary>The longest code, for the column.</summary>
    public const int MaximumLength = 5;

    public static readonly IReadOnlyList<string> Codes = [PortugueseCode, EnglishCode];

    /// <summary>Exactly one of <see cref="Codes"/> (case matters, as in the site's cookie).</summary>
    public static bool IsSupported(string? code) => code is PortugueseCode or EnglishCode;

    /// <summary>The language to write in: English only for <c>en</c>; anything else, null included, is Portuguese.</summary>
    public static Language ForEmails(string? code) => code == EnglishCode ? Language.English : Language.Portuguese;
}
