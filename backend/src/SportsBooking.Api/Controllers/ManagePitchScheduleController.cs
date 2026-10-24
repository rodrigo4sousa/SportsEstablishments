using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>
/// US08: a pitch's slot length and weekly hours, and a preview of the slots they generate. Owner profile only.
/// Another owner's club is a 404 <c>club_not_found</c> (T02); a pitch that is not in this club is a 404
/// <c>pitch_not_found</c>.
/// </summary>
[ApiController]
[Route("api/manage/clubs/{clubId:guid}/pitches/{pitchId:guid}")]
[Authorize(Policy = Profiles.Owner)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class ManagePitchScheduleController(PitchScheduleService schedules) : ControllerBase
{
    /// <summary>The slot length and every weekday (closed, or opening and closing as local "HH:mm"). A new pitch is closed every day.</summary>
    [HttpGet("schedule")]
    [ProducesResponseType<PitchScheduleResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> Get(Guid clubId, Guid pitchId, CancellationToken cancellationToken)
    {
        var result = await schedules.GetAsync(clubId, pitchId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>
    /// Replaces the slot length (30, 60 or 90) and the whole week. Closing "00:00" is midnight; hours past midnight
    /// are rejected. Existing bookings are not changed.
    /// </summary>
    [HttpPut("schedule")]
    [Consumes("application/json")]
    [ProducesResponseType<PitchScheduleResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Update(Guid clubId, Guid pitchId, PitchScheduleRequest request, CancellationToken cancellationToken)
    {
        var result = await schedules.UpdateAsync(clubId, pitchId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>
    /// Read-only preview of the slots the schedule generates on a local date ("yyyy-MM-dd", from today up to 30 days
    /// ahead in Lisbon), start and end as UTC instants. Bookings and blocks are not taken into account.
    /// </summary>
    [HttpGet("slots")]
    [ProducesResponseType<PitchSlotsResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Slots(Guid clubId, Guid pitchId, [FromQuery] string? date, CancellationToken cancellationToken)
    {
        var result = await schedules.SlotsAsync(clubId, pitchId, date, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }
}
