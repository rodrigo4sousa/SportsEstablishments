using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Api.ErrorHandling;
using SportsBooking.Application.Pitches;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Api.Controllers;

/// <summary>
/// US10: the price bands of one pitch. Owner profile only. Another owner's club is a 404 <c>club_not_found</c> (T02), a pitch
/// that is not in this club a 404 <c>pitch_not_found</c>, a band that is not on this pitch a 404 <c>price_band_not_found</c>.
/// </summary>
[ApiController]
[Route("api/manage/clubs/{clubId:guid}/pitches/{pitchId:guid}/price-bands")]
[Authorize(Policy = Profiles.Owner)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
public sealed class ManagePriceBandsController(PriceBandService bands) : ControllerBase
{
    /// <summary>The pitch's price bands (at most 20), ordered by start time. A slot outside every band costs the pitch's base price.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PriceBandResponse>>(StatusCodes.Status200OK, "application/json")]
    public async Task<IActionResult> List(Guid clubId, Guid pitchId, CancellationToken cancellationToken)
    {
        var result = await bands.ListAsync(clubId, pitchId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>
    /// Adds a band: weekday names, local "HH:mm" start and end in 30-minute steps (end "00:00" is midnight) and a price.
    /// 409 <c>price_band_overlap</c> if it overlaps another band on a shared weekday (touching is fine); 422
    /// <c>price_band_limit_reached</c> at 20 bands.
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<PriceBandResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> Create(Guid clubId, Guid pitchId, PriceBandRequest request, CancellationToken cancellationToken)
    {
        var result = await bands.CreateAsync(clubId, pitchId, request, cancellationToken);
        return result.IsSuccess
            // Relative on purpose: behind the proxy, the request host is the API's internal name.
            ? Created($"/api/manage/clubs/{clubId}/pitches/{pitchId}/price-bands/{result.Value.Id}", result.Value)
            : this.Problem(result.Error!);
    }

    /// <summary>Replaces a band. The same rules as creating it; a band does not overlap itself. Existing bookings keep their price.</summary>
    [HttpPut("{bandId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<PriceBandResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Update(Guid clubId, Guid pitchId, Guid bandId, PriceBandRequest request, CancellationToken cancellationToken)
    {
        var result = await bands.UpdateAsync(clubId, pitchId, bandId, request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : this.Problem(result.Error!);
    }

    /// <summary>Removes a band for real; its slots go back to the base price. Existing bookings keep their price.</summary>
    [HttpDelete("{bandId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid clubId, Guid pitchId, Guid bandId, CancellationToken cancellationToken)
    {
        var result = await bands.DeleteAsync(clubId, pitchId, bandId, cancellationToken);
        return result.IsSuccess ? NoContent() : this.Problem(result.Error!);
    }
}
