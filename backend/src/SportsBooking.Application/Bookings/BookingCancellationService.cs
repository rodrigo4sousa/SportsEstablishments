using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Application.Notifications;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Bookings;

namespace SportsBooking.Application.Bookings;

/// <summary>
/// US19: a player cancels their own booking. Another player's booking and an unknown id are the same 404. The booking
/// is changed by <see cref="Booking.CancelByPlayer"/> and kept (never deleted); the exclusion constraint only covers
/// confirmed bookings, so the slot is free again. Order: 404 → only a confirmed booking (422
/// <c>booking_not_confirmed</c>) → the club's window (422 <c>outside_cancellation_window</c>, with the deadline).
/// </summary>
/// <remarks>
/// The window is read from the club at the moment of cancelling, never from the booking (US06). The work runs under the
/// pitch row lock (<see cref="IPitchLock"/>, the same one bookings and blocks take) and reads the booking again inside
/// it, so of two concurrent cancels of the same booking one succeeds and the other sees it cancelled already and gets
/// the 422; neither is a 500. The email to the player is US22.
/// </remarks>
public sealed class BookingCancellationService(
    IAppDbContext dbContext,
    IPitchLock pitchLock,
    ICurrentUser currentUser,
    MyBookingsService myBookings,
    BookingEmailOutbox emails,
    TimeProvider timeProvider)
{
    public async Task<Result<MyBookingResponse>> CancelAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var playerId = currentUser.UserId
            ?? throw new InvalidOperationException("Cancelling needs a signed-in player: protect the endpoint with the Player policy.");

        // Which pitch to lock; only the caller's own bookings are found.
        var pitchId = await dbContext.Bookings.AsNoTracking()
            .Where(booking => booking.Id == bookingId && booking.PlayerId == playerId)
            .Select(booking => (Guid?)booking.PitchId)
            .SingleOrDefaultAsync(cancellationToken);
        if (pitchId is null)
        {
            return BookingErrors.NotFound;
        }

        await using (var scope = await pitchLock.LockAsync(pitchId.Value, cancellationToken))
        {
            // Read under the lock, so a cancel that got here first is seen.
            var booking = await dbContext.Bookings.SingleAsync(candidate => candidate.Id == bookingId, cancellationToken);
            var windowHours = await dbContext.Pitches
                .Where(pitch => pitch.Id == booking.PitchId)
                .Join(dbContext.Clubs, pitch => pitch.ClubId, club => club.Id, (_, club) => club.CancellationWindowHours)
                .SingleAsync(cancellationToken);
            var window = TimeSpan.FromHours(windowHours);

            switch (booking.CancelByPlayer(window, timeProvider.GetUtcNow().UtcDateTime))
            {
                case CancellationOutcome.NotConfirmed:
                    return BookingErrors.NotConfirmed;
                case CancellationOutcome.OutsideWindow:
                    return BookingErrors.OutsideCancellationWindow(booking.CancellationDeadline(window), windowHours);
            }

            // US22: the email is written with the cancellation, in the same SaveChanges.
            await emails.EnqueuePlayerCancellationAsync(booking, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);
        }

        return await myBookings.GetAsync(bookingId, cancellationToken);
    }
}
