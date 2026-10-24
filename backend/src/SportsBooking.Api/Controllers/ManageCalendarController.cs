using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Bookings;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>
/// US11: the owner's calendar of one club. Owner profile only; another owner's club is a 404 <c>club_not_found</c> (T02),
/// checked before the query string. Players and admins get 403, no session 401.
/// </summary>
[ApiController]
[Route("api/manage/clubs/{clubId:guid}/calendar")]
[Authorize(Policy = Profiles.Owner)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class ManageCalendarController(ClubCalendarService calendar) : ControllerBase
{
    /// <summary>
    /// The club's calendar for up to seven days: <c>from</c> and <c>to</c> are local dates ("2026-10-25", both included,
    /// <c>from</c> not after <c>to</c>). It has the club's active pitches (and inactive ones that have bookings in the range)
    /// with each day's opening hours, the bookings in the range with the player's name, and the blocks. Cancelled bookings are
    /// left out unless <c>includeCancelled=true</c>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<CalendarResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Get(
        Guid clubId,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? includeCancelled,
        CancellationToken cancellationToken)
    {
        // Read as text so that a wrong value gets a Portuguese field error, not MVC's English binding message.
        var result = await calendar.GetAsync(clubId, new CalendarRequest(from, to, includeCancelled), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }
}
