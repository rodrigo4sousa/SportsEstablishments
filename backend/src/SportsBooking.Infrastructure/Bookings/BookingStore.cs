using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SportsBooking.Application.Bookings;
using SportsBooking.Domain.Bookings;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Bookings;

internal sealed partial class BookingStore(AppDbContext dbContext, ILogger<BookingStore> logger) : IBookingStore
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
            LogSlotTaken(logger, booking.PitchId, booking.StartsAt, booking.EndsAt);
            return false;
        }
    }

    // Expected, not a fault: someone else booked the slot first. Only the pitch and the slot are logged, never the player.
    [LoggerMessage(Level = LogLevel.Information, Message = "Booking refused: slot {StartsAt:O}-{EndsAt:O} of pitch {PitchId} overlaps a confirmed booking (bookings_no_overlap).")]
    private static partial void LogSlotTaken(ILogger logger, Guid pitchId, DateTime startsAt, DateTime endsAt);
}
