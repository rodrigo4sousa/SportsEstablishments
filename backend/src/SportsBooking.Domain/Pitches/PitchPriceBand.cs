using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Domain.Pitches;

/// <summary>The fields of a band, already validated and normalized (US10).</summary>
public sealed record PriceBandDetails(IReadOnlySet<DayOfWeek> Days, TimeOnly Starts, TimeOnly Ends, decimal Price);

/// <summary>
/// US10: a price for the slots of a pitch that start inside a time range on some weekdays, in the club's local time (T04).
/// <see cref="Ends"/> of 00:00 is midnight at the end of the day; bands do not run past midnight, like opening hours. A band is
/// configuration, not history (a booking keeps its own price), so it is changed and removed for real. Changed only through
/// <see cref="Pitch.TryAddPriceBand"/>, <see cref="Pitch.TryChangePriceBand"/> and <see cref="Pitch.RemovePriceBand"/>.
/// </summary>
public sealed class PitchPriceBand
{
    private PitchPriceBand()
    {
    }

    /// <summary>Public id (GUID v7).</summary>
    public Guid Id { get; private set; }

    public Guid PitchId { get; private set; }

    /// <summary>One bit per <see cref="DayOfWeek"/> (Sunday is bit 0), 1..127; stored as is.</summary>
    public int DaysMask { get; private set; }

    public TimeOnly Starts { get; private set; }

    /// <summary>00:00 means midnight at the end of the day.</summary>
    public TimeOnly Ends { get; private set; }

    /// <summary>Euros for one slot starting inside the band (<c>numeric(10,2)</c>).</summary>
    public decimal Price { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public IReadOnlySet<DayOfWeek> Days => MaskToDays(DaysMask);

    internal static PitchPriceBand Create(Guid pitchId, PriceBandDetails details, DateTime now)
    {
        var band = new PitchPriceBand { Id = Guid.CreateVersion7(), PitchId = pitchId, CreatedAt = now };
        band.Apply(details, now);
        return band;
    }

    internal void Apply(PriceBandDetails details, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (details.Days.Count == 0)
        {
            throw new ArgumentException("A band needs at least one weekday.", nameof(details));
        }

        if (!PitchScheduleRules.IsOnStep(details.Starts) || !PitchScheduleRules.IsOnStep(details.Ends))
        {
            throw new ArgumentException("Band times must be in steps of 30 minutes.", nameof(details));
        }

        if (!OpeningHours.AreValid(details.Starts, details.Ends))
        {
            throw new ArgumentException("A band must end after it starts, or at 00:00 for midnight.", nameof(details));
        }

        if (!PriceBandRules.IsValidPrice(details.Price))
        {
            throw new ArgumentOutOfRangeException(nameof(details), details.Price, "The price is outside the allowed range.");
        }

        DaysMask = DaysToMask(details.Days);
        Starts = details.Starts;
        Ends = details.Ends;
        Price = details.Price;
        UpdatedAt = now;
    }

    /// <summary>True when a slot starting at this local weekday and time is inside the band: start included, end excluded.</summary>
    public bool Covers(DayOfWeek day, TimeOnly time) =>
        (DaysMask & Bit(day)) != 0 && time >= Starts && time.ToTimeSpan() < EndOfRange(Ends);

    /// <summary>True when the two bands share a weekday and their time ranges intersect; touching (one ends where the other starts) does not.</summary>
    public bool Overlaps(PitchPriceBand other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Overlaps(other.DaysMask, other.Starts, other.Ends);
    }

    /// <summary><see cref="Overlaps(PitchPriceBand)"/> for a band that is not stored yet.</summary>
    public bool Overlaps(PriceBandDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return Overlaps(DaysToMask(details.Days), details.Starts, details.Ends);
    }

    private bool Overlaps(int otherDays, TimeOnly otherStarts, TimeOnly otherEnds) =>
        (DaysMask & otherDays) != 0 && Starts.ToTimeSpan() < EndOfRange(otherEnds) && otherStarts.ToTimeSpan() < EndOfRange(Ends);

    // The end as time since the start of the day, with midnight at the end of the day as 24 h.
    private static TimeSpan EndOfRange(TimeOnly ends) => ends == TimeOnly.MinValue ? TimeSpan.FromDays(1) : ends.ToTimeSpan();

    internal static int Bit(DayOfWeek day) => 1 << (int)day;

    internal static int DaysToMask(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (mask, day) => mask | Bit(day));

    internal static HashSet<DayOfWeek> MaskToDays(int mask) =>
        Enum.GetValues<DayOfWeek>().Where(day => (mask & Bit(day)) != 0).ToHashSet();
}
