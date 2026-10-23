using SportsBooking.Application.Common;
using SportsBooking.Infrastructure.Auth;

namespace SportsBooking.Api.Auth;

/// <summary>The signed-in account of the current request, from the access token's <c>sub</c> claim.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(SessionClaims.Subject)?.Value, out var userId) ? userId : null;
}
