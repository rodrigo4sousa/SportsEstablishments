using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Application.Notifications;
using SportsBooking.Application.Pitches;
using SportsBooking.Application.Accounts;
using SportsBooking.Domain.Accounts;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Bookings;

/// <summary>
/// T02 for bookings: the way into a club's bookings from the owner area, built on <see cref="OwnedPitches"/>. A booking is
/// reachable only through a pitch of a club the signed-in owner owns, so another owner's booking, and a booking of another
/// club of the same owner, are simply not there. Never read <c>IAppDbContext.Bookings</c> by id from the owner area.
/// </summary>
public sealed class OwnedBookings(OwnedPitches ownedPitches, IAppDbContext dbContext)
{
    /// <summary>The bookings of the pitches of one club, and only if that club is the signed-in owner's.</summary>
    public IQueryable<Booking> Of(Guid clubId)
    {
        var pitches = ownedPitches.Of(clubId);
        return dbContext.Bookings.Where(booking => pitches.Any(pitch => pitch.Id == booking.PitchId));
    }
}

/// <summary>
/// A booking as the club's owner sees it (US11, US12, US13): what was booked and the name of who it is for. For a player's
/// booking that is the player's name and nothing else about the player (no email, no phone: provisional). For a booking the
/// club made for a customer (<see cref="CreatedByClub"/>) it is the customer's name, and <see cref="CustomerPhone"/> is the
/// number the owner entered (provisional); it is null for a player's booking. <see cref="CanCancel"/> is true for a
/// confirmed booking that has not started.
/// </summary>
public sealed record ClubBookingResponse(
    Guid Id,
    Guid PitchId,
    string PitchName,
    string Sport,
    DateTime StartsAt,
    DateTime EndsAt,
    decimal Price,
    string Status,
    string BookerName,
    bool CreatedByClub,
    string? CustomerPhone,
    DateTime CreatedAt,
    DateTime? CancelledAt,
    string? CancellationReason,
    bool CanCancel);

/// <summary>
/// US13: the owner books a slot for a customer who has no account. <see cref="StartsAt"/> is the slot's start as an instant
/// with its zone, exactly as the slot lists give it; the end and the price come from the pitch. Name and phone follow the
/// US01 rules (the phone is required here, and stored without spaces).
/// </summary>
public sealed record CreateClubBookingRequest(Guid PitchId, string StartsAt, string CustomerName, string CustomerPhone)
{
    /// <summary>The start as a UTC instant, or null if it is not an ISO 8601 instant with a zone.</summary>
    public DateTime? StartsAtUtc => SlotInstant.Parse(StartsAt);

    public CreateClubBookingRequest Normalized() => this with
    {
        StartsAt = StartsAt?.Trim() ?? string.Empty,
        CustomerName = CustomerName?.Trim() ?? string.Empty,
        CustomerPhone = PhoneNumberPolicy.Normalize(CustomerPhone) ?? string.Empty,
    };
}

/// <summary>US13 field rules, applied to a <see cref="CreateClubBookingRequest.Normalized"/> request. Messages are pt-PT.</summary>
public sealed class CreateClubBookingRequestValidator : AbstractValidator<CreateClubBookingRequest>
{
    public CreateClubBookingRequestValidator()
    {
        RuleFor(request => request.PitchId)
            .NotEmpty().WithMessage("Indica o campo a reservar.");

        RuleFor(request => request.StartsAt)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o início do slot.")
            .Must((request, _) => request.StartsAtUtc is not null)
            .WithMessage("Indica o início do slot como instante com fuso, por exemplo 2026-10-25T09:00:00Z.");

        RuleFor(request => request.CustomerName)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o nome do cliente.")
            .MaximumLength(AccountNamePolicy.MaximumLength)
            .WithMessage($"O nome pode ter no máximo {AccountNamePolicy.MaximumLength} caracteres.");

        RuleFor(request => request.CustomerPhone)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o telemóvel do cliente.")
            .Must(PhoneNumberPolicy.IsValid)
            .WithMessage($"Indica um número de telemóvel válido, com {PhoneNumberPolicy.MinimumDigits} a {PhoneNumberPolicy.MaximumDigits} algarismos.");
    }
}

/// <summary>US12: why the owner cancels a booking. Required, trimmed, at most <see cref="BookingRules.CancellationReasonMaximumLength"/> characters.</summary>
public sealed record CancelClubBookingRequest(string Reason)
{
    public CancelClubBookingRequest Normalized() => this with { Reason = Reason?.Trim() ?? string.Empty };
}

