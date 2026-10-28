using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Domain.Pitches;

public enum PitchStatus
{
    Active,
    Inactive,
}

/// <summary>What happened when a band was added or changed (US10).</summary>
public enum PriceBandOutcome
{
    Done,
    Overlaps,
    LimitReached,
}

/// <summary>The details an owner edits (US07), already validated and normalized.</summary>
public sealed record PitchDetails(string Name, Sport Sport, decimal PricePerSlot);

/// <summary>
/// A pitch or court of a club (US07). Never deleted: an owner deactivates it (and can reactivate it) so the history
/// stays. A booking will keep the price it was made at, so changing the price only affects new bookings.
/// Its slot length and weekly hours (US08) say which slots exist; a new pitch is closed every day until they are set.
/// </summary>
public sealed class Pitch
{
    private readonly List<PitchOpeningDay> _openingDays = [];
    private readonly List<PitchPriceBand> _priceBands = [];

    private Pitch()
    {
    }

    /// <summary>Public id (GUID v7).</summary>
    public Guid Id { get; private set; }

    public Guid ClubId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary><see cref="PitchRules.NameKey"/> of the name; unique per club.</summary>
    public string NameKey { get; private set; } = string.Empty;

    public Sport Sport { get; private set; }

    /// <summary>Euros for one slot (<c>numeric(10,2)</c>).</summary>
    public decimal PricePerSlot { get; private set; }

    public PitchStatus Status { get; private set; }

    /// <summary>30, 60 or 90 (US08).</summary>
    public int SlotDurationMinutes { get; private set; }

    public SlotDuration SlotDuration => SlotDuration.FromMinutes(SlotDurationMinutes);

    /// <summary>The open weekdays, at most one interval each; a weekday that is not here is closed.</summary>
    public IReadOnlyList<PitchOpeningDay> OpeningDays => _openingDays;

    /// <summary>The price bands (US10): none or more, never overlapping. A slot outside every band costs <see cref="PricePerSlot"/>.</summary>
    public IReadOnlyList<PitchPriceBand> PriceBands => _priceBands;

    public bool HasPriceBands => _priceBands.Count > 0;

    public bool IsActive => Status == PitchStatus.Active;

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>A new pitch starts active.</summary>
    public static Pitch Create(Guid clubId, PitchDetails details, DateTime now)
    {
        var pitch = new Pitch
        {
            Id = Guid.CreateVersion7(),
            ClubId = clubId,
            Status = PitchStatus.Active,
            SlotDurationMinutes = PitchScheduleRules.DefaultSlotDuration.Minutes,
            CreatedAt = EnsureUtc(now),
            UpdatedAt = now,
        };
        pitch.Apply(details);
        return pitch;
    }

    /// <summary>Replaces name, sport and price. A new price applies to bookings made from now on.</summary>
    public void UpdateDetails(PitchDetails details, DateTime now)
    {
        Apply(details);
        UpdatedAt = EnsureUtc(now);
    }

    /// <summary>Idempotent. The caller has already checked that no future confirmed booking exists (US18).</summary>
    public void Deactivate(DateTime now)
    {
        Status = PitchStatus.Inactive;
        UpdatedAt = EnsureUtc(now);
    }

    /// <summary>Idempotent.</summary>
    public void Reactivate(DateTime now)
    {
        Status = PitchStatus.Active;
        UpdatedAt = EnsureUtc(now);
    }

    /// <summary>The hours on <paramref name="day"/>, or null when the pitch is closed that day.</summary>
    public OpeningHours? HoursOn(DayOfWeek day) => _openingDays.SingleOrDefault(openingDay => openingDay.Day == day)?.Hours;

    /// <summary>
    /// Replaces the slot length and the whole week: <paramref name="week"/> has every weekday, with its hours or
    /// null for closed. Existing bookings are never touched; they keep their own start and end (US08, US17).
    /// </summary>
    public void ChangeSchedule(SlotDuration duration, IReadOnlyDictionary<DayOfWeek, OpeningHours?> week, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(week);
        if (!SlotDuration.IsSupported(duration.Minutes))
        {
            throw new ArgumentOutOfRangeException(nameof(duration), duration.Minutes, "Slot duration must be 30, 60 or 90 minutes.");
        }

        if (week.Count != 7 || PitchScheduleRules.Week.Any(day => !week.ContainsKey(day)))
        {
            throw new ArgumentException("The schedule must have every day of the week.", nameof(week));
        }

        foreach (var (day, hours) in week)
        {
            if (hours is not null && (!PitchScheduleRules.IsOnStep(hours.Opens) || !PitchScheduleRules.IsOnStep(hours.Closes)))
            {
                throw new ArgumentException("Opening hours must be in steps of 30 minutes.", nameof(week));
            }

            // Changed in place, so each weekday keeps its row.
            var existing = _openingDays.SingleOrDefault(openingDay => openingDay.Day == day);
            if (hours is null)
            {
                if (existing is not null)
                {
                    _openingDays.Remove(existing);
                }
            }
            else if (existing is null)
            {
                _openingDays.Add(new PitchOpeningDay(day, hours));
            }
            else
            {
                existing.Set(hours);
            }
        }

        SlotDurationMinutes = duration.Minutes;
        UpdatedAt = EnsureUtc(now);
    }

