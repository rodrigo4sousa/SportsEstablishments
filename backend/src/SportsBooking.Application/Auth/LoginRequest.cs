namespace SportsBooking.Application.Auth;

/// <summary>US03 login form: email and password.</summary>
public sealed record LoginRequest(string Email, string Password)
{
    /// <summary>Trims the email and treats missing values as empty. The password is kept exactly as typed.</summary>
    public LoginRequest Normalized() => new(Email?.Trim() ?? string.Empty, Password ?? string.Empty);
}
