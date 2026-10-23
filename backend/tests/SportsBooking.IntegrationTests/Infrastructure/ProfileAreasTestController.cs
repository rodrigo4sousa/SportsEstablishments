using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.IntegrationTests.Infrastructure;

/// <summary>
/// Test host only (added by <see cref="ApiFactory"/>, never part of the API): one endpoint per profile area, to prove
/// the profile policies until the real area endpoints exist.
/// </summary>
[ApiController]
[Route("test-only/areas")]
public sealed class ProfileAreasTestController : ControllerBase
{
    public const string Route = "/test-only/areas";

    [HttpGet("player")]
    [Authorize(Policy = Profiles.Player)]
    public IActionResult Player() => Ok();

    [HttpGet("owner")]
    [Authorize(Policy = Profiles.Owner)]
    public IActionResult Owner() => Ok();

    [HttpGet("admin")]
    [Authorize(Policy = Profiles.Admin)]
    public IActionResult Admin() => Ok();
}
