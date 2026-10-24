using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SportsBooking.Domain.Pitches;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.IntegrationTests.Infrastructure;

/// <summary>
/// Test host only (found with the other test controllers, never part of the API): an endpoint whose database write
/// fails in a way nothing handles, to prove such failures still end in the global exception handler.
/// </summary>
[ApiController]
[Route("test-only/database-failure")]
[AllowAnonymous]
public sealed class DatabaseFailureTestController(AppDbContext dbContext) : ControllerBase
{
    public const string Route = "/test-only/database-failure";

    /// <summary>Saves a block for a pitch that does not exist: a foreign key violation (23503), not the booking conflict.</summary>
    [HttpGet]
    public async Task<IActionResult> Fail(CancellationToken cancellationToken)
    {
        var start = new DateTime(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        dbContext.Blocks.Add(Block.Create(Guid.CreateVersion7(), start, start.AddHours(1), reason: null, now: start));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok();
    }
}
