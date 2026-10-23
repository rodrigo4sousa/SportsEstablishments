using SportsBooking.Domain.Accounts;

namespace SportsBooking.Application.Accounts;

/// <summary>US01 registration form: name, email, password and an optional mobile number.</summary>
public sealed record RegisterPlayerRequest(string Name, string Email, string Password, string? Phone)
{
    /// <summary>
    /// Trims the name and email, removes spaces from the phone (blank means none) and treats missing values as empty.
    /// The password is kept exactly as typed.
    /// </summary>
    public RegisterPlayerRequest Normalized() => new(
        Name?.Trim() ?? string.Empty,
        Email?.Trim() ?? string.Empty,
        Password ?? string.Empty,
        PhoneNumberPolicy.Normalize(Phone));
}
