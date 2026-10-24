namespace SportsBooking.Application.Common;

/// <summary>
/// Stable error codes returned in the <c>code</c> field of every ProblemDetails response.
/// The frontend picks the user-facing message by code, never by the response text.
/// </summary>
public static class ErrorCodes
{
    public const string BadRequest = "bad_request";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string Conflict = "conflict";
    public const string TooManyRequests = "too_many_requests";
    public const string InternalError = "internal_error";

    /// <summary>400 with per-field messages in <c>errors</c>.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>403: a mutating request whose <c>Origin</c> is not the site's own (CSRF defence).</summary>
    public const string OriginNotAllowed = "origin_not_allowed";

    /// <summary>409 from registration: an account with this email already exists (any profile).</summary>
    public const string EmailTaken = "email_taken";

    /// <summary>401 from <c>POST /api/auth/refresh</c>: the session cannot be renewed; the user must sign in again.</summary>
    public const string InvalidRefreshToken = "invalid_refresh_token";

    /// <summary>
    /// 401 from <c>POST /api/auth/login</c>, the same for an unknown email, a wrong password and a locked or
    /// deactivated account.
    /// </summary>
    public const string InvalidCredentials = "invalid_credentials";

    /// <summary>
    /// 404 from the owner area (T02): the club does not exist or belongs to another owner. The two cases are
    /// indistinguishable on purpose, so no owner learns that another owner's club exists.
    /// </summary>
    public const string ClubNotFound = "club_not_found";

    /// <summary>404 from the owner area (US07): the club is the owner's, but it has no pitch with this id.</summary>
    public const string PitchNotFound = "pitch_not_found";

    /// <summary>404 from the owner area (US09): the pitch is the owner's, but it has no block with this id.</summary>
    public const string BlockNotFound = "block_not_found";

    /// <summary>409 from creating a block (US09): it overlaps another block of the same pitch.</summary>
    public const string BlockOverlap = "block_overlap";

    /// <summary>422 from removing a block (US09): it has already started, so it stays as history.</summary>
    public const string BlockAlreadyStarted = "block_already_started";

    /// <summary>409 from creating a block (US09): confirmed bookings overlap it; <c>bookings</c> lists their times.</summary>
    public const string BlockOverlapsBookings = "block_overlaps_bookings";

    /// <summary>422 from deactivating a pitch (US07): it has future confirmed bookings; <c>count</c> says how many.</summary>
    public const string PitchHasFutureBookings = "pitch_has_future_bookings";

    /// <summary>409 from booking (US18, T03): a confirmed booking of the pitch overlaps the slot.</summary>
    public const string SlotTaken = "slot_taken";

    /// <summary>409 from booking (US18): a block of the pitch overlaps the slot.</summary>
    public const string SlotBlocked = "slot_blocked";

    /// <summary>422 from booking (US18): the start is not a slot the pitch's schedule generates (closed day, outside the hours, off the grid).</summary>
    public const string InvalidSlot = "invalid_slot";

    /// <summary>422 from booking (US18): the slot has already started.</summary>
    public const string SlotStarted = "slot_started";

    /// <summary>422 from booking (US18): the slot's date is not between today and 30 days ahead in the club's time zone.</summary>
    public const string OutsideBookingWindow = "outside_booking_window";

    /// <summary>422 from booking (US18, US07): the pitch is inactive.</summary>
    public const string PitchInactive = "pitch_inactive";

    /// <summary>422 from booking (US18, US29): the club's owner is deactivated, so the club takes no bookings.</summary>
    public const string ClubUnavailable = "club_unavailable";

    /// <summary>404 from the player's booking endpoints (US19): no such booking, or it belongs to another player.</summary>
    public const string BookingNotFound = "booking_not_found";

    /// <summary>422 from cancelling (US19): the club's cancellation window has closed; <c>cancellationDeadline</c> says when.</summary>
    public const string OutsideCancellationWindow = "outside_cancellation_window";

    /// <summary>422 from cancelling (US19): only a confirmed booking can be cancelled (it is cancelled already, or a no-show).</summary>
    public const string BookingNotConfirmed = "booking_not_confirmed";
}
