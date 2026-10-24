using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Clubs;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Application.Catalog;

/// <summary>
/// The public pages' way into clubs (US16, US17), the counterpart of <see cref="Clubs.OwnedClubs"/> for players and
/// visitors: only a club that can take bookings is visible, that is, its owner is not deactivated (US29) and it has at
/// least one active pitch (US07). Everything else is simply not there, so an unknown, hidden and empty club all
/// answer the same 404. Every public query starts here and never reads <c>IAppDbContext.Clubs</c> directly.
/// </summary>
public sealed class VisibleClubs(IAppDbContext dbContext)
{
    /// <summary>The visible clubs, read-only, as a query to filter, order and project further.</summary>
    public IQueryable<Club> All() => dbContext.Clubs.AsNoTracking()
        .Where(club => dbContext.ActiveAccountIds.Contains(club.OwnerId))
        .Where(club => dbContext.Pitches.Any(pitch => pitch.ClubId == club.Id && pitch.Status == PitchStatus.Active));
}
