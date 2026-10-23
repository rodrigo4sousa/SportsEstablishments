using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Clubs;

namespace SportsBooking.Application.Clubs;

/// <summary>
/// T02, per-owner authorization: the only way into clubs from the owner area. Every query starts from the
/// signed-in owner's clubs (the owner comes from the session, never from the request), so a club of another owner
/// is simply not there and the answer is the same 404 as for a club that does not exist.
/// Every club resource (pitches, blocks, bookings, …) goes through here too; see AGENTS.md.
/// </summary>
public sealed class OwnedClubs(IAppDbContext dbContext, ICurrentUser currentUser)
{
    /// <summary>The signed-in owner. The Owner policy guarantees a session; no session here is a programming error.</summary>
    public Guid OwnerId =>
        currentUser.UserId ?? throw new InvalidOperationException("OwnedClubs needs a signed-in owner: protect the endpoint with the Owner policy.");

    /// <summary>The signed-in owner's clubs, as a query to filter, join or project further.</summary>
    public IQueryable<Club> All()
    {
        var ownerId = OwnerId;
        return dbContext.Clubs.Where(club => club.OwnerId == ownerId);
    }

    /// <summary>
    /// The club, tracked for changes, if it belongs to the signed-in owner; otherwise <see cref="ClubErrors.NotFound"/>.
    /// Check this before validating a request, so another owner's club gives the same answer whatever is sent.
    /// </summary>
    public async Task<Result<Club>> FindAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var club = await All().SingleOrDefaultAsync(candidate => candidate.Id == clubId, cancellationToken);
        return club is null ? ClubErrors.NotFound : club;
    }
}

public static class ClubErrors
{
    /// <summary>A club that does not exist and a club of another owner give exactly this answer (T02).</summary>
    public static ServiceError NotFound { get; } = new(
        ErrorKind.NotFound,
        ErrorCodes.ClubNotFound,
        "Clube não encontrado",
        "Não encontrámos este clube.");
}
