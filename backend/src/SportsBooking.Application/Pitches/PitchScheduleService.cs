using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Pitches;

/// <summary>A pitch's slot length and week, in the same shape as <see cref="PitchScheduleRequest"/>.</summary>
public sealed record PitchScheduleResponse(
    int SlotDurationMinutes,
    DayHours Monday,
    DayHours Tuesday,
    DayHours Wednesday,
    DayHours Thursday,
    DayHours Friday,
    DayHours Saturday,
    DayHours Sunday)
{
    public static PitchScheduleResponse From(Pitch pitch) => new(
        pitch.SlotDurationMinutes,
        DayHours.From(pitch.HoursOn(DayOfWeek.Monday)),
        DayHours.From(pitch.HoursOn(DayOfWeek.Tuesday)),
        DayHours.From(pitch.HoursOn(DayOfWeek.Wednesday)),
        DayHours.From(pitch.HoursOn(DayOfWeek.Thursday)),
        DayHours.From(pitch.HoursOn(DayOfWeek.Friday)),
        DayHours.From(pitch.HoursOn(DayOfWeek.Saturday)),
        DayHours.From(pitch.HoursOn(DayOfWeek.Sunday)));
}

/// <summary>One generated slot, as UTC instants.</summary>
/// <param name="Blocked">True when the slot overlaps a block of the pitch (US09). Bookings are not taken into account yet.</param>
public sealed record SlotResponse(DateTime StartsAt, DateTime EndsAt, bool Blocked);

/// <summary>The slots a pitch's schedule generates on one local date; slots that overlap a block are marked (US09).</summary>
public sealed record PitchSlotsResponse(DateOnly Date, int SlotDurationMinutes, IReadOnlyList<SlotResponse> Slots);

/// <summary>
/// US08: a pitch's slot length and weekly hours, and a read-only preview of the slots they generate. Every pitch is
/// reached through <see cref="OwnedPitches"/> (T02), before any validation. Changing the schedule never touches
/// bookings: they keep their own start and end (US17 treats a slot as taken if anything overlaps it).
/// </summary>
public sealed class PitchScheduleService(
    OwnedPitches ownedPitches,
    OwnedClubs ownedClubs,
    IAppDbContext dbContext,
    PitchScheduleRequestValidator validator,
    TimeProvider timeProvider)
{
    private const string DateFormat = "yyyy-MM-dd";

    public async Task<Result<PitchScheduleResponse>> GetAsync(Guid clubId, Guid pitchId, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: false, cancellationToken);
        return found.IsSuccess ? PitchScheduleResponse.From(found.Value) : found.Error!;
    }

    /// <summary>Replaces the slot length and the whole week.</summary>
    public async Task<Result<PitchScheduleResponse>> UpdateAsync(Guid clubId, Guid pitchId, PitchScheduleRequest request, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: true, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var pitch = found.Value;
        pitch.ChangeSchedule(SlotDuration.FromMinutes(request.SlotDurationMinutes), request.Week(), timeProvider.GetUtcNow().UtcDateTime);
        await dbContext.SaveChangesAsync(cancellationToken);
        return PitchScheduleResponse.From(pitch);
    }

    /// <summary>
    /// The slots of <paramref name="date"/> (a local date, "yyyy-MM-dd") from the pitch's hours, start and end as UTC
    /// instants. Only dates from today up to 30 days ahead in the club's time zone, like bookings.
    /// </summary>
    public async Task<Result<PitchSlotsResponse>> SlotsAsync(Guid clubId, Guid pitchId, string? date, CancellationToken cancellationToken)
    {
        var found = await ownedPitches.FindAsync(clubId, pitchId, tracked: false, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Error!;
        }

        if (!DateOnly.TryParseExact(date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return DateError("Indica a data no formato AAAA-MM-DD, por exemplo 2026-10-25.");
        }

        var timeZoneId = await ownedClubs.All().AsNoTracking()
            .Where(club => club.Id == clubId)
            .Select(club => club.TimeZoneId)
            .SingleAsync(cancellationToken);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var today = ClubTime.Today(timeProvider, timeZone);
        if (!BookingWindow.Contains(day, today))
        {
            return DateError($"Escolhe uma data de hoje até {BookingWindow.DaysAhead} dias à frente.");
        }

        var pitch = found.Value;
        var generated = pitch.SlotsOn(day, timeZone);
        var blocks = generated.Count == 0
            ? []
            : await LoadBlocksAsync(pitchId, generated[0].StartsAtUtc, generated[^1].EndsAtUtc, cancellationToken);
        var slots = generated
            .Select(slot => new SlotResponse(slot.StartsAtUtc, slot.EndsAtUtc, blocks.Any(block => block.Overlaps(slot.StartsAtUtc, slot.EndsAtUtc))))
            .ToList();
        return new PitchSlotsResponse(day, pitch.SlotDurationMinutes, slots);
    }

    private async Task<List<Block>> LoadBlocksAsync(Guid pitchId, DateTime from, DateTime to, CancellationToken cancellationToken) =>
        await dbContext.Blocks.AsNoTracking()
            .Where(block => block.PitchId == pitchId && block.StartsAt < to && from < block.EndsAt)
            .ToListAsync(cancellationToken);

    private static ServiceError DateError(string message) =>
        ServiceError.Validation(new Dictionary<string, string[]> { ["date"] = [message] });
}
