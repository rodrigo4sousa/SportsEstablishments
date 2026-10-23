using Microsoft.EntityFrameworkCore;
using SportsBooking.Domain.Clubs;

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

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
