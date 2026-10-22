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
}
