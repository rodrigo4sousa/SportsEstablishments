using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Bookings;

/// <summary>
/// The rules that say a slot can be booked, the same for a player (US18) and for an owner booking for a customer (US13):
/// the pitch is active, the slot's date is today to 30 days ahead in the club's time zone, the start is a slot the pitch's
/// schedule generates, it has not started, and no block overlaps it. It does <b>not</b> look at other bookings: that is the
/// exclusion constraint's job (T03). Run it under the pitch row lock, on the pitch read inside it.
/// </summary>
public sealed class BookingSlotCheck(IAppDbContext dbContext, TimeProvider timeProvider)
{
    /// <summary>The slot starting at <paramref name="startsAt"/>, or the first refusal: 422 inactive, outside the window, not a slot, started; 409 blocked.</summary>
    public async Task<Result<TimeSlot>> CheckAsync(Pitch pitch, TimeZoneInfo timeZone, DateTime startsAt, DateTime now, CancellationToken cancellationToken)
    {
        if (!pitch.IsActive)
        {
            return BookingErrors.PitchInactive;
        }

        var localDate = DateOnly.FromDateTime(ClubTime.ToLocal(startsAt, timeZone));
        if (!BookingWindow.Contains(localDate, ClubTime.Today(timeProvider, timeZone)))
        {
            return BookingErrors.OutsideWindow;
        }

        if (pitch.SlotStartingAt(startsAt, timeZone) is not { } slot)
        {
            return BookingErrors.InvalidSlot;
        }

        if (slot.HasStartedBy(now))
        {
            return BookingErrors.SlotStarted;
        }

        var endsAt = slot.EndsAtUtc;
        if (await dbContext.Blocks.AnyAsync(block => block.PitchId == pitch.Id && block.StartsAt < endsAt && startsAt < block.EndsAt, cancellationToken))
        {
            return BookingErrors.SlotBlocked;
        }

        return slot;
    }
}
