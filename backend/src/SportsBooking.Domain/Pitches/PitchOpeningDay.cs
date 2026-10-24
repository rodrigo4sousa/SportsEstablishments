using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Domain.Pitches;

/// <summary>
/// The hours of a pitch on one weekday (US08), as local wall-clock times in the club's time zone (T04). A weekday
/// without one of these is closed. Changed only through <see cref="Pitch.ChangeSchedule"/>.
/// </summary>
public sealed class PitchOpeningDay
{
    private PitchOpeningDay()
    {
    }

    internal PitchOpeningDay(DayOfWeek day, OpeningHours hours)
    {
        Day = day;
        Set(hours);
    }

    public DayOfWeek Day { get; private set; }

    public TimeOnly Opens { get; private set; }

    /// <summary>00:00 means midnight at the end of the day (<see cref="OpeningHours"/>).</summary>
    public TimeOnly Closes { get; private set; }

    public OpeningHours Hours => OpeningHours.Create(Opens, Closes);

    internal void Set(OpeningHours hours)
    {
        ArgumentNullException.ThrowIfNull(hours);
        Opens = hours.Opens;
        Closes = hours.Closes;
    }
}
