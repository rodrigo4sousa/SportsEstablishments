using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Statistics;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>
/// US14: occupation and revenue for the owner. Owner profile only; another owner's club is a 404 <c>club_not_found</c> (T02), checked
/// before the query. Dates are Lisbon <c>yyyy-MM-dd</c>, both included; without them, the last 30 days; at most 366 days.
/// </summary>
[ApiController]
[Authorize(Policy = Profiles.Owner)]
public sealed class ManageStatisticsController(ClubStatisticsService statistics) : ControllerBase
{
    /// <summary>
    /// One club, per pitch and in total: booked hours (Confirmed and NoShow bookings, clipped to the period), open hours (the schedule
    /// in force, real elapsed time), occupation (booked / open), revenue (non-cancelled bookings) and the counts. <c>pitchId</c> keeps
    /// one pitch of the club (404 <c>pitch_not_found</c> for another club's).
    /// </summary>
    [HttpGet("api/manage/clubs/{clubId:guid}/statistics")]
    [ProducesResponseType<ClubStatisticsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Club(Guid clubId, [FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid? pitchId, CancellationToken cancellationToken)
    {
        var result = await statistics.ForClubAsync(clubId, from, to, pitchId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>Every club of the signed-in owner, one line each with the same numbers, and the total.</summary>
    [HttpGet("api/manage/statistics")]
    [ProducesResponseType<OwnerStatisticsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Clubs([FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        var result = await statistics.ForOwnerAsync(from, to, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }
}
