using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Pitches;

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
/// A booking as the club's owner sees it (US11, US12): what the player booked and the player's name, nothing else about
/// the player (no email, no phone: provisional). <see cref="CanCancel"/> is true for a confirmed booking that has not started.
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
    string PlayerName,
    DateTime CreatedAt,
    DateTime? CancelledAt,
    string? CancellationReason,
    bool CanCancel);

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
/// </summary>
public sealed class ClubBookingService(
    OwnedPitches ownedPitches,
    OwnedBookings ownedBookings,
    IAppDbContext dbContext,
    IPitchLock pitchLock,
    CancelClubBookingRequestValidator validator,
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

        public required string PlayerName { get; init; }

        public required DateTime CreatedAt { get; init; }

        public required DateTime? CancelledAt { get; init; }

        public required string? CancellationReason { get; init; }
    }

    /// <summary>The booking's details, with the player's name only.</summary>
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

            await dbContext.SaveChangesAsync(cancellationToken);
            await scope.CommitAsync(cancellationToken);
        }

        return await GetAsync(clubId, bookingId, cancellationToken);
    }

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    private IQueryable<Row> Rows(Guid clubId) =>
        ownedPitches.Of(clubId).AsNoTracking()
            .Join(dbContext.Bookings, pitch => pitch.Id, booking => booking.PitchId, (pitch, booking) => new { Pitch = pitch, Booking = booking })
            .Join(
                dbContext.AccountNames,
                joined => joined.Booking.PlayerId,
                account => (Guid?)account.Id,
                (joined, account) => new Row
                {
                    Id = joined.Booking.Id,
                    PitchId = joined.Pitch.Id,
                    PitchName = joined.Pitch.Name,
                    Sport = joined.Pitch.Sport,
                    StartsAt = joined.Booking.StartsAt,
                    EndsAt = joined.Booking.EndsAt,
                    Price = joined.Booking.Price,
                    Status = joined.Booking.Status,
                    PlayerName = account.Name,
                    CreatedAt = joined.Booking.CreatedAt,
                    CancelledAt = joined.Booking.CancelledAt,
                    CancellationReason = joined.Booking.CancellationReason,
                });

    private static ClubBookingResponse ToResponse(Row row, DateTime now) => new(
        row.Id,
        row.PitchId,
        row.PitchName,
        Sports.ToCode(row.Sport),
        row.StartsAt,
        row.EndsAt,
        row.Price,
        BookingStatuses.ToCode(row.Status),
        row.PlayerName,
        row.CreatedAt,
        row.CancelledAt,
        row.CancellationReason,
        row.Status == BookingStatus.Confirmed && row.StartsAt > now);
}
