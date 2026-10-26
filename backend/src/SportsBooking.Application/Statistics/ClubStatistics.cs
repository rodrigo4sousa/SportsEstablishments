using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Pitches;
using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Application.Statistics;

/// <summary>One pitch's numbers in the owner's statistics (US14).</summary>
public sealed record PitchStatisticsResponse(
    Guid PitchId,
    string Name,
    string Sport,
    string Status,
    decimal BookedHours,
    decimal OpenHours,
    decimal Occupation,
    decimal Revenue,
    int ConfirmedBookings,
    int NoShows);

/// <summary>US14: one club over a period (Lisbon dates, both included), per pitch and in total.</summary>
public sealed record ClubStatisticsResponse(Guid ClubId, string ClubName, string From, string To, IReadOnlyList<PitchStatisticsResponse> Pitches, OccupationFiguresResponse Total);

/// <summary>One club's line of the owner's list (US14, «por clube»).</summary>
public sealed record OwnerClubStatisticsResponse(
    Guid ClubId,
    string ClubName,
    decimal BookedHours,
    decimal OpenHours,
    decimal Occupation,
    decimal Revenue,
    int ConfirmedBookings,
    int NoShows);

/// <summary>US14: every club of the signed-in owner over a period, one line each, and the total.</summary>
public sealed record OwnerStatisticsResponse(string From, string To, IReadOnlyList<OwnerClubStatisticsResponse> Clubs, OccupationFiguresResponse Total);

/// <summary>
/// US14: occupation and revenue for the owner, through T02: the club is checked first (404 <c>club_not_found</c>, the same for another
/// owner's club), then the pitch filter (404 <c>pitch_not_found</c> for a pitch of another club), then the dates (400). The owner's list
/// is built from <see cref="OwnedClubs.All"/>, so no other owner's club is ever in it. The numbers come from <see cref="OccupationService"/>.
/// </summary>
public sealed class ClubStatisticsService(
    OwnedClubs ownedClubs,
    OwnedPitches ownedPitches,
    IAppDbContext dbContext,
    OccupationService occupation,
    TimeProvider timeProvider)
{
    private static readonly TimeZoneInfo Lisbon = TimeZoneInfo.FindSystemTimeZoneById(ClubTimeZones.LisbonId);

    public async Task<Result<ClubStatisticsResponse>> ForClubAsync(Guid clubId, string? from, string? to, Guid? pitchId, CancellationToken cancellationToken)
    {
        var club = await ownedClubs.All().AsNoTracking().Where(candidate => candidate.Id == clubId).Select(candidate => new { candidate.Id, candidate.Name }).SingleOrDefaultAsync(cancellationToken);
        if (club is null)
        {
            return ClubErrors.NotFound;
        }

        var pitches = ownedPitches.Of(clubId);
        if (pitchId is { } onePitch)
        {
            if (!await pitches.AnyAsync(pitch => pitch.Id == onePitch, cancellationToken))
            {
                return PitchErrors.NotFound;
            }

            pitches = pitches.Where(pitch => pitch.Id == onePitch);
        }

        var period = StatisticsPeriodInput.Parse(from, to, ClubTime.Today(timeProvider, Lisbon));
        if (!period.IsSuccess)
        {
            return period.Error!;
        }

        var rows = (await occupation.ForPitchesAsync(pitches, period.Value, cancellationToken))
            .OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.PitchId)
            .ToList();
        return new ClubStatisticsResponse(
            club.Id,
            club.Name,
            StatisticsPeriodInput.Format(period.Value.From),
            StatisticsPeriodInput.Format(period.Value.To),
            rows.Select(row => new PitchStatisticsResponse(
                row.PitchId,
                row.Name,
                Sports.ToCode(row.Sport),
                row.IsActive ? "active" : "inactive",
                row.Figures.BookedHours,
                row.Figures.OpenHours,
                row.Figures.Occupation,
                row.Figures.Revenue,
                row.Figures.ConfirmedBookings,
                row.Figures.NoShows)).ToList(),
            OccupationFiguresResponse.From(OccupationFigures.Sum(rows.Select(row => row.Figures))));
    }

    public async Task<Result<OwnerStatisticsResponse>> ForOwnerAsync(string? from, string? to, CancellationToken cancellationToken)
    {
        var period = StatisticsPeriodInput.Parse(from, to, ClubTime.Today(timeProvider, Lisbon));
        if (!period.IsSuccess)
        {
            return period.Error!;
        }

        var clubs = await ownedClubs.All().AsNoTracking().Select(club => new { club.Id, club.Name }).ToListAsync(cancellationToken);
        var owned = ownedClubs.All();
        var pitches = dbContext.Pitches.Where(pitch => owned.Any(club => club.Id == pitch.ClubId));
        var byClub = (await occupation.ForPitchesAsync(pitches, period.Value, cancellationToken))
            .GroupBy(row => row.ClubId)
            .ToDictionary(group => group.Key, group => OccupationFigures.Sum(group.Select(row => row.Figures)));

        // Every club of the owner is listed, also one with nothing open or booked.
        var lines = clubs
            .Select(club => (club, figures: byClub.GetValueOrDefault(club.Id, OccupationFigures.Zero)))
            .OrderBy(line => line.club.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(line => line.club.Id)
            .ToList();
        return new OwnerStatisticsResponse(
            StatisticsPeriodInput.Format(period.Value.From),
            StatisticsPeriodInput.Format(period.Value.To),
            lines.Select(line => new OwnerClubStatisticsResponse(
                line.club.Id,
                line.club.Name,
                line.figures.BookedHours,
                line.figures.OpenHours,
                line.figures.Occupation,
                line.figures.Revenue,
                line.figures.ConfirmedBookings,
                line.figures.NoShows)).ToList(),
            OccupationFiguresResponse.From(OccupationFigures.Sum(lines.Select(line => line.figures))));
    }
}
