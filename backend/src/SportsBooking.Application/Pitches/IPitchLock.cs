namespace SportsBooking.Application.Pitches;

/// <summary>
/// Serialises everything that decides whether a period of a pitch is free (docs/stack.md, "Dados"): a transaction that
/// first locks the pitch row (<c>SELECT … FOR UPDATE</c>), so a block and a booking for the same pitch cannot both
/// pass their overlap check. Implemented in Infrastructure (PostgreSQL).
/// </summary>
public interface IPitchLock
{
    /// <summary>
    /// Starts a transaction on the current <see cref="Common.IAppDbContext"/> and locks the pitch row. Queries and
    /// <c>SaveChangesAsync</c> made on the same context until <see cref="IPitchLockScope.CommitAsync"/> run inside it;
    /// disposing without committing rolls back. A second caller for the same pitch waits here.
    /// </summary>
    Task<IPitchLockScope> LockAsync(Guid pitchId, CancellationToken cancellationToken);
}

public interface IPitchLockScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
