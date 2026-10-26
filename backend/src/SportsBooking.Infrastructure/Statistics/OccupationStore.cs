using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Admin;
using SportsBooking.Application.Statistics;
using SportsBooking.Domain.Scheduling;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Statistics;

/// <summary>
/// US14, US27: the booking side of occupation, one grouped query. A booking counts when it is Confirmed or NoShow and overlaps the
/// period (half-open, as everywhere); its minutes are clipped to the period with <c>LEAST</c>/<c>GREATEST</c>; its price counts in
/// full. The filter on <c>pitch_id</c> and <c>starts_at</c> uses <c>ix_bookings_pitch_id_starts_at</c>.
/// </summary>
internal sealed class OccupationStore(AppDbContext dbContext) : IOccupationStore
{
    private sealed class Row
    {
        public Guid PitchId { get; init; }

        public long BookedMinutes { get; init; }

        public decimal Revenue { get; init; }

        public int ConfirmedBookings { get; init; }

        public int NoShows { get; init; }
    }

    public async Task<IReadOnlyList<PitchBookingTotals>> TotalsByPitchAsync(IReadOnlyCollection<Guid> pitchIds, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        if (pitchIds.Count == 0)
        {
            return [];
        }

        var ids = pitchIds.ToArray();
        var rows = await dbContext.Database
            .SqlQuery<Row>($"""
                            SELECT pitch_id,
                                   (SUM(EXTRACT(EPOCH FROM (LEAST(ends_at, {toUtc}) - GREATEST(starts_at, {fromUtc})))) / 60)::bigint AS booked_minutes,
                                   SUM(price) AS revenue,
                                   (COUNT(*) FILTER (WHERE status = 'Confirmed'))::int AS confirmed_bookings,
                                   (COUNT(*) FILTER (WHERE status = 'NoShow'))::int AS no_shows
                            FROM bookings
                            WHERE pitch_id = ANY({ids})
                              AND status IN ('Confirmed', 'NoShow')
                              AND starts_at < {toUtc} AND ends_at > {fromUtc}
                            GROUP BY pitch_id
                            """)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new PitchBookingTotals(row.PitchId, row.BookedMinutes, row.Revenue, row.ConfirmedBookings, row.NoShows)).ToList();
    }
}

/// <summary>
/// US28: new accounts by the Lisbon period of <c>registered_at</c> (<c>date_trunc</c> after <c>AT TIME ZONE</c>, ISO weeks from Monday),
/// owners and players only. The unit is one of two fixed words chosen here, never text from the request.
/// </summary>
internal sealed class RegistrationStatistics(AppDbContext dbContext) : IRegistrationStatistics
{
    private sealed class Row
    {
        public DateOnly Bucket { get; init; }

        public string Profile { get; init; } = string.Empty;

        public int Count { get; init; }
    }

    public async Task<IReadOnlyList<RegistrationCount>> CountByPeriodAsync(Granularity granularity, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var unit = granularity switch
        {
            Granularity.Week => "week",
            Granularity.Month => "month",
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Registrations are counted by week or month."),
        };
        var zone = ClubTimeZones.LisbonId;
        var rows = await dbContext.Database
            .SqlQuery<Row>($"""
                            SELECT date_trunc({unit}, u.registered_at AT TIME ZONE {zone})::date AS bucket, r.name AS profile, count(*)::int AS count
                            FROM users u
                            JOIN user_roles ur ON ur.user_id = u.id
                            JOIN roles r ON r.id = ur.role_id
                            WHERE r.name IN ('Owner', 'Player')
                              AND u.registered_at >= {fromUtc} AND u.registered_at < {toUtc}
                            GROUP BY 1, 2
                            ORDER BY 1, 2
                            """)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new RegistrationCount(row.Bucket, row.Profile, row.Count)).ToList();
    }
}
