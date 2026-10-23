using SportsBooking.Domain.Scheduling;

namespace SportsBooking.Domain.Clubs;

/// <summary>The details an owner edits (US06), already validated and normalized.</summary>
public sealed record ClubDetails(string Name, string Address, string Locality, string Phone, string ContactEmail);

/// <summary>
/// A club (US06). It belongs to exactly one owner, has a public slug that never changes, and uses the
/// Europe/Lisbon time zone (T04). Changes go through the methods below.
/// </summary>
public sealed class Club
{
    private Club()
    {
    }

    /// <summary>Public id (GUID v7), used in the owner area: <c>/manage/clubs/{id}</c>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The owner's account id. Set once at creation; a club never changes owner.</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>Public address: <c>/clubs/{slug}</c>. Unique across the platform; never changed by a rename.</summary>
    public string Slug { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Address { get; private set; } = string.Empty;

    public string Locality { get; private set; } = string.Empty;

    public string Phone { get; private set; } = string.Empty;

    public string ContactEmail { get; private set; } = string.Empty;

    /// <summary>
    /// How many hours before the start a player may still cancel. It is read at the moment of a cancellation and
    /// never copied into bookings, so a change applies to every cancellation made after it (US06, US19).
    /// </summary>
    public int CancellationWindowHours { get; private set; }

    /// <summary>IANA time zone of the club's opening hours. Always Europe/Lisbon for now (T04); not editable.</summary>
    public string TimeZoneId { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public TimeSpan CancellationWindow => TimeSpan.FromHours(CancellationWindowHours);

    public static Club Create(Guid ownerId, string slug, ClubDetails details, int cancellationWindowHours, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        EnsureValidWindow(cancellationWindowHours);
        var club = new Club
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Slug = slug,
            CancellationWindowHours = cancellationWindowHours,
            TimeZoneId = ClubTimeZones.LisbonId,
            CreatedAt = EnsureUtc(now),
            UpdatedAt = now,
        };
        club.Apply(details);
        return club;
    }

    /// <summary>Replaces the editable details. The slug stays as it was, even when the name changes.</summary>
    public void UpdateDetails(ClubDetails details, DateTime now)
    {
        Apply(details);
        UpdatedAt = EnsureUtc(now);
    }

    /// <summary>Applies to cancellations made from now on; bookings never keep a copy of the window.</summary>
    public void ChangeCancellationWindow(int hours, DateTime now)
    {
        EnsureValidWindow(hours);
        CancellationWindowHours = hours;
        UpdatedAt = EnsureUtc(now);
    }

    private void Apply(ClubDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        Name = details.Name;
        Address = details.Address;
        Locality = details.Locality;
        Phone = details.Phone;
        ContactEmail = details.ContactEmail;
    }

    private static void EnsureValidWindow(int hours)
    {
        if (!ClubRules.IsValidCancellationWindow(hours))
        {
            throw new ArgumentOutOfRangeException(nameof(hours), hours, "The cancellation window is outside the allowed range.");
        }
    }

    private static DateTime EnsureUtc(DateTime now) =>
        now.Kind == DateTimeKind.Utc ? now : throw new ArgumentException("Times must be UTC.", nameof(now));
}
