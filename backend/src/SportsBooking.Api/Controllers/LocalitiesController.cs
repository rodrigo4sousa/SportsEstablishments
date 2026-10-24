using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Application.Catalog;

namespace SportsBooking.Api.Controllers;

/// <summary>US16 (provisional): the localities to choose from in the club search. Public and read-only.</summary>
[ApiController]
[Route("api/localities")]
[AllowAnonymous]
public sealed class LocalitiesController(ClubSearchService search) : ControllerBase
{
    /// <summary>The localities of the clubs that can take bookings, one per spelling-insensitive locality, ordered.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await search.LocalitiesAsync(cancellationToken));
}
