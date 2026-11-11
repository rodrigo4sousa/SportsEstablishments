using SportsBooking.Domain.Accounts;

namespace SportsBooking.Application.Accounts;

/// <summary>
/// Registration form of US01 (player) and US02 (owner): name, email, password, the version of the terms the person accepted (T07) and
/// an optional mobile number, and (T09) the optional language of the account's emails, which the site sends from its own language.
/// </summary>
public sealed record RegisterAccountRequest(
    string Name,
    string Email,
    string Password,
    string AcceptedTermsVersion,
    string? Phone = null,
    string? PreferredLanguage = null)
{
    /// <summary>
    /// Trims the name and email, removes spaces from the phone (blank means none) and treats missing values as empty.
    /// The password is kept exactly as typed. A blank language means none (an older site does not send it).
    /// </summary>
    public RegisterAccountRequest Normalized() => new(
        Name?.Trim() ?? string.Empty,
        Email?.Trim() ?? string.Empty,
        Password ?? string.Empty,
        AcceptedTermsVersion?.Trim() ?? string.Empty,
        PhoneNumberPolicy.Normalize(Phone),
        string.IsNullOrWhiteSpace(PreferredLanguage) ? null : PreferredLanguage.Trim());
}
