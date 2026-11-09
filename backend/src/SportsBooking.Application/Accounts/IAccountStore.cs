namespace SportsBooking.Application.Accounts;

/// <summary>Account persistence, implemented on ASP.NET Core Identity in Infrastructure.</summary>
public interface IAccountStore
{
    /// <summary>
    /// Creates an account with a hashed password and the given profile, which must be one of
    /// <see cref="Domain.Accounts.Profiles.SelfRegistration"/> (any other is a programming error, never a request). Returns null
    /// when the email is already used by any account (compared case-insensitively), also when another request
    /// creates it at the same moment.
    /// </summary>
    Task<AccountResponse?> CreateAsync(NewAccount account, string profile, CancellationToken cancellationToken);

    /// <summary>
    /// US03: the account with this email (compared case-insensitively) and password. Returns null, after the same
    /// password-hashing work, when the email is unknown, the password is wrong, or the account is locked out or
    /// deactivated. Wrong passwords count towards the lockout.
    /// </summary>
    Task<AccountResponse?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken);

    Task<AccountResponse?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>True when the account exists and is not deactivated (<c>users.deactivated_at</c> empty).</summary>
    Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>A validated, normalized registration.</summary>
public sealed record NewAccount(string Name, string Email, string Password, string? Phone, string AcceptedTermsVersion);

/// <summary>The signed-in account, as returned by <c>GET /api/auth/me</c>. <see cref="Profile"/> is Player, Owner or Admin.</summary>
public sealed record AccountResponse(Guid Id, string Name, string Email, string Profile);
