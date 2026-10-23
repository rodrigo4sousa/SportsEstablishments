using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SportsBooking.Api.Auth;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Api.Security;
using SportsBooking.Application.Accounts;
using SportsBooking.Application.Auth;
using SportsBooking.Infrastructure.Auth;

namespace SportsBooking.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    PlayerRegistrationService registration,
    LoginService login,
    ISessionService sessions,
    IAccountStore accounts) : ControllerBase
{
    /// <summary>
    /// US01: creates a player account and signs the player in (session cookies set). A taken email is a 409
    /// <c>email_taken</c>; invalid fields are a 400 <c>validation_failed</c> with one message per field.
    /// </summary>
    [HttpPost("register")]
    [Consumes("application/json")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimits.Register)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Register(RegisterPlayerRequest request, CancellationToken cancellationToken)
    {
        var result = await registration.RegisterAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.Problem(result.Error!);
        }

        AuthCookies.Append(Response, result.Value.Session);
        // Relative on purpose: behind the proxy, the request host is the API's internal name.
        return Created("/api/auth/me", result.Value.Account);
    }

    /// <summary>
    /// US03: signs in a player, owner or admin (session cookies set) and returns the account, as <c>/me</c> does.
    /// Every failure is the same 401 <c>invalid_credentials</c>: unknown email, wrong password, locked or deactivated
    /// account. Empty fields are a 400 <c>validation_failed</c>.
    /// </summary>
    [HttpPost("login")]
    [Consumes("application/json")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimits.Login)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await login.LoginAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return this.Problem(result.Error!);
        }

        AuthCookies.Append(Response, result.Value.Session);
        return Ok(result.Value.Account);
    }

    /// <summary>Renews the session from the refresh token cookie and rotates it. Refused for deactivated accounts.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimits.Refresh)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
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

    /// <summary>
    /// US03: ends the session. Revokes the refresh token from the cookie (if any) and clears both cookies. Always 204,
    /// with or without a valid session, so it also works after the access token has expired.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sessions.EndAsync(Request.Cookies[AuthCookies.RefreshToken], cancellationToken);
        AuthCookies.Delete(Response);
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
