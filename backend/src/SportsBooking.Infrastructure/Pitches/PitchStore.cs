using Microsoft.EntityFrameworkCore;
using Npgsql;
using SportsBooking.Application.Pitches;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Pitches;

internal sealed class PitchStore(AppDbContext dbContext) : IPitchStore
{
    public async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: PitchConfiguration.NameIndex,
        })
        {
            return false;
        }
    }
}
