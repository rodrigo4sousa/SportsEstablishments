using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Domain.Pitches;

/// <summary>US08 rules for a pitch's weekly hours (the time step and the default length are provisional, see US08).</summary>
public static class PitchScheduleRules
{
    /// <summary>Opening and closing times go in steps of 30 minutes (09:00, 09:30, …), with no seconds.</summary>
    public const int TimeStepMinutes = 30;

    /// <summary>The slot length of a new pitch, until the owner sets the hours.</summary>
    public static SlotDuration DefaultSlotDuration => SlotDuration.SixtyMinutes;

    /// <summary>The week as the owner sees it: Monday first.</summary>
    public static IReadOnlyList<DayOfWeek> Week { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    public static bool IsOnStep(TimeOnly time) => time.Second == 0 && time.Millisecond == 0 && time.Microsecond == 0 && time.Nanosecond == 0
        && time.Minute % TimeStepMinutes == 0;
}
