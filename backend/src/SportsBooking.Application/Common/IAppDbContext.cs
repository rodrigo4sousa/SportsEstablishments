using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Admin;
using SportsBooking.Application.Notifications;
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

    /// <summary>Every price band of the platform (US10). Reach them only through a pitch found with <see cref="Application.Pitches.OwnedPitches"/> (T02).</summary>
    DbSet<PitchPriceBand> PriceBands { get; }

    /// <summary>Every booking of the platform. In the owner area, reach them only through a pitch found with <see cref="Application.Pitches.OwnedPitches"/> (T02).</summary>
    DbSet<Booking> Bookings { get; }

    /// <summary>
    /// Every account with its profile, registration date and whether it is deactivated, and nothing else (no name, email or
    /// phone): what the admin's counts read (US24, US28). Never add a personal field to it.
    /// </summary>
    IQueryable<AccountProfile> AccountProfiles { get; }

    /// <summary>The owners' accounts, with name and email, for the admin's list of owners (US26). Players are never here.</summary>
    IQueryable<OwnerAccount> OwnerAccounts { get; }

    /// <summary>
    /// The emails waiting to be sent (US22). Add to it on the same context as the change it is about, before that
    /// change's <c>SaveChangesAsync</c>; only the outbox worker reads and updates it.
    /// </summary>
    DbSet<OutboxMessage> OutboxMessages { get; }

    /// <summary>
    /// The ids of the accounts that are not deactivated (US29), as a query to use in a subquery. The only view of
    /// accounts the Application needs: the public pages hide the clubs of a deactivated owner.
    /// </summary>
    IQueryable<Guid> ActiveAccountIds { get; }

    /// <summary>
    /// The display name of every account. Names of players are personal data: read them only for bookings reached
    /// through an owned club (US11, US12), never to list accounts. Joined to bookings, not queried alone.
    /// </summary>
    IQueryable<AccountName> AccountNames { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>An account id and its display name (US11). A class with <c>init</c> properties, so EF can filter and order after joining it.</summary>
public sealed class AccountName
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }
}
