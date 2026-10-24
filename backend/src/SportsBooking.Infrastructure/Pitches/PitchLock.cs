using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SportsBooking.Application.Pitches;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Pitches;

internal sealed class PitchLock(AppDbContext dbContext) : IPitchLock
{
    public async Task<IPitchLockScope> LockAsync(Guid pitchId, CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // READ COMMITTED: once the lock is ours, the next statement sees what the previous holder committed.
            await dbContext.Database.ExecuteSqlAsync($"SELECT 1 FROM pitches WHERE id = {pitchId} FOR UPDATE", cancellationToken);
            return new Scope(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

    private sealed class Scope(IDbContextTransaction transaction) : IPitchLockScope
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
