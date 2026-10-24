using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>
/// US07: the pitches of one of the owner's clubs. Owner profile only. Another owner's club is a 404
/// <c>club_not_found</c> (T02); a pitch that is not in this club is a 404 <c>pitch_not_found</c>.
/// </summary>
[ApiController]
[Route("api/manage/clubs/{clubId:guid}/pitches")]
[Authorize(Policy = Profiles.Owner)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class ManagePitchesController(PitchService pitches) : ControllerBase
{
    /// <summary>Adds an active pitch to the club.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<PitchResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Create(Guid clubId, PitchRequest request, CancellationToken cancellationToken)
    {
        var result = await pitches.CreateAsync(clubId, request, cancellationToken);
        return result.IsSuccess
            // Relative on purpose: behind the proxy, the request host is the API's internal name.
            ? Created($"/api/manage/clubs/{clubId}/pitches/{result.Value.Id}", result.Value)
            : this.Problem(result.Error!);
    }

    /// <summary>The club's pitches, active and inactive, ordered by name, one page at a time (20 per page by default, at most 100).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<PitchResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> List(
        Guid clubId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await pitches.ListAsync(clubId, new PageRequest(page, pageSize), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    [HttpGet("{pitchId:guid}")]
    [ProducesResponseType<PitchResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> Get(Guid clubId, Guid pitchId, CancellationToken cancellationToken)
    {
        var result = await pitches.GetAsync(clubId, pitchId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>Replaces name, sport and price. A new price only applies to bookings made from now on.</summary>
    [HttpPut("{pitchId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<PitchResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> Update(Guid clubId, Guid pitchId, PitchRequest request, CancellationToken cancellationToken)
    {
        var result = await pitches.UpdateAsync(clubId, pitchId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>Takes the pitch out of search and bookings. Pitches are never deleted. Doing it twice is fine.</summary>
    [HttpPost("{pitchId:guid}/deactivate")]
    [ProducesResponseType<PitchResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> Deactivate(Guid clubId, Guid pitchId, CancellationToken cancellationToken)
    {
        var result = await pitches.DeactivateAsync(clubId, pitchId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>Makes an inactive pitch available again. Doing it twice is fine.</summary>
    [HttpPost("{pitchId:guid}/reactivate")]
    [ProducesResponseType<PitchResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> Reactivate(Guid clubId, Guid pitchId, CancellationToken cancellationToken)
    {
        var result = await pitches.ReactivateAsync(clubId, pitchId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }
}
