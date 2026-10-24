using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Bookings;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>US19: the signed-in player's own bookings. Player profile only (owners and admins get 403, no session 401).</summary>
[ApiController]
[Route("api/me/bookings")]
[Authorize(Policy = Profiles.Player)]
public sealed class MeBookingsController(MyBookingsService myBookings) : ControllerBase
{
    /// <summary>
    /// The caller's bookings, 20 to a page. <c>scope=upcoming</c>: confirmed bookings that have not ended, oldest first.
    /// <c>scope=past</c>: ended and cancelled bookings, newest first. Each has a <c>canCancel</c> flag and, for a
    /// confirmed booking, the <c>cancellationDeadline</c> (UTC instant).
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<MyBookingResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> List([FromQuery] string? scope, [FromQuery] string? page, CancellationToken cancellationToken)
    {
        // Read as text so that "abc" gets the same Portuguese field error as "0", not MVC's English binding message.
        var pageNumber = string.IsNullOrWhiteSpace(page) ? 1
            : int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        var result = await myBookings.ListAsync(new MyBookingsRequest(scope, pageNumber), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }
}
