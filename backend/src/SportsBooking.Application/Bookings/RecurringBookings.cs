using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Catalog;
using SportsBooking.Application.Common;
using SportsBooking.Application.Notifications;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Bookings;

/// <summary>
/// US20: the same slot every week. <see cref="StartsAt"/> is the first occurrence, as an instant with its zone (as the slot lists
/// give it); <see cref="Weeks"/> is how many occurrences, the first included (1 to <see cref="RecurringBookingRules.MaximumWeeks"/>).
/// </summary>
public sealed record RecurringBookingRequest(Guid PitchId, string StartsAt, int Weeks)
{
    public DateTime? StartsAtUtc => SlotInstant.Parse(StartsAt);
}

public static class RecurringBookingRules
{
    /// <summary>US20: at most four weeks, the first one included.</summary>
    public const int MaximumWeeks = 4;
}

/// <summary>Why a date of a series cannot be booked: stable codes, never who booked it.</summary>
public static class OccurrenceReasons
{
    public const string Taken = "taken";
    public const string Blocked = "blocked";
    public const string NotASlot = "not_a_slot";
    public const string OutsideWindow = "outside_window";
    public const string Closed = "closed";
    public const string PitchInactive = "pitch_inactive";

    /// <summary>Provisional: only the first date can have started already (a request made during that slot).</summary>
    public const string Started = "started";
}

/// <summary>
/// One date of a series in the preview. <see cref="Status"/> is <c>free</c> or <c>unavailable</c>, the latter with a
/// <see cref="Reason"/> from <see cref="OccurrenceReasons"/>. The end and the price are what a booking of that date would have
/// (for a date that is not a slot, the end is the start plus the pitch's slot length).
/// </summary>
public sealed record OccurrencePreview(DateTime StartsAt, DateTime EndsAt, decimal Price, string Status, string? Reason);

public sealed record RecurringPreviewResponse(int Weeks, IReadOnlyList<OccurrencePreview> Occurrences);

/// <summary>A date left out of the series, with its reason code.</summary>
public sealed record SkippedOccurrence(DateTime StartsAt, DateTime EndsAt, string Reason);

/// <summary>What one recurring request made: the bookings (sharing <see cref="SeriesId"/>) and the dates left out.</summary>
public sealed record RecurringBookingResponse(Guid SeriesId, IReadOnlyList<BookingResponse> Created, IReadOnlyList<SkippedOccurrence> Skipped);

public sealed class RecurringBookingRequestValidator : AbstractValidator<RecurringBookingRequest>
{
    public RecurringBookingRequestValidator()
    {
        RuleFor(request => request.PitchId)
            .NotEmpty().WithMessage("Indica o campo a reservar.");

        RuleFor(request => request.StartsAt)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Indica o início do slot.")
            .Must((request, _) => request.StartsAtUtc is not null)
            .WithMessage("Indica o início do slot como instante com fuso, por exemplo 2026-10-25T09:00:00Z.");

        RuleFor(request => request.Weeks)
            .InclusiveBetween(1, RecurringBookingRules.MaximumWeeks)
            .WithMessage($"Escolhe entre 1 e {RecurringBookingRules.MaximumWeeks} semanas.");
    }
}

public static class RecurringBookingErrors
{
    /// <summary>US20: no date of the series could be booked; <c>skipped</c> says why for each one.</summary>
    public static ServiceError NoneCreated(IReadOnlyList<SkippedOccurrence> skipped) => new(
        ErrorKind.Conflict,
        ErrorCodes.SeriesUnavailable,
        "Nenhuma data disponível",
        "Nenhuma das datas escolhidas estava disponível, por isso não foi feita nenhuma reserva.",
        Extensions: new Dictionary<string, object?> { ["skipped"] = skipped });
}

