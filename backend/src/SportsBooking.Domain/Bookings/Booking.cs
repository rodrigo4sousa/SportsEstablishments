namespace SportsBooking.Domain.Bookings;

/// <summary>The states a booking goes through. Bookings are never deleted; they change state.</summary>
public enum BookingStatus
{
    Confirmed,
    CancelledByPlayer,
    CancelledByClub,
    NoShow,
}

/// <summary>The stable codes of <see cref="BookingStatus"/> in the API (the UI shows the pt-PT labels).</summary>
public static class BookingStatuses
{
    public static string ToCode(BookingStatus status) => status switch
    {
        BookingStatus.Confirmed => "confirmed",
        BookingStatus.CancelledByPlayer => "cancelled_by_player",
        BookingStatus.CancelledByClub => "cancelled_by_club",
        BookingStatus.NoShow => "no_show",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown booking status."),
    };
}

/// <summary>
/// A booking of one slot of a pitch (US18), UTC instants [StartsAt, EndsAt). It keeps the price of the moment it was
/// made. No two confirmed bookings of a pitch overlap: the database refuses it (T03, exclusion constraint).
/// Today a booking always belongs to a player; US13 adds bookings an owner makes for a customer without an account
/// (another factory, customer name and phone) without touching the overlap rule, which only looks at the pitch,
/// the period and the status.
/// </summary>
public sealed class Booking
{
    private Booking()
    {
    }

    /// <summary>Public id (GUID v7).</summary>
    public Guid Id { get; private set; }

    public Guid PitchId { get; private set; }

    /// <summary>The player's account. Nullable for US13 (customer without an account); always set for now.</summary>
    public Guid? PlayerId { get; private set; }

    public DateTime StartsAt { get; private set; }

    public DateTime EndsAt { get; private set; }

    /// <summary>The pitch's price per slot when the booking was made (<c>numeric(10,2)</c>); a later price change never alters it.</summary>
    public decimal Price { get; private set; }

    public BookingStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public bool IsConfirmed => Status == BookingStatus.Confirmed;

    /// <summary>A confirmed booking by a player (US18).</summary>
    public static Booking ForPlayer(Guid pitchId, Guid playerId, DateTime startsAt, DateTime endsAt, decimal price, DateTime now)
    {
        if (playerId == Guid.Empty)
        {
            throw new ArgumentException("A player booking needs the player.", nameof(playerId));
        }

        if (EnsureUtc(endsAt) <= EnsureUtc(startsAt))
        {
            throw new ArgumentException("A booking must end after it starts.", nameof(endsAt));
        }

        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), price, "A booking has a price above zero.");
        }

        return new Booking
        {
            Id = Guid.CreateVersion7(),
            PitchId = pitchId,
            PlayerId = playerId,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Price = price,
            Status = BookingStatus.Confirmed,
            CreatedAt = EnsureUtc(now),
        };
    }

    /// <summary>Half-open overlap, the same rule as blocks and as the database constraint (<c>tstzrange</c> is <c>[)</c>).</summary>
    public bool Overlaps(DateTime startsAt, DateTime endsAt) => StartsAt < endsAt && startsAt < EndsAt;

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("Times must be UTC.", nameof(value));
}
