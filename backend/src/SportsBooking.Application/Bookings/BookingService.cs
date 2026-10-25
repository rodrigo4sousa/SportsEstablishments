using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Common;
using SportsBooking.Application.Notifications;
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

    /// <summary>A booking that does not exist and a booking of another player give exactly this answer.</summary>
    public static ServiceError NotFound { get; } = new(
        ErrorKind.NotFound, ErrorCodes.BookingNotFound, "Reserva não encontrada", "Não encontrámos esta reserva.");

    public static ServiceError NotConfirmed { get; } = new(
        ErrorKind.Unprocessable,
        ErrorCodes.BookingNotConfirmed,
        "Reserva não confirmada",
        "Só é possível cancelar uma reserva confirmada.");

    /// <summary>The club's owner cancels a booking that has started (US12): it stays as history.</summary>
    public static ServiceError AlreadyStarted { get; } = new(
        ErrorKind.Unprocessable,
        ErrorCodes.BookingAlreadyStarted,
        "Reserva já começou",
        "Esta reserva já começou, por isso já não pode ser cancelada.");

    /// <summary>The window has closed. The deadline is a UTC instant in the <c>cancellationDeadline</c> extension.</summary>
    public static ServiceError OutsideCancellationWindow(DateTime deadlineUtc, int windowHours) => new(
        ErrorKind.Unprocessable,
        ErrorCodes.OutsideCancellationWindow,
        "Prazo de cancelamento terminado",
        $"Só é possível cancelar até {windowHours} {(windowHours == 1 ? "hora" : "horas")} antes do início, e esse prazo já passou.",
        Extensions: new Dictionary<string, object?> { ["cancellationDeadline"] = deadlineUtc });

    /// <summary>US15: marking a no-show needs a confirmed booking (same code as for a cancellation, wording for this action).</summary>
    public static ServiceError NoShowNotConfirmed { get; } = new(
        ErrorKind.Unprocessable,
        ErrorCodes.BookingNotConfirmed,
        "Reserva não confirmada",
        "Só é possível marcar falta numa reserva confirmada.");

    /// <summary>US15: the booking has not ended yet, so nobody can have failed to come.</summary>
    public static ServiceError NotEnded { get; } = new(
        ErrorKind.Unprocessable,
        ErrorCodes.BookingNotEnded,
        "Reserva ainda não terminou",
        "Só é possível marcar falta depois de a reserva terminar.");

    /// <summary>US15: only a no-show can be taken back.</summary>
    public static ServiceError NotNoShow { get; } = new(
        ErrorKind.Unprocessable,
        ErrorCodes.BookingNotNoShow,
        "Reserva sem falta",
        "Esta reserva não está marcada como falta.");

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
    BookingSlotCheck slotCheck,
    BookingEmailOutbox emails,
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
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var checkedSlot = await slotCheck.CheckAsync(pitch, timeZone, startsAt, now, cancellationToken);
        if (!checkedSlot.IsSuccess)
        {
            return checkedSlot.Error!;
        }

        var endsAt = checkedSlot.Value.EndsAtUtc;

        var booking = Booking.ForPlayer(pitch.Id, playerId, startsAt, endsAt, pitch.PricePerSlot, now);
        // US22: the confirmation email is queued now and written by the same SaveChanges as the booking, so a booking
        // that loses the race (23P01, rolled back) leaves no email behind.
        await emails.EnqueueConfirmationAsync(booking, cancellationToken);
        // US23 and US21: the owner is told (if the club wants it) and the player is reminded 3 hours before: same transaction, same fate.
        await emails.EnqueueOwnerNotificationAsync(booking, cancellationToken);
        emails.EnqueueReminder(booking);
        if (!await bookingStore.TryAddAsync(booking, cancellationToken))
        {
            return BookingErrors.SlotTaken;
        }

        await scope.CommitAsync(cancellationToken);
        return new BookingResponse(booking.Id, booking.PitchId, club.Id, booking.StartsAt, booking.EndsAt, booking.Price, BookingStatuses.ToCode(booking.Status));
    }
}
