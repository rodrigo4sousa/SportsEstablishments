using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Application.Statistics;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Admin;

/// <summary>One club of the admin's top 10 (US27): no player, and of the owner only the name.</summary>
public sealed record TopOccupationClubResponse(
    Guid ClubId,
    string Slug,
    string ClubName,
    string OwnerName,
    string Locality,
    decimal Occupation,
    decimal BookedHours,
    decimal OpenHours);

public sealed record TopOccupationResponse(string From, string To, IReadOnlyList<TopOccupationClubResponse> Clubs);

/// <summary>
/// US27: the 10 clubs with the highest occupation in the period, with the same formula as the owner's statistics (US14,
/// <see cref="OccupationService"/>), so a club has the same number in both. Ordered by occupation, then booked hours, then the club's
/// id. A club with no open hours in the period (no active pitch, or every day closed) is left out. Every club counts, also one of a
/// deactivated owner (provisional). Only schedules are read into memory; bookings are added up by the database.
/// </summary>
public sealed class AdminTopOccupationService(IAppDbContext dbContext, OccupationService occupation, TimeProvider timeProvider)
{
    public const int Size = 10;

    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById(ClubTimeZones.LisbonId);

    public async Task<Result<TopOccupationResponse>> GetAsync(string? from, string? to, CancellationToken cancellationToken)
    {
        var period = StatisticsPeriodInput.Parse(from, to, ClubTime.Today(timeProvider, Lisbon));
        if (!period.IsSuccess)
        {
            return period.Error!;
        }

        var byClub = (await occupation.ForPitchesAsync(dbContext.Pitches, period.Value, cancellationToken))
            .GroupBy(row => row.ClubId)
            .Select(group => (ClubId: group.Key, Figures: OccupationFigures.Sum(group.Select(row => row.Figures))))
            .Where(line => line.Figures.OpenMinutes > 0)
            .ToList();

        // The ratio, the booked minutes and the id, exactly: rounding comes only when the numbers are written out.
        var top = byClub
            .OrderByDescending(line => (decimal)line.Figures.BookedMinutes / line.Figures.OpenMinutes)
            .ThenByDescending(line => line.Figures.BookedMinutes)
            .ThenBy(line => line.ClubId.ToString("D", CultureInfo.InvariantCulture), StringComparer.Ordinal)
            .Take(Size)
            .ToList();
        var ids = top.Select(line => line.ClubId).ToList();
        var clubs = await (
                from club in dbContext.Clubs.AsNoTracking()
                where ids.Contains(club.Id)
                join owner in dbContext.OwnerAccounts on club.OwnerId equals owner.Id
                select new { club.Id, club.Slug, club.Name, club.Locality, OwnerName = owner.Name })
            .ToDictionaryAsync(club => club.Id, cancellationToken);

        return new TopOccupationResponse(
            StatisticsPeriodInput.Format(period.Value.From),
            StatisticsPeriodInput.Format(period.Value.To),
            top.Select(line =>
            {
                var club = clubs[line.ClubId];
                return new TopOccupationClubResponse(club.Id, club.Slug, club.Name, club.OwnerName, club.Locality, line.Figures.Occupation, line.Figures.BookedHours, line.Figures.OpenHours);
            }).ToList());
    }
}

/// <summary>How many owners and players registered in one Lisbon period (US28), by profile.</summary>
public sealed record RegistrationCount(DateOnly Start, string Profile, int Count);

/// <summary>US28: the grouped count of new accounts by the Lisbon period of <c>registered_at</c>, in PostgreSQL (Infrastructure).</summary>
public interface IRegistrationStatistics
{
    Task<IReadOnlyList<RegistrationCount>> CountByPeriodAsync(Granularity granularity, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
}

/// <summary>One period of the registrations chart (US28): its first and last day (Lisbon) and the new owners and players.</summary>
public sealed record RegistrationsBucket(string Start, string End, int Owners, int Players);

public sealed record RegistrationsResponse(string Granularity, string From, string To, int Owners, int Players, IReadOnlyList<RegistrationsBucket> Buckets);

/// <summary>
/// US28: new owners and new players per week (from Monday) or month, by the Lisbon date of their registration, with the range rules of
/// US25 (<see cref="BookingsOverTimeRequest"/>: last 30 days by default, widened to whole periods, the same maximums, dates 2000–2100)
/// and a 0 for every period without any. Only week and month (the day is not offered: 400 on <c>granularity</c>). Every account counts,
/// deactivated or not (provisional); the admin never does.
/// </summary>
public sealed class AdminRegistrationsService(IRegistrationStatistics statistics, BookingsOverTimeRequestValidator validator, TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById(ClubTimeZones.LisbonId);

    public async Task<Result<RegistrationsResponse>> GetAsync(string? granularity, string? from, string? to, CancellationToken cancellationToken)
    {
        var request = new BookingsOverTimeRequest(granularity ?? "week", from, to, null).Normalized();
        if (request.Granularity is not ("week" or "month"))
        {
            return ServiceError.Validation(new Dictionary<string, string[]> { ["granularity"] = ["Indica a granularidade: «week» ou «month»."] });
        }

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ServiceError.Validation(validation.ToFieldErrors());
        }

        var unit = request.Granularity == "month" ? Granularity.Month : Granularity.Week;
        var range = ReportingPeriods.Range(request, ClubTime.Today(timeProvider, Lisbon), unit);
        if (!range.IsSuccess)
        {
            return range.Error!;
        }

        var (starts, effectiveTo) = range.Value;
        var fromUtc = ClubTime.ToUtc(starts[0], TimeOnly.MinValue, Lisbon);
        var toUtc = ClubTime.ToUtc(effectiveTo.AddDays(1), TimeOnly.MinValue, Lisbon);
        var counts = await statistics.CountByPeriodAsync(unit, fromUtc, toUtc, cancellationToken);
        int Of(DateOnly start, string profile) => counts.Where(count => count.Start == start && count.Profile == profile).Sum(count => count.Count);

        var buckets = starts
            .Select(start => new RegistrationsBucket(
                ReportingPeriods.Format(start),
                ReportingPeriods.Format(ReportingPeriods.End(start, unit)),
                Of(start, Domain.Accounts.Profiles.Owner),
                Of(start, Domain.Accounts.Profiles.Player)))
            .ToList();
        return new RegistrationsResponse(
            ReportingPeriods.ToCode(unit),
            ReportingPeriods.Format(starts[0]),
            ReportingPeriods.Format(effectiveTo),
            buckets.Sum(bucket => bucket.Owners),
            buckets.Sum(bucket => bucket.Players),
            buckets);
    }
}