public sealed class CancelClubBookingRequestValidator : AbstractValidator<CancelClubBookingRequest>
{
    public CancelClubBookingRequestValidator()
    {
        RuleFor(request => request.Reason)
            .NotEmpty().WithMessage("Indica o motivo do cancelamento.")
            .MaximumLength(BookingRules.CancellationReasonMaximumLength)
            .WithMessage($"O motivo pode ter no máximo {BookingRules.CancellationReasonMaximumLength} caracteres.");
    }
}

/// <summary>
/// US11 and US12: one booking of a club, and its cancellation by the club's owner. The club is checked first, then the
/// booking inside that club (both 404, before the request is validated). The cancellation has no window; it runs under
/// the pitch row lock (<see cref="IPitchLock"/>, the same one bookings, blocks and the player's cancellation take) and
/// reads the booking again inside it, so of an owner's cancel and a player's cancel of the same booking exactly one wins
/// and the other gets 422 <c>booking_not_confirmed</c>. The entity decides (<see cref="Booking.CancelByClub"/>).
/// The owner also books for a customer without an account here (US13): same rules and same lock as a player's booking
/// (<see cref="BookingSlotCheck"/>), same constraint (409 <c>slot_taken</c>); such a booking is in no player's list.
/// </summary>
public sealed class ClubBookingService(
    OwnedClubs ownedClubs,
    OwnedPitches ownedPitches,
    OwnedBookings ownedBookings,
    IAppDbContext dbContext,
    IPitchLock pitchLock,
    IBookingStore bookingStore,
    BookingSlotCheck slotCheck,
    BookingEmailOutbox emails,
    IAccountStore accounts,
    CancelClubBookingRequestValidator validator,
    CreateClubBookingRequestValidator createValidator,
    TimeProvider timeProvider)
{
    // Filtered after it is projected, so it is built with an object initializer (see MyBookingsService).
    private sealed class Row
    {
        public required Guid Id { get; init; }

        public required Guid PitchId { get; init; }

        public required string PitchName { get; init; }

        public required Sport Sport { get; init; }

        public required DateTime StartsAt { get; init; }

        public required DateTime EndsAt { get; init; }

        public required decimal Price { get; init; }

        public required BookingStatus Status { get; init; }

        public required string BookerName { get; init; }

        public required bool CreatedByClub { get; init; }

        public required string? CustomerPhone { get; init; }

        public required DateTime CreatedAt { get; init; }

        public required DateTime? CancelledAt { get; init; }

        public required string? CancellationReason { get; init; }
    }

    /// <summary>
    /// US13: books a slot for a customer. Order, as for a player (US18) but through the owner's club: club (404, before
    /// validating) → validation (400) → pitch of this club (404 <c>pitch_not_found</c>) → the owner is active (422
    /// <c>club_unavailable</c>) → pitch lock → pitch read again → <see cref="BookingSlotCheck"/> (422 inactive, outside the
    /// window, invalid slot, started; 409 blocked) → insert through the store (409 <c>slot_taken</c> from the constraint).
    /// </summary>
    public async Task<Result<ClubBookingResponse>> CreateAsync(Guid clubId, CreateClubBookingRequest request, CancellationToken cancellationToken)
    {
        if (!await ownedPitches.ClubIsOwnedAsync(clubId, cancellationToken))
        {
            return ClubErrors.NotFound;
        }

        var normalized = request.Normalized();
        var validation = await createValidator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var found = await ownedPitches.FindAsync(clubId, normalized.PitchId, tracked: false, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        // US29: a deactivated owner's clubs take no bookings, whoever makes them (the token may outlive the account for a while).
        if (!await accounts.IsActiveAsync(ownedClubs.OwnerId, cancellationToken))
        {
            return BookingErrors.ClubUnavailable;
        }

        var timeZone = await ownedPitches.TimeZoneOfAsync(clubId, cancellationToken);
        var startsAt = normalized.StartsAtUtc!.Value;
        Guid bookingId;

        await using (var scope = await pitchLock.LockAsync(normalized.PitchId, cancellationToken))
        {
            // Read under the lock: a deactivation, a price or a schedule change that got the lock first is seen here.
            var pitch = await dbContext.Pitches.AsNoTracking().SingleAsync(candidate => candidate.Id == normalized.PitchId, cancellationToken);
            var now = Now();
            var checkedSlot = await slotCheck.CheckAsync(pitch, timeZone, startsAt, now, cancellationToken);
            if (!checkedSlot.IsSuccess)
            {
                return checkedSlot.Error!;
            }

            var booking = Booking.ForCustomer(pitch.Id, normalized.CustomerName, normalized.CustomerPhone, startsAt, checkedSlot.Value.EndsAtUtc, pitch.PricePerSlot, now);
            if (!await bookingStore.TryAddAsync(booking, cancellationToken))
            {
                return BookingErrors.SlotTaken;
            }

            await scope.CommitAsync(cancellationToken);
            bookingId = booking.Id;
        }

        return await GetAsync(clubId, bookingId, cancellationToken);
    }

    /// <summary>The booking's details, with the name of who it is for (and, for a customer, the phone).</summary>
    public async Task<Result<ClubBookingResponse>> GetAsync(Guid clubId, Guid bookingId, CancellationToken cancellationToken)
    {
        if (!await ownedPitches.ClubIsOwnedAsync(clubId, cancellationToken))
        {
            return ClubErrors.NotFound;
        }

        var row = await Rows(clubId).Where(candidate => candidate.Id == bookingId).SingleOrDefaultAsync(cancellationToken);
        return row is null ? BookingErrors.NotFound : ToResponse(row, Now());
    }

    /// <summary>
    /// Cancels a confirmed booking that has not started, with the owner's reason: 422 <c>booking_not_confirmed</c> if it
    /// is not confirmed (cancelled already, no-show), then 422 <c>booking_already_started</c> if it has started.
    /// </summary>
    public async Task<Result<ClubBookingResponse>> CancelAsync(
        Guid clubId,
        Guid bookingId,
        CancelClubBookingRequest request,
        CancellationToken cancellationToken)
    {
        if (!await ownedPitches.ClubIsOwnedAsync(clubId, cancellationToken))
        {
            return ClubErrors.NotFound;
        }

        // Which pitch to lock; only a booking of this club is found.
        var pitchId = await ownedBookings.Of(clubId).AsNoTracking()
            .Where(booking => booking.Id == bookingId)
            .Select(booking => (Guid?)booking.PitchId)
            .SingleOrDefaultAsync(cancellationToken);
        if (pitchId is null)
        {
            return BookingErrors.NotFound;
        }

        var normalized = request.Normalized();
        var validation = await validator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        await using (var scope = await pitchLock.LockAsync(pitchId.Value, cancellationToken))
        {
            // Read under the lock, so a cancel that got here first is seen.
            var booking = await dbContext.Bookings.SingleAsync(candidate => candidate.Id == bookingId, cancellationToken);
            switch (booking.CancelByClub(normalized.Reason, Now()))
            {
                case ClubCancellationOutcome.NotConfirmed:
                    return BookingErrors.NotConfirmed;
                case ClubCancellationOutcome.AlreadyStarted:
                    return BookingErrors.AlreadyStarted;
            }

            // US22: the player is told, with the reason, in the same SaveChanges; a customer booking (US13) queues nothing.
            await emails.EnqueueClubCancellationAsync(booking, cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);
        }

        return await GetAsync(clubId, bookingId, cancellationToken);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private IQueryable<Row> Rows(Guid clubId) =>
        from pitch in ownedPitches.Of(clubId).AsNoTracking()
        join booking in dbContext.Bookings on pitch.Id equals booking.PitchId
        // A booking the club made for a customer has no account to join (US13): the name is the customer's.
        join account in dbContext.AccountNames on booking.PlayerId equals (Guid?)account.Id into accounts
        from account in accounts.DefaultIfEmpty()
        select new Row
        {
            Id = booking.Id,
            PitchId = pitch.Id,
            PitchName = pitch.Name,
            Sport = pitch.Sport,
            StartsAt = booking.StartsAt,
            EndsAt = booking.EndsAt,
            Price = booking.Price,
            Status = booking.Status,
            BookerName = booking.PlayerId == null ? booking.CustomerName! : account.Name,
            CreatedByClub = booking.PlayerId == null,
            CustomerPhone = booking.CustomerPhone,
            CreatedAt = booking.CreatedAt,
            CancelledAt = booking.CancelledAt,
            CancellationReason = booking.CancellationReason,
        };

    private static ClubBookingResponse ToResponse(Row row, DateTime now) => new(
        row.Id,
        row.PitchId,
        row.PitchName,
        Sports.ToCode(row.Sport),
        row.StartsAt,
        row.EndsAt,
        row.Price,
        BookingStatuses.ToCode(row.Status),
        row.BookerName,
        row.CreatedByClub,
        row.CustomerPhone,
        row.CreatedAt,
        row.CancelledAt,
        row.CancellationReason,
        row.Status == BookingStatus.Confirmed && row.StartsAt > now);
}
