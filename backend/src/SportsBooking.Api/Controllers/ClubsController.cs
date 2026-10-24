using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Catalog;
using SportsBooking.Application.Common;

namespace SportsBooking.Api.Controllers;

/// <summary>
/// US16: the public clubs. Open to everyone, signed in or not, read-only, and nothing about players or owners.
/// A club that cannot take bookings (no active pitch, deactivated owner) is simply not there.
/// </summary>
[ApiController]
[Route("api/clubs")]
[AllowAnonymous]
public sealed class ClubsController(ClubSearchService search, PublicClubService clubs) : ControllerBase
{
    /// <summary>
    /// Searches the clubs by sport (a stable code, e.g. <c>padel</c>), locality (ignoring case and accents) and date
    /// (<c>yyyy-MM-dd</c>, from today up to 30 days ahead; today in Lisbon by default), 20 per page, ordered by name.
    /// Each result says whether the club has a free slot on the date.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<ClubSearchItem>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Search(
        [FromQuery] string? sport,
        [FromQuery] string? locality,
        [FromQuery] string? date,
        [FromQuery] int page = 1,
        CancellationToken cancellationToken = default)
    {
        var result = await search.SearchAsync(new ClubSearchRequest(sport, locality, date, page), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>
    /// The public page of a club, by its slug: name, address, locality, phone, contact email and its active pitches
    /// with their current price. An unknown club, a club with no active pitch and a club of a deactivated owner are
    /// all the same 404 <c>club_not_found</c>.
    /// </summary>
    [HttpGet("{slug}")]
    [ProducesResponseType<PublicClubResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(string slug, CancellationToken cancellationToken)
    {
        var result = await clubs.GetAsync(slug, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }
}
