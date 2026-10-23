using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>
/// US06: the owner's clubs. Owner profile only (403 for others, 401 without a session). A club of another owner is a
/// 404 <c>club_not_found</c>, exactly like a club that does not exist (T02).
/// </summary>
[ApiController]
[Route("api/manage/clubs")]
[Authorize(Policy = Profiles.Owner)]
public sealed class ManageClubsController(ClubService clubs) : ControllerBase
{
    /// <summary>Creates a club for the signed-in owner. The slug is made from the name; the time zone is Europe/Lisbon.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<OwnerClubResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Create(ClubRequest request, CancellationToken cancellationToken)
    {
        var result = await clubs.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            // Relative on purpose: behind the proxy, the request host is the API's internal name.
            ? Created($"/api/manage/clubs/{result.Value.Id}", result.Value)
            : this.Problem(result.Error!);
    }

    /// <summary>The signed-in owner's clubs, ordered by name, one page at a time (20 per page by default, at most 100).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<OwnerClubResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await clubs.ListAsync(new PageRequest(page, pageSize), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    [HttpGet("{clubId:guid}")]
    [ProducesResponseType<OwnerClubResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Get(Guid clubId, CancellationToken cancellationToken)
    {
        var result = await clubs.GetAsync(clubId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>Replaces the club's details and cancellation window (required here). The slug never changes.</summary>
    [HttpPut("{clubId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<OwnerClubResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Update(Guid clubId, ClubRequest request, CancellationToken cancellationToken)
    {
        var result = await clubs.UpdateAsync(clubId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }
}
