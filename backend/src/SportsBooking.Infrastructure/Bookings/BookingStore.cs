using Microsoft.EntityFrameworkCore;
using Npgsql;
using SportsBooking.Application.Bookings;
using SportsBooking.Domain.Bookings;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Bookings;

internal sealed class BookingStore(AppDbContext dbContext) : IBookingStore
{
    public async Task<bool> TryAddAsync(Booking booking, CancellationToken cancellationToken)
    {
        dbContext.Bookings.Add(booking);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ExclusionViolation,
            ConstraintName: BookingConfiguration.NoOverlapConstraint,
        })
        {
            // T03: another confirmed booking overlaps. The caller's transaction is aborted and rolls back.
            dbContext.Entry(booking).State = EntityState.Detached;
            return false;
        }
    }
}
