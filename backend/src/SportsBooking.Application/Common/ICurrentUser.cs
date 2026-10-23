namespace SportsBooking.Application.Common;

/// <summary>
/// Who is making the request, read from the session (the access token), never from the request body or URL.
/// Implemented in the API from the authenticated user.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The signed-in account's id, or null without a session.</summary>
    Guid? UserId { get; }
}
