using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Clubs;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Application.Pitches;

/// <summary>
/// T02 for pitches: the way into a pitch from the owner area, built on <see cref="OwnedClubs"/>. The club is checked
/// first (another owner's club is <c>club_not_found</c>), then the pitch is looked for only inside that club (a pitch
/// of another club is <c>pitch_not_found</c>). Check this before validating a request.
/// </summary>
public sealed class OwnedPitches(OwnedClubs ownedClubs, IAppDbContext dbContext)
{
    public Task<bool> ClubIsOwnedAsync(Guid clubId, CancellationToken cancellationToken) =>
        ownedClubs.All().AnyAsync(club => club.Id == clubId, cancellationToken);

    /// <summary>The club's time zone, for a club the signed-in owner owns (check <see cref="ClubIsOwnedAsync"/> first).</summary>
    public async Task<TimeZoneInfo> TimeZoneOfAsync(Guid clubId, CancellationToken cancellationToken)
    {
        var id = await ownedClubs.All().AsNoTracking().Where(club => club.Id == clubId).Select(club => club.TimeZoneId).SingleAsync(cancellationToken);
        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }

    /// <summary>The pitches of one club, and only if that club is the signed-in owner's.</summary>
    public IQueryable<Pitch> Of(Guid clubId)
    {
        var owned = ownedClubs.All();
        return dbContext.Pitches.Where(pitch => pitch.ClubId == clubId && owned.Any(club => club.Id == pitch.ClubId));
    }

    public async Task<Result<Pitch>> FindAsync(Guid clubId, Guid pitchId, bool tracked, CancellationToken cancellationToken)
    {
        if (!await ClubIsOwnedAsync(clubId, cancellationToken))
        {
            return ClubErrors.NotFound;
        }

        var pitches = tracked ? Of(clubId) : Of(clubId).AsNoTracking();
        var pitch = await pitches.SingleOrDefaultAsync(candidate => candidate.Id == pitchId, cancellationToken);
        return pitch is null ? PitchErrors.NotFound : pitch;
    }
}
