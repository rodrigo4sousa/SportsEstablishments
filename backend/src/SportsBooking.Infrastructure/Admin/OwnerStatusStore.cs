using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Admin;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Admin;

internal sealed class OwnerStatusStore(AppDbContext dbContext) : IOwnerStatusStore
{
    public async Task<bool> TryChangeAsync(Guid ownerId, OwnerStatusAction action, string reason, Guid adminId, DateTime now, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The conditional update is the decision: of two requests at the same moment only one finds the account in the old state.
        var changed = action == OwnerStatusAction.Deactivated
            ? await dbContext.Users
                .Where(user => user.Id == ownerId && user.DeactivatedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.DeactivatedAt, (DateTime?)now), cancellationToken)
            : await dbContext.Users
                .Where(user => user.Id == ownerId && user.DeactivatedAt != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.DeactivatedAt, (DateTime?)null), cancellationToken);
        if (changed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        dbContext.Add(OwnerStatusChange.Record(ownerId, action, reason, adminId, now));
        await dbContext.SaveChangesAsync(cancellationToken);

        if (action == OwnerStatusAction.Deactivated)
        {
            // No new access token can come from a refresh; the access token already issued lasts until it expires (accepted, US29).
            await dbContext.RefreshTokens
                .Where(token => token.UserId == ownerId && token.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, (DateTime?)now), cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<OwnerStatusChangeResponse>> HistoryAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var rows = await (
                from change in dbContext.Set<OwnerStatusChange>().AsNoTracking()
                where change.OwnerId == ownerId
                join admin in dbContext.Users on change.AdminId equals admin.Id
                orderby change.CreatedAt descending, change.Id descending
                select new { change.Id, change.Action, change.Reason, change.AdminId, AdminName = admin.Name, change.CreatedAt })
            .ToListAsync(cancellationToken);
        return rows
            .Select(row => new OwnerStatusChangeResponse(
                row.Id,
                row.Action == OwnerStatusAction.Deactivated ? "deactivated" : "reactivated",
                row.Reason,
                row.AdminId,
                row.AdminName,
                DateTime.SpecifyKind(row.CreatedAt, DateTimeKind.Utc)))
            .ToList();
    }
}
