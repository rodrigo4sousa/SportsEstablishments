using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Bookings;

/// <summary>A booking as its player sees it. <see cref="Status"/> is a stable code (<c>confirmed</c>, …); times are UTC.</summary>
public sealed record BookingResponse(Guid Id, Guid PitchId, Guid ClubId, DateTime StartsAt, DateTime EndsAt, decimal Price, string Status);

public static class BookingErrors
{
    public static ServiceError SlotTaken { get; } = new(
        ErrorKind.Conflict, ErrorCodes.SlotTaken, "Horário ocupado", "Este horário acabou de ser reservado. Escolhe outro.");

    public static ServiceError SlotBlocked { get; } = new(
        ErrorKind.Conflict, ErrorCodes.SlotBlocked, "Horário bloqueado", "Este horário está bloqueado pelo clube. Escolhe outro.");

    public static ServiceError InvalidSlot { get; } = new(
        ErrorKind.Unprocessable, ErrorCodes.InvalidSlot, "Horário inválido", "Este horário não é um slot deste campo.");

    public static ServiceError SlotStarted { get; } = new(
        ErrorKind.Unprocessable, ErrorCodes.SlotStarted, "Horário já começou", "Este horário já começou. Escolhe outro.");

    public static ServiceError OutsideWindow { get; } = new(
        ErrorKind.Unprocessable,
        ErrorCodes.OutsideBookingWindow,
        "Data fora do período de reservas",
        $"Só é possível reservar de hoje até {BookingWindow.DaysAhead} dias à frente.");

    public static ServiceError PitchInactive { get; } = new(
        ErrorKind.Unprocessable, ErrorCodes.PitchInactive, "Campo indisponível", "Este campo não está a aceitar reservas.");

    public static ServiceError ClubUnavailable { get; } = new(
        ErrorKind.Unprocessable, ErrorCodes.ClubUnavailable, "Clube indisponível", "Este clube não está a aceitar reservas.");
}

/// <summary>
/// US18 and T03: a player books a slot. Pitches are public (one marketplace), so any pitch can be booked; the slot
/// must be one the pitch's schedule generates, and its end and price come from the pitch. Everything that decides
/// whether the slot is free runs under the pitch row lock (<see cref="IPitchLock"/>, the same one blocks take): the
/// pitch is read again, then blocks are checked, then the booking is inserted. The exclusion constraint on
/// <c>bookings</c> is the last line of defence against another confirmed booking: its violation is 409 <c>slot_taken</c>.
/// </summary>
public sealed class BookingService(
    IAppDbContext dbContext,
    IPitchLock pitchLock,
    IBookingStore bookingStore,
    IAccountStore accounts,
    ICurrentUser currentUser,
    CreateBookingRequestValidator validator,
    TimeProvider timeProvider)
{
    public async Task<Result<BookingResponse>> CreateAsync(CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var playerId = currentUser.UserId
            ?? throw new InvalidOperationException("Booking needs a signed-in player: protect the endpoint with the Player policy.");

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var startsAt = request.StartsAtUtc!.Value;
        var club = await dbContext.Pitches.AsNoTracking()
            .Where(pitch => pitch.Id == request.PitchId)
            .Join(dbContext.Clubs, pitch => pitch.ClubId, club => club.Id, (_, club) => new { club.Id, club.OwnerId, club.TimeZoneId })
            .SingleOrDefaultAsync(cancellationToken);
        if (club is null)
        {
            return PitchErrors.NotFound;
        }

        // US29: a deactivated owner's clubs take no bookings.
        if (!await accounts.IsActiveAsync(club.OwnerId, cancellationToken))
        {
            return BookingErrors.ClubUnavailable;
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(club.TimeZoneId);

        await using var scope = await pitchLock.LockAsync(request.PitchId, cancellationToken);

        // Read under the lock: a deactivation, a price or a schedule change that got the lock first is seen here.
        var pitch = await dbContext.Pitches.AsNoTracking().SingleAsync(candidate => candidate.Id == request.PitchId, cancellationToken);
        if (!pitch.IsActive)
        {
            return BookingErrors.PitchInactive;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var localDate = DateOnly.FromDateTime(ClubTime.ToLocal(startsAt, timeZone));
        if (!BookingWindow.Contains(localDate, ClubTime.Today(timeProvider, timeZone)))
        {
            return BookingErrors.OutsideWindow;
        }

        if (pitch.SlotStartingAt(startsAt, timeZone) is not { } slot)
        {
            return BookingErrors.InvalidSlot;
        }

        if (slot.StartsAtUtc <= now)
        {
            return BookingErrors.SlotStarted;
        }

        var endsAt = slot.EndsAtUtc;
        if (await dbContext.Blocks.AnyAsync(block => block.PitchId == pitch.Id && block.StartsAt < endsAt && startsAt < block.EndsAt, cancellationToken))
        {
            return BookingErrors.SlotBlocked;
        }

        var booking = Booking.ForPlayer(pitch.Id, playerId, startsAt, endsAt, pitch.PricePerSlot, now);
        if (!await bookingStore.TryAddAsync(booking, cancellationToken))
        {
            return BookingErrors.SlotTaken;
        }

        await scope.CommitAsync(cancellationToken);
        return new BookingResponse(booking.Id, booking.PitchId, club.Id, booking.StartsAt, booking.EndsAt, booking.Price, BookingStatuses.ToCode(booking.Status));
    }
}
