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

    /// <summary>401 from <c>POST /api/auth/refresh</c>: the session cannot be renewed; the user must sign in again.</summary>
    public const string InvalidRefreshToken = "invalid_refresh_token";
}
