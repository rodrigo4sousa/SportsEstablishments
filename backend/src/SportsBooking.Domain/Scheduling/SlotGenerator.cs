namespace SportsBooking.Domain.Scheduling;

public static class SlotGenerator
{
    /// <summary>
    /// Generates the slots of one local day: consecutive blocks of <paramref name="duration"/> starting at the
    /// opening time; a block that would end after the closing time is not generated.
    /// Blocks are measured in elapsed time between UTC instants, so every slot lasts exactly its duration even
    /// on daylight saving days (one hour fewer in March, one hour more in October). Local times that do not exist
    /// or happen twice are resolved by <see cref="ClubTime.ToUtc"/>.
    /// </summary>
    public static IReadOnlyList<TimeSlot> Generate(
        DateOnly date,
        OpeningHours hours,
        SlotDuration duration,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(hours);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (!SlotDuration.IsSupported(duration.Minutes))
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration.Minutes, "Slot duration must be 30, 60 or 90 minutes.");
        }

        var (opensAtUtc, closesAtUtc) = hours.UtcIntervalOn(date, timeZone);

        var length = duration.Length;
        var slots = new List<TimeSlot>();

        for (var start = opensAtUtc; start + length <= closesAtUtc; start += length)
        {
            slots.Add(new TimeSlot(start, start + length));
        }

        return slots;
    }
}
