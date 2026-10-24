using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Bookings;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>US18: a player's bookings. Player profile only (owners and admins get 403, no session 401).</summary>
[ApiController]
[Route("api/bookings")]
[Authorize(Policy = Profiles.Player)]
public sealed class BookingsController(BookingService bookings) : ControllerBase
{
    /// <summary>
    /// Books one slot: the pitch and the slot's start as an instant with its zone (as the slot lists give it). The end
    /// comes from the pitch's schedule and the price is the pitch's current one. 409 <c>slot_taken</c> when a confirmed
    /// booking overlaps (never a 500), 409 <c>slot_blocked</c> for a block; 422 for an inactive pitch, a club that takes
    /// no bookings, a start that is not a slot, a slot that has started, or a date outside today..+30 days in Lisbon.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BookingResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Create(CreateBookingRequest request, CancellationToken cancellationToken)
    {
        var result = await bookings.CreateAsync(request, cancellationToken);
        // No Location: reading a booking comes with US19.
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : this.Problem(result.Error!);
    }
}
