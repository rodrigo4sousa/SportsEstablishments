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

    /// <summary>
    /// US23: tells the club's owner that a player booked, if the club has the setting on (the default). Only called for a player's
    /// booking: what an owner books for a customer (US13) never reaches here, and a customer booking would send nothing anyway.
    /// </summary>
    public async Task EnqueueOwnerNotificationAsync(Booking booking, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(booking);
        if (booking.PlayerId is null)
        {
            return;
        }

        var notify = await dbContext.Pitches.AsNoTracking()
            .Where(pitch => pitch.Id == booking.PitchId)
            .Join(dbContext.Clubs, pitch => pitch.ClubId, club => club.Id, (_, club) => club.NotifyOnNewBooking)
            .SingleAsync(cancellationToken);
        if (!notify)
        {
            return;
        }

        dbContext.OutboxMessages.Add(OutboxMessage.Create(
            OutboxMessageTypes.OwnerNewBooking, new BookingRefPayload(booking.Id).Serialize(), timeProvider.GetUtcNow().UtcDateTime));
    }

    /// <summary>
    /// US21: the player's reminder, due 3 hours before the start (<see cref="BookingReminderRules.DueAt"/>), queued in the booking's
    /// own transaction. None for a customer's booking, and none when that moment is not in the future. A unique index allows one
    /// per booking.
    /// </summary>
    public void EnqueueReminder(Booking booking)
    {
        ArgumentNullException.ThrowIfNull(booking);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (booking.PlayerId is null || BookingReminderRules.DueAt(booking.StartsAt, now) is not { } due)
        {
            return;
        }

        dbContext.OutboxMessages.Add(OutboxMessage.Create(OutboxMessageTypes.BookingReminder, new BookingRefPayload(booking.Id).Serialize(), now, due));
    }

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
