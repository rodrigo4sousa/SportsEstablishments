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
    /// <summary>
    /// US13: books a slot for a customer without an account (<c>customerName</c>, <c>customerPhone</c>). The slot rules, the
    /// pitch lock and the overlap constraint are the player's (US18): 409 <c>slot_taken</c> (never a 500) and
    /// <c>slot_blocked</c>; 422 for an inactive pitch, a start that is not a slot, a slot that has started, or a date outside
    /// today..+30 days in Lisbon. The booking is marked <c>createdByClub</c>, has the price of the moment, and sends no email.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<ClubBookingResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Create(Guid clubId, CreateClubBookingRequest request, CancellationToken cancellationToken)
    {
        var result = await bookings.CreateAsync(clubId, request, cancellationToken);
        return result.IsSuccess
            // Relative on purpose, like the other Location headers: behind the proxy the request host is the API's own name.
            ? Created($"/api/manage/clubs/{clubId}/bookings/{result.Value.Id}", result.Value)
            : this.Problem(result.Error!);
    }

    /// <summary>One booking of the club: pitch, time, price, status, who it is for (the player's name, or the customer's name and phone when the club made it) and, if cancelled, when and why.</summary>
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

    /// <summary>
    /// US15: marks a booking whose end has passed as a no-show (<c>no_show</c>), a player's or a customer's. 422
    /// <c>booking_not_confirmed</c> if it is not confirmed (cancelled, or already a no-show); 422 <c>booking_not_ended</c> if it has
    /// not ended. The answer is the booking, with the player's number of no-shows at this owner's clubs.
    /// </summary>
    [HttpPost("{bookingId:guid}/no-show")]
    [ProducesResponseType<ClubBookingResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> MarkNoShow(Guid clubId, Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await bookings.MarkNoShowAsync(clubId, bookingId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>
    /// US15: takes a no-show back, so the booking is <c>confirmed</c> again. 422 <c>booking_not_no_show</c> if it is not a no-show;
    /// 409 <c>slot_taken</c> if another confirmed booking took the slot in the meantime.
    /// </summary>
    [HttpPost("{bookingId:guid}/no-show/undo")]
    [ProducesResponseType<ClubBookingResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> UndoNoShow(Guid clubId, Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await bookings.UndoNoShowAsync(clubId, bookingId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }
}
