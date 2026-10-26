using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Admin;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>
/// The admin's numbers (Épico 6). Admin profile only: owners and players get 403, no session 401. Counts only: nothing here
/// ever holds a player's name, email, phone or id.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = Profiles.Admin)]
public sealed class AdminController(
    AdminStatsService stats,
    AdminBookingsOverTimeService bookingsOverTime,
    AdminOwnersService owners,
    AdminOwnerStatusService ownerStatus,
    AdminTopOccupationService topOccupation,
    AdminRegistrationsService registrations) : ControllerBase
{
    /// <summary>US24: owners (active and deactivated), clubs (visible, of deactivated owners, and without active pitches), active pitches, players and confirmed bookings.</summary>
    [HttpGet("stats")]
    [ProducesResponseType<AdminStatsResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> Stats(CancellationToken cancellationToken) => Ok(await stats.GetAsync(cancellationToken));

    /// <summary>
    /// US25: bookings per day, week (from Monday) or month, by the Lisbon date they were created, with a 0 for every period
    /// without any. <c>from</c> and <c>to</c> default to the last 30 days and are widened to whole periods; cancelled
    /// bookings are left out unless <c>includeCancelled=true</c>.
    /// </summary>
    [HttpGet("bookings-over-time")]
    [ProducesResponseType<BookingsOverTimeResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> BookingsOverTime(
        [FromQuery] string? granularity,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? includeCancelled,
        CancellationToken cancellationToken)
    {
        // Read as text so that a wrong value gets a Portuguese field error, not MVC's English binding message.
        var result = await bookingsOverTime.GetAsync(new BookingsOverTimeRequest(granularity, from, to, includeCancelled), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>
    /// US26: the owners with the number of their clubs, pitches, bookings (not cancelled) and distinct players, 20 to a page.
    /// <c>sort</c> is name, email, registeredAt, status, clubs, pitches, bookings or players; <c>direction</c> asc or desc;
    /// <c>search</c> finds a part of the name or email.
    /// </summary>
    [HttpGet("owners")]
    [ProducesResponseType<PagedResponse<AdminOwnerResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Owners(
        [FromQuery] string? search,
        [FromQuery] string? sort,
        [FromQuery] string? direction,
        [FromQuery] string? page,
        CancellationToken cancellationToken)
    {
        // Read as text so that "abc" gets the same Portuguese field error as "0", not MVC's English binding message.
        var pageNumber = string.IsNullOrWhiteSpace(page) ? 1
            : int.TryParse(page, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
        var result = await owners.ListAsync(new AdminOwnersRequest(search, sort, direction, pageNumber), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>
    /// US27: the 10 clubs with the highest occupation in the period (Lisbon dates, both included; the last 30 days without them), with
    /// the formula of the owner's statistics (US14). Ties: booked hours, then the club's id. Clubs with nothing open are left out.
    /// </summary>
    [HttpGet("clubs/top-occupation")]
    [ProducesResponseType<TopOccupationResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> TopOccupation([FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        var result = await topOccupation.GetAsync(from, to, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>
    /// US28: new owners and new players per week (from Monday) or month, by the Lisbon date they registered, with a 0 for every period
    /// without any; the range rules of <c>bookings-over-time</c> (last 30 days by default, widened to whole periods).
    /// </summary>
    [HttpGet("registrations")]
    [ProducesResponseType<RegistrationsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Registrations([FromQuery] string? granularity, [FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        var result = await registrations.GetAsync(granularity, from, to, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>US29: one owner, as in the list, plus how many confirmed bookings in their clubs have not ended yet. 404 for an id that is not an owner's.</summary>
    [HttpGet("owners/{id:guid}")]
    [ProducesResponseType<AdminOwnerDetailResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Owner(Guid id, CancellationToken cancellationToken)
    {
        var result = await owners.GetAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>
    /// US29: deactivates an owner, with the reason. They cannot sign in or refresh, their clubs leave the search and refuse bookings,
    /// and no booking is cancelled. 422 <c>owner_already_deactivated</c> when it already is.
    /// </summary>
    [HttpPost("owners/{id:guid}/deactivate")]
    [ProducesResponseType<AdminOwnerDetailResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Deactivate(Guid id, [FromBody] OwnerStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await ownerStatus.DeactivateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>US29: reactivates an owner, with the reason. 422 <c>owner_not_deactivated</c> when they are not deactivated.</summary>
    [HttpPost("owners/{id:guid}/reactivate")]
    [ProducesResponseType<AdminOwnerDetailResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Reactivate(Guid id, [FromBody] OwnerStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await ownerStatus.ReactivateAsync(id, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>US29: the deactivations and reactivations of an owner, newest first: what, why, which admin and when.</summary>
    [HttpGet("owners/{id:guid}/history")]
    [ProducesResponseType<List<OwnerStatusChangeResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> History(Guid id, CancellationToken cancellationToken)
    {
        var result = await ownerStatus.HistoryAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }
}
