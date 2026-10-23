namespace SportsBooking.Domain.Scheduling;

/// <summary>
/// Opening and closing time of a pitch on one day, as local wall-clock times in the club's time zone.
/// A closing time of 00:00 means midnight at the end of that day, so 00:00–00:00 is open all day.
/// Hours that run past midnight into the next day are not supported.
/// </summary>
public sealed record OpeningHours
{
    private OpeningHours(TimeOnly opens, TimeOnly closes)
    {
        Opens = opens;
        Closes = closes;
    }

    public TimeOnly Opens { get; }

    public TimeOnly Closes { get; }

    public bool ClosesAtMidnight => Closes == TimeOnly.MinValue;

    public static bool AreValid(TimeOnly opens, TimeOnly closes) => closes == TimeOnly.MinValue || closes > opens;

    public static OpeningHours Create(TimeOnly opens, TimeOnly closes) =>
        AreValid(opens, closes)
            ? new OpeningHours(opens, closes)
            : throw new ArgumentException("Closing time must be after opening time, or 00:00 for midnight.", nameof(closes));
}