/// <summary>The dates of a series: the same local weekday and local clock time, week after week (T04).</summary>
public static class RecurringOccurrences
{
    /// <summary>
    /// The first start exactly as given, then the same local date + 7·n days at the same local time, converted with
    /// <see cref="ClubTime.ToUtc"/>: across a clock change the local time stays and the UTC instant moves by an hour.
    /// </summary>
    public static IReadOnlyList<DateTime> Starts(DateTime firstUtc, int weeks, TimeZoneInfo timeZone)
    {
        var local = ClubTime.ToLocal(firstUtc, timeZone);
        var date = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);
        return Enumerable.Range(0, weeks)
            .Select(week => week == 0 ? firstUtc : ClubTime.ToUtc(date.AddDays(7 * week), time, timeZone))
            .ToList();
    }

    /// <summary>The reason code for a refusal of <see cref="BookingSlotCheck"/>. A closed weekday and a time off the grid are told apart.</summary>
    public static string ReasonFor(ServiceError error, Pitch pitch, DateTime startsAt, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(pitch);
        return error.Code switch
        {
            ErrorCodes.PitchInactive => OccurrenceReasons.PitchInactive,
            ErrorCodes.OutsideBookingWindow => OccurrenceReasons.OutsideWindow,
            ErrorCodes.InvalidSlot => pitch.HoursOn(ClubTime.ToLocal(startsAt, timeZone).DayOfWeek) is null ? OccurrenceReasons.Closed : OccurrenceReasons.NotASlot,
            ErrorCodes.SlotStarted => OccurrenceReasons.Started,
            ErrorCodes.SlotBlocked => OccurrenceReasons.Blocked,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Code, "Not a slot refusal."),
        };
    }

    /// <summary>The end a booking of this start would have: the generated slot's, or the start plus the slot length when it is not a slot.</summary>
    public static DateTime EndOf(Pitch pitch, DateTime startsAt, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(pitch);
        return pitch.SlotStartingAt(startsAt, timeZone)?.EndsAtUtc ?? startsAt.AddMinutes(pitch.SlotDurationMinutes);
    }
}

