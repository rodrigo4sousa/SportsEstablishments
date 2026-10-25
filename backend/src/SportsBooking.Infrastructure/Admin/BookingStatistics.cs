using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Admin;
using SportsBooking.Domain.Scheduling;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Admin;

/// <summary>
/// US25: the grouped count of bookings by the Lisbon period they were created in, in PostgreSQL:
/// <c>date_trunc(unit, created_at AT TIME ZONE 'Europe/Lisbon')</c> turns the instant into the club's local time before
/// cutting it, so a booking made at 00:30 on 25 October belongs to the 25th, and the week starts on Monday (ISO).
/// The cancelled ones (by the player or by the club) are left out unless asked for. The unit is one of three fixed
/// words chosen here, never text from the request.
/// </summary>
internal sealed class BookingStatistics(AppDbContext dbContext) : IBookingStatistics
{
    private sealed class Row
    {
        public DateOnly Bucket { get; init; }

        public int Count { get; init; }
    }

    public async Task<IReadOnlyList<PeriodCount>> CountCreatedByPeriodAsync(
        Granularity granularity,
        DateTime fromUtc,
        DateTime toUtc,
        bool includeCancelled,
        CancellationToken cancellationToken)
    {
        var unit = granularity switch
        {
            Granularity.Day => "day",
            Granularity.Week => "week",
            Granularity.Month => "month",
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Unknown granularity."),
        };
        var zone = ClubTimeZones.LisbonId;

        var rows = await dbContext.Database
            .SqlQuery<Row>($"""
                            SELECT date_trunc({unit}, created_at AT TIME ZONE {zone})::date AS "Bucket", count(*)::int AS "Count"
                            FROM bookings
                            WHERE created_at >= {fromUtc} AND created_at < {toUtc}
                              AND ({includeCancelled} OR status NOT IN ('CancelledByPlayer', 'CancelledByClub'))
                            GROUP BY 1
                            ORDER BY 1
                            """)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new PeriodCount(row.Bucket, row.Count)).ToList();
    }
}
