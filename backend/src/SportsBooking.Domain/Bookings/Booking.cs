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

/// <summary>What happened when the club's owner asked to cancel a booking (US12).</summary>
public enum ClubCancellationOutcome
{
    /// <summary>The booking is now <see cref="BookingStatus.CancelledByClub"/>.</summary>
    Cancelled,

    /// <summary>Only a confirmed booking can be cancelled: it was cancelled already, or it was marked as a no-show.</summary>
    NotConfirmed,

    /// <summary>The booking has started: it is history now, and stays as it is.</summary>
    AlreadyStarted,
}

/// <summary>What happened when the club's owner marked a booking as a no-show (US15).</summary>
public enum NoShowOutcome
{
    /// <summary>The booking is now <see cref="BookingStatus.NoShow"/>.</summary>
    Marked,

    /// <summary>Only a confirmed booking can be marked: it was cancelled, or it already is a no-show.</summary>
    NotConfirmed,

    /// <summary>The booking has not ended yet (it ends at <see cref="Booking.EndsAt"/>, which counts as ended).</summary>
    NotEnded,
}

/// <summary>Limits of what is stored with a booking.</summary>
public static class BookingRules
{
    /// <summary>The longest reason an owner gives for cancelling a booking (provisional, as for a block's reason).</summary>
    public const int CancellationReasonMaximumLength = 200;
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
/// A booking belongs either to a player (US18) or to a customer without an account whom the club's owner booked for
/// (US13, a name and a phone), never to both and never to neither (<c>ck_bookings_player_or_customer</c>). The overlap
/// rule only looks at the pitch, the period and the status, so both kinds compete for the same slots.
/// </summary>
public sealed class Booking
{
    private Booking()
    {
    }

    /// <summary>Public id (GUID v7).</summary>
    public Guid Id { get; private set; }

    public Guid PitchId { get; private set; }

    /// <summary>The player's account; null for a booking the club made for a customer (US13).</summary>
    public Guid? PlayerId { get; private set; }

    /// <summary>The customer's name (US13); null for a player's booking.</summary>
    public string? CustomerName { get; private set; }

    /// <summary>The customer's phone as the owner typed it, spaces removed (US13); null for a player's booking.</summary>
    public string? CustomerPhone { get; private set; }

    /// <summary>True for a booking the club made for a customer without an account (US13): it is in no player's list.</summary>
    public bool IsForCustomer => PlayerId is null;

    public DateTime StartsAt { get; private set; }

    public DateTime EndsAt { get; private set; }

    /// <summary>The pitch's price per slot when the booking was made (<c>numeric(10,2)</c>); a later price change never alters it.</summary>
    public decimal Price { get; private set; }

