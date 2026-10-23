using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Common;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Auth;

internal sealed partial class SessionService(
    AppDbContext dbContext,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider,
    ILogger<SessionService> logger) : ISessionService
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    public async Task<SessionTokens> StartAsync(Guid userId, string profile, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var (refreshToken, refreshValue) = RefreshToken.Issue(userId, now.UtcDateTime, (now + SessionLifetimes.RefreshToken).UtcDateTime);
        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Tokens(userId, profile, now, refreshValue, refreshToken.ExpiresAt);
    }

    public async Task<Result<SessionTokens>> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var hash = RefreshToken.Hash(refreshToken);
        var presented = await dbContext.RefreshTokens.AsNoTracking()
            .SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);
        if (presented is null)
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var now = timeProvider.GetUtcNow();
        if (presented.RevokedAt is not null)
        {
            // A used token coming back means it may have been stolen: end every session of the account.
            LogReusedToken(logger, presented.UserId);
            await RevokeAllAsync(presented.UserId, now, cancellationToken);
            return AuthErrors.InvalidRefreshToken;
        }

        if (presented.IsExpired(now.UtcDateTime))
        {
            return AuthErrors.InvalidRefreshToken;
        }

        var account = await dbContext.Users
            .Where(user => user.Id == presented.UserId)
            .Select(user => new
            {
                user.DeactivatedAt,
                Profile = dbContext.UserRoles
                    .Where(userRole => userRole.UserId == user.Id)
                    .Join(dbContext.Roles, userRole => userRole.RoleId, role => role.Id, (_, role) => role.Name)
                    .FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        // Épico 1: a deactivated account never gets new tokens.
        if (account is null || account.DeactivatedAt is not null || account.Profile is null)
        {
            await RevokeAllAsync(presented.UserId, now, cancellationToken);
            return AuthErrors.InvalidRefreshToken;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Only one request can use a token, even when two arrive at the same time.
        var consumed = await dbContext.RefreshTokens
            .Where(token => token.Id == presented.Id && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now.UtcDateTime), cancellationToken);
        if (consumed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            LogReusedToken(logger, presented.UserId);
            await RevokeAllAsync(presented.UserId, now, cancellationToken);
            return AuthErrors.InvalidRefreshToken;
        }

        var (next, nextValue) = RefreshToken.Issue(presented.UserId, now.UtcDateTime, presented.ExpiresAt);
        dbContext.RefreshTokens.Add(next);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Tokens(presented.UserId, account.Profile, now, nextValue, next.ExpiresAt);
    }

    private async Task RevokeAllAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now.UtcDateTime), cancellationToken);

    private SessionTokens Tokens(Guid userId, string profile, DateTimeOffset now, string refreshValue, DateTime refreshExpiresAt)
    {
        var options = jwtOptions.Value;
        var accessExpiresAt = now + SessionLifetimes.AccessToken;
        var accessToken = TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = accessExpiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [SessionClaims.Subject] = userId.ToString(),
                [SessionClaims.Role] = profile,
                [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),
            },
            SigningCredentials = new SigningCredentials(options.SecurityKey(), SecurityAlgorithms.HmacSha256),
        });

        return new SessionTokens(
            accessToken,
            accessExpiresAt,
            refreshValue,
            new DateTimeOffset(DateTime.SpecifyKind(refreshExpiresAt, DateTimeKind.Utc)));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token reused for account {UserId}; all its sessions were revoked.")]
    private static partial void LogReusedToken(ILogger logger, Guid userId);
}
