using SportsBooking.Application.Common;

namespace SportsBooking.Application.Auth;

/// <summary>
/// Cookie session tokens (docs/stack.md, Autenticação): a short-lived JWT access token and an opaque,
/// rotating refresh token that is stored only as a hash.
/// </summary>
public interface ISessionService
{
    /// <summary>Starts a session for an account that was just authenticated or created.</summary>
    Task<SessionTokens> StartAsync(Guid userId, string profile, CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a refresh token for a new pair (rotation). Fails with <see cref="AuthErrors.InvalidRefreshToken"/>
    /// when the token is unknown, expired or already used, or when the account is no longer active (Épico 1).
    /// A token that is presented again after use revokes every session of that account.
    /// </summary>
    Task<Result<SessionTokens>> RefreshAsync(string? refreshToken, CancellationToken cancellationToken);
}

public sealed record SessionTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public static class AuthErrors
{
    public static ServiceError InvalidRefreshToken { get; } = new(
        ErrorKind.Unauthorized,
        ErrorCodes.InvalidRefreshToken,
        "Sessão terminada",
        "A tua sessão terminou. Inicia sessão novamente.");

    /// <summary>
    /// US03: the one answer for every failed login (unknown email, wrong password, locked or deactivated account),
    /// so it never reveals whether an email has an account.
    /// </summary>
    public static ServiceError InvalidCredentials { get; } = new(
        ErrorKind.Unauthorized,
        ErrorCodes.InvalidCredentials,
        "Não foi possível entrar",
        "Email ou palavra-passe incorretos.");
}
