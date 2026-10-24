using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Bookings;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>
/// US11 and US12: the bookings of one club, for its owner. Owner profile only. Another owner's club is a 404
/// <c>club_not_found</c> (T02) and a booking that is not in this club a 404 <c>booking_not_found</c>; both are checked
/// before the body. The player's name is the only personal data a booking shows.
/// </summary>
[ApiController]
[Route("api/manage/clubs/{clubId:guid}/bookings")]
[Authorize(Policy = Profiles.Owner)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class ManageBookingsController(ClubBookingService bookings) : ControllerBase
{
    /// <summary>One booking of the club: pitch, time, price, status, the player's name and, if cancelled, when and why.</summary>
    [HttpGet("{bookingId:guid}")]
    [ProducesResponseType<ClubBookingResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> Get(Guid clubId, Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await bookings.GetAsync(clubId, bookingId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>
    /// Cancels a booking that has not started, with a reason (required, at most 200 characters). The club has no
    /// cancellation window. The booking becomes <c>cancelled_by_club</c> and the slot is free again. 422
    /// <c>booking_not_confirmed</c> if it is not confirmed; 422 <c>booking_already_started</c> if it has started.
    /// </summary>
    [HttpPost("{bookingId:guid}/cancel")]
    [Consumes("application/json")]
    [ProducesResponseType<ClubBookingResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Cancel(Guid clubId, Guid bookingId, CancelClubBookingRequest request, CancellationToken cancellationToken)
    {
        var result = await bookings.CancelAsync(clubId, bookingId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }
}