    /// <summary>The slots of one local date, from its weekday's hours (none when closed), as UTC instants (T04).</summary>
    public IReadOnlyList<TimeSlot> SlotsOn(DateOnly date, TimeZoneInfo timeZone) =>
        HoursOn(date.DayOfWeek) is { } hours ? SlotGenerator.Generate(date, hours, SlotDuration, timeZone) : [];

    /// <summary>
    /// The generated slot that starts exactly at <paramref name="startsAtUtc"/>, or null (US18): a closed day, a time
    /// outside the hours or a time off the grid is not a slot. The slot belongs to the local date of its start, so
    /// the end always comes from the schedule, never from the request.
    /// </summary>
    public TimeSlot? SlotStartingAt(DateTime startsAtUtc, TimeZoneInfo timeZone)
    {
        var localDate = DateOnly.FromDateTime(ClubTime.ToLocal(startsAtUtc, timeZone));
        return SlotsOn(localDate, timeZone).SingleOrDefault(slot => slot.StartsAtUtc == startsAtUtc);
    }

    /// <summary>
    /// The price of the slot that starts at <paramref name="startsAtUtc"/> (US10): the band that contains its local start
    /// time on its local weekday, otherwise <see cref="PricePerSlot"/>. A slot that runs past the end of a band keeps the
    /// price of the band where it starts (provisional). Local time, so the repeated hour of 25 October is priced by the
    /// clock on the wall, in summer and in winter alike (T04). The one place a slot's price is decided.
    /// </summary>
    public decimal PriceForSlot(DateTime startsAtUtc, TimeZoneInfo timeZone)
    {
        var local = ClubTime.ToLocal(startsAtUtc, timeZone);
        var time = TimeOnly.FromDateTime(local);
        var band = _priceBands.FirstOrDefault(candidate => candidate.Covers(local.DayOfWeek, time));
        return band?.Price ?? PricePerSlot;
    }

    /// <summary>Adds a band unless the pitch already has <see cref="PriceBandRules.MaximumPerPitch"/> or the band overlaps another one.</summary>
    public PriceBandOutcome TryAddPriceBand(PriceBandDetails details, DateTime now, out PitchPriceBand? band)
    {
        ArgumentNullException.ThrowIfNull(details);
        band = null;
        if (_priceBands.Count >= PriceBandRules.MaximumPerPitch)
        {
            return PriceBandOutcome.LimitReached;
        }

        if (_priceBands.Any(existing => existing.Overlaps(details)))
        {
            return PriceBandOutcome.Overlaps;
        }

        band = PitchPriceBand.Create(Id, details, EnsureUtc(now));
        _priceBands.Add(band);
        return PriceBandOutcome.Done;
    }

    /// <summary>Replaces the fields of one of the pitch's bands, unless the new ones overlap another band (not itself).</summary>
    public PriceBandOutcome TryChangePriceBand(PitchPriceBand band, PriceBandDetails details, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(band);
        ArgumentNullException.ThrowIfNull(details);
        if (!_priceBands.Contains(band))
        {
            throw new ArgumentException("The band does not belong to this pitch.", nameof(band));
        }

        if (_priceBands.Any(existing => existing != band && existing.Overlaps(details)))
        {
            return PriceBandOutcome.Overlaps;
        }

        band.Apply(details, EnsureUtc(now));
        return PriceBandOutcome.Done;
    }

    /// <summary>Removes a band for real: bands are configuration, and bookings keep their own price.</summary>
    public void RemovePriceBand(PitchPriceBand band)
    {
        ArgumentNullException.ThrowIfNull(band);
        _priceBands.Remove(band);
    }

    private void Apply(PitchDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (!PitchRules.IsValidPrice(details.PricePerSlot))
        {
            throw new ArgumentOutOfRangeException(nameof(details), details.PricePerSlot, "The price is outside the allowed range.");
        }

        Name = details.Name;
        NameKey = PitchRules.NameKey(details.Name);
        Sport = details.Sport;
        PricePerSlot = details.PricePerSlot;
    }

    private static DateTime EnsureUtc(DateTime now) =>
        now.Kind == DateTimeKind.Utc ? now : throw new ArgumentException("Times must be UTC.", nameof(now));
}
