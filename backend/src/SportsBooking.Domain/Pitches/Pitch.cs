namespace SportsBooking.Domain.Pitches;

public enum PitchStatus
{
    Active,
    Inactive,
}

/// <summary>The details an owner edits (US07), already validated and normalized.</summary>
public sealed record PitchDetails(string Name, Sport Sport, decimal PricePerSlot);

/// <summary>
/// A pitch or court of a club (US07). Never deleted: an owner deactivates it (and can reactivate it) so the history
/// stays. A booking will keep the price it was made at, so changing the price only affects new bookings.
/// Slot length and opening hours are US08.
/// </summary>
public sealed class Pitch
{
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
