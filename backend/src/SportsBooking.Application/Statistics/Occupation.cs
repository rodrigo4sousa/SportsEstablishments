using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Statistics;

/// <summary>
/// The Lisbon dates of a statistics period, both included (US14, US27). Its instants are the start of <see cref="From"/> and the
/// start of the day after <see cref="To"/> in the club's time zone, so a period with 25 October 2026 has a 25-hour day in it.
/// </summary>
public sealed record StatisticsPeriod(DateOnly From, DateOnly To)
{
    public DateTime FromUtc(TimeZoneInfo timeZone) => ClubTime.ToUtc(From, TimeOnly.MinValue, timeZone);

    public DateTime ToUtcExclusive(TimeZoneInfo timeZone) => ClubTime.ToUtc(To.AddDays(1), TimeOnly.MinValue, timeZone);

    public IEnumerable<DateOnly> Days()
    {
        for (var day = From; day <= To; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}

/// <summary>
/// The query string of the statistics endpoints: Lisbon dates <c>yyyy-MM-dd</c>, both optional. Without them the period is the last
/// 30 days (today included), like the admin's chart (US25). Provisional limits: at most <see cref="MaximumDays"/> days, dates between
/// 2000 and 2100.
/// </summary>
public static class StatisticsPeriodInput
{
    public const int DefaultDays = 30;

    public const int MaximumDays = 366;

    private static readonly DateOnly Earliest = new(2000, 1, 1);

    private static readonly DateOnly Latest = new(2100, 12, 31);

    private const string BadDate = "Indica a data no formato AAAA-MM-DD, por exemplo 2026-10-25.";

    public static Result<StatisticsPeriod> Parse(string? from, string? to, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        var fromDate = Read(from, "from", errors);
        var toDate = Read(to, "to", errors);
        if (errors.Count > 0)
        {
            return ServiceError.Validation(errors);
        }

        var end = toDate ?? today;
        var start = fromDate ?? end.AddDays(-(DefaultDays - 1));
        if (start > end)
        {
            // Without an end, the end is today: then it is the start that is wrong.
            return toDate is null
                ? Error("from", "A data inicial não pode ser posterior a hoje.")
                : Error("to", "A data final não pode ser anterior à inicial.");
        }

        if (end.DayNumber - start.DayNumber + 1 > MaximumDays)
        {
            return Error("to", $"O período pode ter no máximo {MaximumDays} dias.");
        }

        return new StatisticsPeriod(start, end);
    }

    public static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly? Read(string? text, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors[field] = [BadDate];
            return null;
        }

        if (date < Earliest || date > Latest)
        {
            errors[field] = [$"A data tem de estar entre {Earliest.Year} e {Latest.Year}."];
            return null;
        }

        return date;
    }

    private static ServiceError Error(string field, string message) => ServiceError.Validation(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>What the database adds up for one pitch over a period: only Confirmed and NoShow bookings that overlap it.</summary>
public sealed record PitchBookingTotals(Guid PitchId, long BookedMinutes, decimal Revenue, int ConfirmedBookings, int NoShows);

/// <summary>
/// The booking side of the statistics, in Infrastructure (one grouped SQL query, nothing loaded into memory): per pitch, the minutes of
/// Confirmed and NoShow bookings that overlap <c>[fromUtc, toUtc)</c>, clipped to it; the sum of their stored prices; and how many of
/// each status. Cancelled bookings are left out of everything. Pitches with no such booking are not returned.
/// </summary>
public interface IOccupationStore
{
    Task<IReadOnlyList<PitchBookingTotals>> TotalsByPitchAsync(IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
}

/// <summary>
/// One set of numbers (US14, US27): booked and open hours, occupation (booked / open, 0 when nothing was open), revenue and the counts.
/// Everything is added up in whole minutes and rounded only here: hours to 2 decimals, occupation to 4 (0.8125 = 81,25 %).
/// </summary>
public sealed record OccupationFigures(long BookedMinutes, long OpenMinutes, decimal Revenue, int ConfirmedBookings, int NoShows)
{
    public static OccupationFigures Zero { get; } = new(0, 0, 0m, 0, 0);

    public decimal BookedHours => Math.Round(BookedMinutes / 60m, 2);

    public decimal OpenHours => Math.Round(OpenMinutes / 60m, 2);

    public decimal Occupation => OpenMinutes == 0 ? 0m : Math.Round((decimal)BookedMinutes / OpenMinutes, 4);

    public static OccupationFigures operator +(OccupationFigures left, OccupationFigures right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        return new OccupationFigures(
            left.BookedMinutes + right.BookedMinutes,
            left.OpenMinutes + right.OpenMinutes,
            left.Revenue + right.Revenue,
            left.ConfirmedBookings + right.ConfirmedBookings,
            left.NoShows + right.NoShows);
    }

    public static OccupationFigures Sum(IEnumerable<OccupationFigures> figures) => figures.Aggregate(Zero, (total, next) => total + next);
}

/// <summary>A pitch's numbers for a period, with what the screens show of the pitch.</summary>
public sealed record PitchOccupation(Guid PitchId, Guid ClubId, string Name, Sport Sport, bool IsActive, OccupationFigures Figures);

/// <summary>
/// The one occupation calculation (US14 for the owner, US27 for the admin, so both give the same number for the same club and period).
/// <list type="bullet">
/// <item>Booked hours: real elapsed time of Confirmed and NoShow bookings that overlap the period, clipped to it; cancelled ones are
/// left out (in the database, <see cref="IOccupationStore"/>).</item>
/// <item>Open hours: for each Lisbon day of the period, the bookable time of the schedule in force now: the slots
/// <see cref="SlotGenerator"/> generates times the slot duration, real elapsed time (so 25 and 23 hours on the clock-change days; a
/// closed day is 0; the minutes after the last slot that fits are not bookable, so they do not count: a 90-minute pitch open 4 hours
/// has 3). An inactive pitch has no open hours: it counts only with the bookings it already has.</item>
/// <item>Revenue: the stored prices of the same non-cancelled bookings, in full (a price is not split at the edges).</item>
/// </list>
/// Only the schedules (a few rows per pitch) are read into memory; bookings are only ever added up by the database.
/// </summary>
public sealed class OccupationService(IAppDbContext dbContext, IOccupationStore store)
{
    private sealed class PitchRow
    {
        public required Guid Id { get; init; }

        public required Guid ClubId { get; init; }

        public required string Name { get; init; }

        public required Sport Sport { get; init; }

        public required PitchStatus Status { get; init; }

        public required int SlotDurationMinutes { get; init; }

        public required string TimeZoneId { get; init; }

        public required List<PitchOpeningDay> OpeningDays { get; init; }
    }

    /// <summary>
    /// The numbers of the given pitches over the period: every active pitch, and an inactive one only if it has bookings in it.
    /// The caller decides which pitches (an owner's club, all of an owner's clubs, the whole platform).
    /// </summary>
    public async Task<IReadOnlyList<PitchOccupation>> ForPitchesAsync(IQueryable<Pitch> pitches, StatisticsPeriod period, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);
        var rows = await (
                from pitch in pitches.AsNoTracking()
                join club in dbContext.Clubs on pitch.ClubId equals club.Id
                select new PitchRow
                {
                    Id = pitch.Id,
                    ClubId = pitch.ClubId,
                    Name = pitch.Name,
                    Sport = pitch.Sport,
                    Status = pitch.Status,
                    SlotDurationMinutes = pitch.SlotDurationMinutes,
                    TimeZoneId = club.TimeZoneId,
                    OpeningDays = pitch.OpeningDays.ToList(),
                })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        // Every club is in Lisbon (T04); the period's instants follow the clubs' time zone all the same.
        var byZone = rows.GroupBy(row => row.TimeZoneId);
        var totals = new Dictionary<Guid, PitchBookingTotals>();
        foreach (var zone in byZone)
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(zone.Key);
            var found = await store.TotalsByPitchAsync(zone.Select(row => row.Id).ToList(), period.FromUtc(timeZone), period.ToUtcExclusive(timeZone), cancellationToken);
            foreach (var total in found)
            {
                totals[total.PitchId] = total;
            }
        }

        var result = new List<PitchOccupation>();
        foreach (var row in rows)
        {
            var isActive = row.Status == PitchStatus.Active;
            totals.TryGetValue(row.Id, out var booked);
            if (!isActive && booked is null)
            {
                continue;
            }

            var open = isActive ? OpenMinutes(row.OpeningDays, SlotDuration.FromMinutes(row.SlotDurationMinutes), period, TimeZoneInfo.FindSystemTimeZoneById(row.TimeZoneId)) : 0;
            result.Add(new PitchOccupation(
                row.Id,
                row.ClubId,
                row.Name,
                row.Sport,
                isActive,
                new OccupationFigures(booked?.BookedMinutes ?? 0, open, booked?.Revenue ?? 0m, booked?.ConfirmedBookings ?? 0, booked?.NoShows ?? 0)));
        }

        return result;
    }

    /// <summary>
    /// The open minutes of a schedule over the period: for each day, the slots the generator makes for it times their duration, so
    /// the same slots a player can book, in real elapsed time.
    /// </summary>
    public static long OpenMinutes(IReadOnlyCollection<PitchOpeningDay> schedule, SlotDuration duration, StatisticsPeriod period, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(period);
        var byDay = schedule.ToDictionary(day => day.Day, day => day.Hours);
        long minutes = 0;
        foreach (var date in period.Days())
        {
            if (byDay.TryGetValue(date.DayOfWeek, out var hours))
            {
                minutes += (long)SlotGenerator.Generate(date, hours, duration, timeZone).Count * duration.Minutes;
            }
        }

        return minutes;
    }
}

/// <summary>The numbers as the API gives them: hours with 2 decimals, occupation as a fraction with 4 (0.8125 is 81,25 %).</summary>
public sealed record OccupationFiguresResponse(decimal BookedHours, decimal OpenHours, decimal Occupation, decimal Revenue, int ConfirmedBookings, int NoShows)
{
    public static OccupationFiguresResponse From(OccupationFigures figures)
    {
        ArgumentNullException.ThrowIfNull(figures);
        return new(figures.BookedHours, figures.OpenHours, figures.Occupation, figures.Revenue, figures.ConfirmedBookings, figures.NoShows);
    }
}
