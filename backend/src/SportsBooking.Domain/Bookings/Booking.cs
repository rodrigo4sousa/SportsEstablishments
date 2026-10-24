namespace SportsBooking.Domain.Bookings;

/// <summary>The states a booking goes through. Bookings are never deleted; they change state.</summary>
public enum BookingStatus
{
    Confirmed,
    CancelledByPlayer,
    CancelledByClub,
    NoShow,
}

/// <summary>What happened when a player asked to cancel a booking (US19).</summary>
public enum CancellationOutcome
{
    /// <summary>The booking is now <see cref="BookingStatus.CancelledByPlayer"/>.</summary>
    Cancelled,

    /// <summary>Only a confirmed booking can be cancelled: it was cancelled already, or it was marked as a no-show.</summary>
    NotConfirmed,

    /// <summary>The club's cancellation window has closed (or never left room): see <see cref="Booking.CancellationDeadline"/>.</summary>
    OutsideWindow,
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

    /// <summary>
    /// The last moment before which a player may cancel: the start minus the club's cancellation window. The window is
    /// the club's value at the moment of cancelling and is never kept in the booking (US06, US19).
    /// </summary>
    public DateTime CancellationDeadline(TimeSpan window) => StartsAt - EnsureWindow(window);

    /// <summary>
    /// True while the booking is confirmed and <paramref name="now"/> is strictly before the deadline (provisional: at
    /// the deadline itself it is already too late).
    /// </summary>
    public bool CanBeCancelledByPlayer(TimeSpan window, DateTime now) => IsConfirmed && EnsureUtc(now) < CancellationDeadline(window);

    /// <summary>
    /// A player cancels their booking (US19). Only a confirmed booking can be cancelled, and only before the deadline
    /// (<see cref="CanBeCancelledByPlayer"/>). The booking is kept as <see cref="BookingStatus.CancelledByPlayer"/>, never
    /// deleted, and stops counting for the overlap constraint, so the slot is free again. Nothing changes unless the
    /// outcome is <see cref="CancellationOutcome.Cancelled"/>.
    /// </summary>
    public CancellationOutcome CancelByPlayer(TimeSpan window, DateTime now)
    {
        if (!IsConfirmed)
        {
            return CancellationOutcome.NotConfirmed;
        }

        if (!CanBeCancelledByPlayer(window, now))
        {
            return CancellationOutcome.OutsideWindow;
        }

        Status = BookingStatus.CancelledByPlayer;
        return CancellationOutcome.Cancelled;
    }

    /// <summary>Half-open overlap, the same rule as blocks and as the database constraint (<c>tstzrange</c> is <c>[)</c>).</summary>
    public bool Overlaps(DateTime startsAt, DateTime endsAt) => StartsAt < endsAt && startsAt < EndsAt;

    private static TimeSpan EnsureWindow(TimeSpan window) =>
        window >= TimeSpan.Zero ? window : throw new ArgumentOutOfRangeException(nameof(window), window, "A cancellation window cannot be negative.");

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("Times must be UTC.", nameof(value));
}
