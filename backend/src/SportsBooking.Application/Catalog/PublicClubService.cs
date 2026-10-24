using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Application.Catalog;

/// <summary>An active pitch on the club page: what a player needs to pick one. The price is the current price per slot.</summary>
public sealed record PublicPitchResponse(Guid Id, string Name, string Sport, decimal PricePerSlot);

/// <summary>
/// The public page of a club (US16): its contacts and its active pitches. Nothing about the owner or the players.
/// The club's own id stays private (the owner area uses it); the public address is the slug.
/// </summary>
public sealed record PublicClubResponse(
    string Slug,
    string Name,
    string Address,
    string Locality,
    string Phone,
    string ContactEmail,
    IReadOnlyList<PublicPitchResponse> Pitches);

/// <summary>
/// US16 and US17: what a visitor or player can read about one club and its pitches. Everything starts from
/// <see cref="VisibleClubs"/>, so an unknown club, a club without an active pitch and a club of a deactivated owner
/// all give the same 404 <c>club_not_found</c>.
/// </summary>
public sealed class PublicClubService(VisibleClubs visibleClubs, IAppDbContext dbContext)
{
    public async Task<Result<PublicClubResponse>> GetAsync(string slug, CancellationToken cancellationToken)
    {
        var club = await visibleClubs.All()
            .Where(candidate => candidate.Slug == slug)
            .Select(candidate => new { candidate.Id, candidate.Slug, candidate.Name, candidate.Address, candidate.Locality, candidate.Phone, candidate.ContactEmail })
            .SingleOrDefaultAsync(cancellationToken);
        if (club is null)
        {
            return ClubErrors.NotFound;
        }

#pragma warning disable CA1304, CA1311 // Translated to SQL lower(); no .NET culture is involved.
        var pitches = await dbContext.Pitches.AsNoTracking()
            .Where(pitch => pitch.ClubId == club.Id && pitch.Status == PitchStatus.Active)
            .OrderBy(pitch => pitch.Name.ToLower())
            .ThenBy(pitch => pitch.Name)
            .ThenBy(pitch => pitch.Id)
            .ToListAsync(cancellationToken);
#pragma warning restore CA1304, CA1311

        return new PublicClubResponse(
            club.Slug,
            club.Name,
            club.Address,
            club.Locality,
            club.Phone,
            club.ContactEmail,
            pitches.Select(pitch => new PublicPitchResponse(pitch.Id, pitch.Name, Sports.ToCode(pitch.Sport), pitch.PricePerSlot)).ToList());
    }
}
