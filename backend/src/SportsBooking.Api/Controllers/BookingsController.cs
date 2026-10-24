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
public sealed class BookingsController(BookingService bookings, MyBookingsService myBookings, BookingCancellationService cancellation) : ControllerBase
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
        return result.IsSuccess
            // Relative on purpose, like the other Location headers: behind the proxy the request host is the API's own name.
            ? Created($"/api/bookings/{result.Value.Id}", result.Value)
            : this.Problem(result.Error!);
    }

    /// <summary>
    /// Cancels one of the signed-in player's own bookings: allowed only while it is confirmed and now is strictly before
    /// the start minus the club's current cancellation window. Another player's booking and an unknown id are 404
    /// <c>booking_not_found</c>; a booking that is not confirmed is 422 <c>booking_not_confirmed</c>; after the deadline
    /// it is 422 <c>outside_cancellation_window</c> with <c>cancellationDeadline</c> (UTC). The booking is kept as
    /// cancelled by the player and its slot is free again.
    /// </summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<MyBookingResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await cancellation.CancelAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>One of the signed-in player's own bookings. Another player's booking is a 404, like one that does not exist.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<MyBookingResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await myBookings.GetAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }
}