/// <summary>
/// US20: a player books the same slot for up to four weeks. The preview says which dates are free; booking runs, date by date,
/// exactly what a single booking runs (US18): <see cref="BookingSlotCheck"/> under the pitch lock on the pitch read inside it, the
/// price from <see cref="Pitch.PriceForSlot"/>, the insert through <see cref="IBookingStore.TryAddAsync"/> so the exclusion
/// constraint has the last word. Each date has its own transaction, so a refused date leaves the others alone. The bookings made
/// share one series id; the player gets one summary email for the series (not one confirmation per booking), while the owner's
/// email (US23) and the reminder (US21) are queued per booking, in its own transaction, as for a single booking.
/// </summary>
public sealed class RecurringBookingService(
    IAppDbContext dbContext,
    VisibleClubs visibleClubs,
    IPitchLock pitchLock,
    IBookingStore bookingStore,
    BookingSlotCheck slotCheck,
    BookingEmailOutbox emails,
    IAccountStore accounts,
    ICurrentUser currentUser,
    RecurringBookingRequestValidator validator,
    TimeProvider timeProvider)
{
    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Read-only: every date of the series with its end, its price and whether it is free. Only a pitch the public can see (an active
    /// pitch of a visible club, US16): any other is 404 <c>pitch_not_found</c>. A date is taken when a confirmed booking overlaps it;
    /// nothing says whose.
    /// </summary>
    public async Task<Result<RecurringPreviewResponse>> PreviewAsync(RecurringBookingRequest request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var visible = visibleClubs.All();
        var found = await dbContext.Pitches.AsNoTracking()
            .Where(pitch => pitch.Id == request.PitchId && pitch.Status == PitchStatus.Active)
            .Join(visible, pitch => pitch.ClubId, club => club.Id, (pitch, club) => new { Pitch = pitch, club.TimeZoneId })
            .SingleOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return PitchErrors.NotFound;
        }

        var pitch = found.Pitch;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(found.TimeZoneId);
        var now = Now();
        var occurrences = new List<OccurrencePreview>();
        foreach (var startsAt in RecurringOccurrences.Starts(request.StartsAtUtc!.Value, request.Weeks, timeZone))
        {
            var price = pitch.PriceForSlot(startsAt, timeZone);
            var checkedSlot = await slotCheck.CheckAsync(pitch, timeZone, startsAt, now, cancellationToken);
            if (!checkedSlot.IsSuccess)
            {
                var reason = RecurringOccurrences.ReasonFor(checkedSlot.Error!, pitch, startsAt, timeZone);
                occurrences.Add(new OccurrencePreview(startsAt, RecurringOccurrences.EndOf(pitch, startsAt, timeZone), price, "unavailable", reason));
                continue;
            }

            var endsAt = checkedSlot.Value.EndsAtUtc;
            var taken = await dbContext.Bookings.AnyAsync(
                booking => booking.PitchId == pitch.Id && booking.Status == BookingStatus.Confirmed && booking.StartsAt < endsAt && startsAt < booking.EndsAt,
                cancellationToken);
            occurrences.Add(taken
                ? new OccurrencePreview(startsAt, endsAt, price, "unavailable", OccurrenceReasons.Taken)
                : new OccurrencePreview(startsAt, endsAt, price, "free", null));
        }

        return new RecurringPreviewResponse(request.Weeks, occurrences);
    }

    /// <summary>
    /// Books every free date, in order, each in its own transaction under the pitch lock. A refused date (taken, blocked, not a slot,
    /// …) is left out with its reason and the next one is tried. At least one booking: the series; none: 409
    /// <c>series_unavailable</c> with the reasons. The pitch and the club are checked first, as for a single booking: an unknown
    /// pitch is 404, a club whose owner is deactivated 422 <c>club_unavailable</c>.
    /// </summary>
    public async Task<Result<RecurringBookingResponse>> CreateAsync(RecurringBookingRequest request, CancellationToken cancellationToken)
    {
        var playerId = currentUser.UserId
            ?? throw new InvalidOperationException("Booking needs a signed-in player: protect the endpoint with the Player policy.");

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var club = await dbContext.Pitches.AsNoTracking()
            .Where(pitch => pitch.Id == request.PitchId)
            .Join(dbContext.Clubs, pitch => pitch.ClubId, club => club.Id, (_, club) => new { club.Id, club.OwnerId, club.TimeZoneId })
            .SingleOrDefaultAsync(cancellationToken);
        if (club is null)
        {
            return PitchErrors.NotFound;
        }

        if (!await accounts.IsActiveAsync(club.OwnerId, cancellationToken))
        {
            return BookingErrors.ClubUnavailable;
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(club.TimeZoneId);
        var seriesId = Guid.CreateVersion7();
        var created = new List<Booking>();
        var skipped = new List<SkippedOccurrence>();
        foreach (var startsAt in RecurringOccurrences.Starts(request.StartsAtUtc!.Value, request.Weeks, timeZone))
        {
            var outcome = await BookOneAsync(request.PitchId, playerId, seriesId, startsAt, timeZone, cancellationToken);
            if (outcome.Booking is { } booking)
            {
                created.Add(booking);
            }
            else
            {
                skipped.Add(outcome.Skipped!);
            }
        }

        if (created.Count == 0)
        {
            return RecurringBookingErrors.NoneCreated(skipped);
        }

        // One summary for the series, in its own transaction once the dates are settled (provisional): a crash between the last booking
        // and this save would lose the summary, never a booking.
        await emails.EnqueueSeriesSummaryAsync(created, skipped, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecurringBookingResponse(
            seriesId,
            created.Select(booking => new BookingResponse(booking.Id, booking.PitchId, club.Id, booking.StartsAt, booking.EndsAt, booking.Price, BookingStatuses.ToCode(booking.Status))).ToList(),
            skipped);
    }

    private sealed record Outcome(Booking? Booking, SkippedOccurrence? Skipped);

    /// <summary>One date, as a single booking (US18) would do it, in its own pitch-lock transaction.</summary>
    private async Task<Outcome> BookOneAsync(Guid pitchId, Guid playerId, Guid seriesId, DateTime startsAt, TimeZoneInfo timeZone, CancellationToken cancellationToken)
    {
        await using var scope = await pitchLock.LockAsync(pitchId, cancellationToken);

        // Read under the lock: a deactivation, a price or a schedule change that got the lock first is seen here.
        var pitch = await dbContext.Pitches.AsNoTracking().SingleAsync(candidate => candidate.Id == pitchId, cancellationToken);
        var now = Now();
        var checkedSlot = await slotCheck.CheckAsync(pitch, timeZone, startsAt, now, cancellationToken);
        if (!checkedSlot.IsSuccess)
        {
            var reason = RecurringOccurrences.ReasonFor(checkedSlot.Error!, pitch, startsAt, timeZone);
            return new Outcome(null, new SkippedOccurrence(startsAt, RecurringOccurrences.EndOf(pitch, startsAt, timeZone), reason));
        }

        var endsAt = checkedSlot.Value.EndsAtUtc;
        var booking = Booking.ForPlayer(pitch.Id, playerId, startsAt, endsAt, pitch.PriceForSlot(startsAt, timeZone), now, seriesId);
        // US23 and US21 per booking, in its own transaction; the player's confirmation is the series summary instead.
        await emails.EnqueueOwnerNotificationAsync(booking, cancellationToken);
        emails.EnqueueReminder(booking);
        if (!await bookingStore.TryAddAsync(booking, cancellationToken))
        {
            return new Outcome(null, new SkippedOccurrence(startsAt, endsAt, OccurrenceReasons.Taken));
        }

        await scope.CommitAsync(cancellationToken);
        return new Outcome(booking, null);
    }
}