    public BookingStatus Status { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>When the booking was cancelled (by the player or by the club); null while it is not cancelled.</summary>
    public DateTime? CancelledAt { get; private set; }

    /// <summary>Why the club cancelled it (US12); null unless <see cref="BookingStatus.CancelledByClub"/>.</summary>
    public string? CancellationReason { get; private set; }

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

    /// <summary>A confirmed booking the club's owner makes for a customer who has no account (US13).</summary>
    public static Booking ForCustomer(Guid pitchId, string customerName, string customerPhone, DateTime startsAt, DateTime endsAt, decimal price, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(customerName) || string.IsNullOrWhiteSpace(customerPhone))
        {
            throw new ArgumentException("A customer booking needs the customer's name and phone.", nameof(customerName));
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
            CustomerName = customerName.Trim(),
            CustomerPhone = customerPhone.Trim(),
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
    public DateTime CancellationDeadline(TimeSpan window) => CancellationDeadline(StartsAt, window);

    /// <summary>The same deadline for a booking that is not loaded as an entity (the list of my bookings).</summary>
    public static DateTime CancellationDeadline(DateTime startsAt, TimeSpan window) => EnsureUtc(startsAt) - EnsureWindow(window);

    /// <summary>
    /// True while the booking is confirmed and <paramref name="now"/> is strictly before the deadline (provisional: at
    /// the deadline itself it is already too late).
    /// </summary>
    public bool CanBeCancelledByPlayer(TimeSpan window, DateTime now) => CanBeCancelledByPlayer(Status, StartsAt, window, now);

    /// <summary>The same rule for a booking that is not loaded as an entity; the entity method above uses it.</summary>
    public static bool CanBeCancelledByPlayer(BookingStatus status, DateTime startsAt, TimeSpan window, DateTime now) =>
        status == BookingStatus.Confirmed && EnsureUtc(now) < CancellationDeadline(startsAt, window);

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
        CancelledAt = EnsureUtc(now);
        return CancellationOutcome.Cancelled;
    }

    /// <summary>
    /// The player did not come (US15 builds the owner's action on this): only a confirmed booking whose end has passed.
    /// Returns false, and changes nothing, for any other booking.
    /// </summary>
    public bool MarkNoShow(DateTime now) => RecordNoShow(now) == NoShowOutcome.Marked;

    /// <summary>
    /// The same rule with the reason it was refused (US15): not confirmed comes before not ended. Nothing changes unless the
    /// outcome is <see cref="NoShowOutcome.Marked"/>. A no-show can be a player's or a customer's booking (US13).
    /// </summary>
    public NoShowOutcome RecordNoShow(DateTime now)
    {
        if (!IsConfirmed)
        {
            return NoShowOutcome.NotConfirmed;
        }

        if (EnsureUtc(now) < EndsAt)
        {
            return NoShowOutcome.NotEnded;
        }

        Status = BookingStatus.NoShow;
        return NoShowOutcome.Marked;
    }

    /// <summary>
    /// The owner takes a no-show back (US15): the booking is confirmed again. Returns false, and changes nothing, for any other
    /// status. The database may still refuse the change if another confirmed booking took the slot meanwhile (23P01).
    /// </summary>
    public bool UndoNoShow()
    {
        if (Status != BookingStatus.NoShow)
        {
            return false;
        }

        Status = BookingStatus.Confirmed;
        return true;
    }

    /// <summary>True from the instant the booking starts: it is no longer "in the future".</summary>
    public bool HasStarted(DateTime now) => StartsAt <= EnsureUtc(now);

    /// <summary>
    /// The club's owner cancels a booking that has not started (US12), with the reason they give. There is no
    /// cancellation window for the club. The booking is kept as <see cref="BookingStatus.CancelledByClub"/>, with the
    /// reason (trimmed) and the moment, and stops counting for the overlap constraint, so the slot is free again.
    /// Nothing changes unless the outcome is <see cref="ClubCancellationOutcome.Cancelled"/>; a missing reason is a
    /// programming error (the service validates it first).
    /// </summary>
    public ClubCancellationOutcome CancelByClub(string reason, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A booking cancelled by the club needs a reason.", nameof(reason));
        }

        if (!IsConfirmed)
        {
            return ClubCancellationOutcome.NotConfirmed;
        }

        if (HasStarted(now))
        {
            return ClubCancellationOutcome.AlreadyStarted;
        }

        Status = BookingStatus.CancelledByClub;
        CancelledAt = EnsureUtc(now);
        CancellationReason = reason.Trim();
        return ClubCancellationOutcome.Cancelled;
    }

    /// <summary>Half-open overlap, the same rule as blocks and as the database constraint (<c>tstzrange</c> is <c>[)</c>).</summary>
    public bool Overlaps(DateTime startsAt, DateTime endsAt) => StartsAt < endsAt && startsAt < EndsAt;

    private static TimeSpan EnsureWindow(TimeSpan window) =>
        window >= TimeSpan.Zero ? window : throw new ArgumentOutOfRangeException(nameof(window), window, "A cancellation window cannot be negative.");

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : throw new ArgumentException("Times must be UTC.", nameof(value));
}
