using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.Auth;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Auth;
using SportsBooking.Infrastructure.Auth;

namespace SportsBooking.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(ISessionService sessions, IAccountStore accounts) : ControllerBase
{
    /// <summary>Renews the session from the refresh token cookie and rotates it. Refused for deactivated accounts.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var result = await sessions.RefreshAsync(Request.Cookies[AuthCookies.RefreshToken], cancellationToken);
        if (!result.IsSuccess)
        {
            AuthCookies.Delete(Response);
            return this.Problem(result.Error!);
        }

        AuthCookies.Append(Response, result.Value);
        return NoContent();
    }

    /// <summary>The signed-in account. The front end learns who is signed in only from here.</summary>
    [HttpGet("me")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var account = Guid.TryParse(User.FindFirst(SessionClaims.Subject)?.Value, out var userId)
            ? await accounts.FindAsync(userId, cancellationToken)
            : null;

        return account is null ? Unauthorized() : Ok(account);
    }
}
