namespace SportsBooking.Domain.Scheduling;

/// <summary>
/// Conversions between a club's local wall-clock time and UTC instants.
/// Instants are always stored as UTC; opening hours are local times in the club's time zone.
/// </summary>
public static class ClubTime
{
    /// <summary>
    /// Converts a local date and time in <paramref name="timeZone"/> to a UTC instant.
    /// <list type="bullet">
    /// <item>A time that does not exist (clocks go forward) is shifted forward by the gap:
    /// in Lisbon on 29 March 2026, 01:30 becomes 02:30 WEST (01:30 UTC).</item>
    /// <item>A time that happens twice (clocks go back) resolves to its first occurrence:
    /// in Lisbon on 25 October 2026, 01:30 is 01:30 WEST (00:30 UTC).</item>
    /// </list>
    /// </summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(local))
        {
            // The offset in force before the gap lands the time the same distance after the jump.
            var offsetBeforeGap = timeZone.GetUtcOffset(local.AddDays(-1));
            return DateTime.SpecifyKind(local - offsetBeforeGap, DateTimeKind.Utc);
        }

        if (timeZone.IsAmbiguousTime(local))
        {
            // The larger offset gives the earlier instant.
            var firstOccurrenceOffset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return DateTime.SpecifyKind(local - firstOccurrenceOffset, DateTimeKind.Utc);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    /// <summary>Converts a UTC instant to the local wall-clock time in <paramref name="timeZone"/>.</summary>
    public static DateTime ToLocal(DateTime utc, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The instant must be a UTC DateTime.", nameof(utc));
        }

        return TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
    }

    /// <summary>The current calendar date in <paramref name="timeZone"/>, which can differ from the UTC date around midnight.</summary>
    public static DateOnly Today(TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(timeZone);

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);
    }
}
