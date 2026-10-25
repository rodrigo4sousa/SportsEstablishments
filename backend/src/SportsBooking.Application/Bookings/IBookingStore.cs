using SportsBooking.Domain.Bookings;

namespace SportsBooking.Application.Bookings;

/// <summary>Booking persistence that needs the database's own rules; implemented in Infrastructure.</summary>
public interface IBookingStore
{
    /// <summary>
    /// Adds and saves the booking. Returns <c>false</c> when the database refuses it because a confirmed booking of
    /// the same pitch overlaps it (T03, exclusion constraint, SQLSTATE 23P01); nothing is saved then.
    /// </summary>
    Task<bool> TryAddAsync(Booking booking, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the changes already made to tracked bookings (US15: a no-show taken back). Returns <c>false</c> when the database refuses
    /// because that makes a confirmed booking overlap another (23P01); the changes are forgotten then, and the caller's transaction
    /// is aborted (it must not be committed).
    /// </summary>
    Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken);
}
