using Microsoft.EntityFrameworkCore;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Clubs;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Application.Common;

/// <summary>
/// The application's view of the database (docs/stack.md: no generic repository; the DbContext is the unit of work).
/// Implemented by the EF Core <c>AppDbContext</c> in Infrastructure.
/// </summary>
public interface IAppDbContext
{
    /// <summary>
    /// Every club of the platform. In the owner area, never query this directly: go through
    /// <see cref="Clubs.OwnedClubs"/>, which keeps only the signed-in owner's clubs (T02).
    /// </summary>
    DbSet<Club> Clubs { get; }

    /// <summary>Every pitch of the platform. In the owner area, reach pitches only through a club resolved with <see cref="Clubs.OwnedClubs"/> (T02).</summary>
    DbSet<Pitch> Pitches { get; }

    /// <summary>Every block of the platform. Reach blocks only through a pitch found with <see cref="Application.Pitches.OwnedPitches"/> (T02).</summary>
    DbSet<Block> Blocks { get; }

    /// <summary>Every booking of the platform. In the owner area, reach them only through a pitch found with <see cref="Application.Pitches.OwnedPitches"/> (T02).</summary>
    DbSet<Booking> Bookings { get; }

    /// <summary>
    /// The ids of the accounts that are not deactivated (US29), as a query to use in a subquery. The only view of
    /// accounts the Application needs: the public pages hide the clubs of a deactivated owner.
    /// </summary>
    IQueryable<Guid> ActiveAccountIds { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
