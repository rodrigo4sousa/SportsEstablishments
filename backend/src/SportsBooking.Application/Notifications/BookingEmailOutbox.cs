using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Bookings;

namespace SportsBooking.Application.Notifications;

/// <summary>
/// US22: puts the email about a booking in the outbox, on the same <see cref="IAppDbContext"/> as the booking, so the
/// caller's own <c>SaveChangesAsync</c> (inside its pitch-lock transaction) writes both or neither. Nothing is saved here.
/// A booking the club made for a customer (US13) has no account and no email: nothing is queued for it.
/// </summary>
public sealed class BookingEmailOutbox(IAppDbContext dbContext, TimeProvider timeProvider)
{
    public Task EnqueueConfirmationAsync(Booking booking, CancellationToken cancellationToken) =>
        EnqueueAsync(booking, OutboxMessageTypes.BookingConfirmed, reason: null, cancellationToken);

    public Task EnqueuePlayerCancellationAsync(Booking booking, CancellationToken cancellationToken) =>
        EnqueueAsync(booking, OutboxMessageTypes.BookingCancelledByPlayer, reason: null, cancellationToken);

    public Task EnqueueClubCancellationAsync(Booking booking, CancellationToken cancellationToken) =>
        EnqueueAsync(booking, OutboxMessageTypes.BookingCancelledByClub, booking.CancellationReason, cancellationToken);

    private async Task EnqueueAsync(Booking booking, string type, string? reason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(booking);
        if (booking.PlayerId is not { } playerId)
        {
            return;
        }

        var place = await dbContext.Pitches.AsNoTracking()
            .Where(pitch => pitch.Id == booking.PitchId)
            .Join(
                dbContext.Clubs,
                pitch => pitch.ClubId,
                club => club.Id,
                (pitch, club) => new { PitchName = pitch.Name, ClubName = club.Name, club.Address, club.Locality, club.TimeZoneId })
            .SingleAsync(cancellationToken);

        var payload = new BookingEmailPayload(
            playerId,
            place.ClubName,
            place.Address,
            place.Locality,
            place.TimeZoneId,
            place.PitchName,
            booking.StartsAt,
            booking.EndsAt,
            booking.Price,
            reason);
        dbContext.OutboxMessages.Add(OutboxMessage.Create(type, payload.Serialize(), timeProvider.GetUtcNow().UtcDateTime));
    }
}
