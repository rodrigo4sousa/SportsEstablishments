using SportsBooking.Domain.Clubs;

namespace SportsBooking.Application.Clubs;

/// <summary>Club persistence that needs the database's own rules; implemented in Infrastructure.</summary>
public interface IClubStore
{
    /// <summary>
    /// Saves a new club under the first free slug for <paramref name="baseSlug"/> (the base itself, then "-2",
    /// "-3", …). The unique index on the slug settles concurrent creates: the loser retries with the next suffix.
    /// <paramref name="create"/> builds the club for a given slug and may be called more than once.
    /// </summary>
    Task<Club> AddWithUniqueSlugAsync(string baseSlug, Func<string, Club> create, CancellationToken cancellationToken);
}
