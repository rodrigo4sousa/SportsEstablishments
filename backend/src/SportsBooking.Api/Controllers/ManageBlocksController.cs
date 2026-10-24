using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Common;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>
/// US09: the blocks of one pitch. Owner profile only. Another owner's club is a 404 <c>club_not_found</c> (T02); a
/// pitch that is not in this club is a 404 <c>pitch_not_found</c>; a block that is not on this pitch a 404 <c>block_not_found</c>.
/// </summary>
[ApiController]
[Route("api/manage/clubs/{clubId:guid}/pitches/{pitchId:guid}/blocks")]
[Authorize(Policy = Profiles.Owner)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class ManageBlocksController(BlockService blocks) : ControllerBase
{
    /// <summary>
    /// Blocks a period. <c>start</c> and <c>end</c> are Lisbon local date-times ("2026-10-25T09:00", 30-minute steps);
    /// the answer has them as UTC instants. 409 <c>block_overlap</c> if another block of the pitch overlaps (touching is fine).
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BlockResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Create(Guid clubId, Guid pitchId, BlockRequest request, CancellationToken cancellationToken)
    {
        var result = await blocks.CreateAsync(clubId, pitchId, request, cancellationToken);
        return result.IsSuccess
            // Relative on purpose: behind the proxy, the request host is the API's internal name.
            ? Created($"/api/manage/clubs/{clubId}/pitches/{pitchId}/blocks/{result.Value.Id}", result.Value)
            : this.Problem(result.Error!);
    }

    /// <summary>The pitch's ongoing and future blocks, ordered by start, one page at a time (20 per page by default, at most 100).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResponse<BlockResponse>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public async Task<IActionResult> List(
        Guid clubId,
        Guid pitchId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await blocks.ListAsync(clubId, pitchId, new PageRequest(page, pageSize), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>Removes a block that has not started. A block that has started is refused with 422 <c>block_already_started</c>.</summary>
    [HttpDelete("{blockId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Delete(Guid clubId, Guid pitchId, Guid blockId, CancellationToken cancellationToken)
    {
        var result = await blocks.DeleteAsync(clubId, pitchId, blockId, cancellationToken);
        return result.IsSuccess ? NoContent() : this.Problem(result.Error!);
    }
}
