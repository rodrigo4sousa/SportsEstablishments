using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Accounts;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Identity;

internal sealed class AccountPrivacyStore(
    UserManager<AppUser> userManager,
    AppDbContext dbContext,
    FallbackPasswordHash fallbackHash) : IAccountPrivacyStore
{
    public async Task<AccountPersonalData?> ReadPersonalDataAsync(Guid accountId, CancellationToken cancellationToken) =>
        await dbContext.Users.AsNoTracking()
            .Where(user => user.Id == accountId)
            .Select(user => new AccountPersonalData(
                user.Name,
                user.Email!,
                user.PhoneNumber,
                dbContext.UserRoles
                    .Where(userRole => userRole.UserId == user.Id)
                    .Join(dbContext.Roles, userRole => userRole.RoleId, role => role.Id, (_, role) => role.Name!)
                    .First(),
                user.RegisteredAt,
                user.AcceptedTermsVersion,
                user.TermsAcceptedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<bool> VerifyPasswordAsync(Guid accountId, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(accountId.ToString());
        if (user?.PasswordHash is null || !user.IsActive || await userManager.IsLockedOutAsync(user))
        {
            fallbackHash.Verify(password);
            return false;
        }

        if (!await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            return false;
        }

        await userManager.ResetAccessFailedCountAsync(user);
        return true;
    }

    public async Task<AccountDeletionOutcome> DeleteAsync(Guid accountId, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // FOR UPDATE conflicts with the key-share lock of a foreign key: a booking or club being inserted for this account commits
        // first, so the statements below see it.
        await dbContext.Database.ExecuteSqlAsync($"SELECT 1 FROM users WHERE id = {accountId} FOR UPDATE", cancellationToken);

        if (await dbContext.Clubs.AnyAsync(club => club.OwnerId == accountId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return AccountDeletionOutcome.OwnerHasClubs;
        }

        var tombstone = DeletedAccount.TombstoneEmail(accountId);
        var normalizedTombstone = tombstone.ToUpperInvariant();
        var securityStamp = Guid.NewGuid().ToString("N");
        var anonymized = await dbContext.Users
            .Where(user => user.Id == accountId && user.DeletedAt == null)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.Name, DeletedAccount.PlaceholderName)
                    .SetProperty(user => user.Email, tombstone)
                    .SetProperty(user => user.NormalizedEmail, normalizedTombstone)
                    .SetProperty(user => user.UserName, tombstone)
                    .SetProperty(user => user.NormalizedUserName, normalizedTombstone)
                    .SetProperty(user => user.EmailConfirmed, false)
                    .SetProperty(user => user.PasswordHash, (string?)null)
                    .SetProperty(user => user.SecurityStamp, securityStamp)
                    .SetProperty(user => user.ConcurrencyStamp, securityStamp)
                    .SetProperty(user => user.PhoneNumber, (string?)null)
                    .SetProperty(user => user.PhoneNumberConfirmed, false)
                    .SetProperty(user => user.TwoFactorEnabled, false)
                    .SetProperty(user => user.LockoutEnd, (DateTimeOffset?)null)
                    .SetProperty(user => user.AccessFailedCount, 0)
                    .SetProperty(user => user.AcceptedTermsVersion, (string?)null)
                    .SetProperty(user => user.TermsAcceptedAt, (DateTime?)null)
                    // Every existing check of a deactivated account applies too: login, refresh, password reset, the public pages.
                    .SetProperty(user => user.DeactivatedAt, user => user.DeactivatedAt ?? now)
                    .SetProperty(user => user.DeletedAt, (DateTime?)now),
                cancellationToken);
        if (anonymized == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AccountDeletionOutcome.NotFound;
        }

        // The conditional update decides, as for a status change of an owner: a booking the club cancelled a moment ago stays as it is, and
        // the exclusion constraint only covers confirmed bookings, so the slots are free from the commit. A booking that has started stays.
        await dbContext.Bookings
            .Where(booking => booking.PlayerId == accountId && booking.Status == BookingStatus.Confirmed && booking.StartsAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(booking => booking.Status, BookingStatus.CancelledByPlayer)
                    .SetProperty(booking => booking.CancelledAt, (DateTime?)now),
                cancellationToken);

        await dbContext.RefreshTokens.Where(token => token.UserId == accountId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.UserTokens.Where(token => token.UserId == accountId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.UserLogins.Where(login => login.UserId == accountId).ExecuteDeleteAsync(cancellationToken);
        await dbContext.UserClaims.Where(claim => claim.UserId == accountId).ExecuteDeleteAsync(cancellationToken);

        // Emails not sent yet: to this person (payload with the player or account id) or about their bookings (payload with the booking id,
        // e.g. the reminder or the owner's notice that names the player). A worker holding one of these rows finishes first.
        var id = accountId.ToString();
        await dbContext.Database.ExecuteSqlAsync(
            $"""
             DELETE FROM outbox_messages
             WHERE sent_at IS NULL AND failed_at IS NULL AND skipped_at IS NULL
               AND (payload ->> 'playerId' = {id}
                    OR payload ->> 'accountId' = {id}
                    OR payload ->> 'bookingId' IN (SELECT id::text FROM bookings WHERE player_id = {accountId}))
             """,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return AccountDeletionOutcome.Deleted;
    }
}
