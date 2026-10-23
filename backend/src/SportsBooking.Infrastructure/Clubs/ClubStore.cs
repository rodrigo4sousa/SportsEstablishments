using Microsoft.EntityFrameworkCore;
using Npgsql;
using SportsBooking.Application.Clubs;
using SportsBooking.Domain.Clubs;
using SportsBooking.Infrastructure.Persistence;

namespace SportsBooking.Infrastructure.Clubs;

internal sealed class ClubStore(AppDbContext dbContext) : IClubStore
{
    // Each lost race moves on to a higher suffix, so a handful of attempts covers any realistic burst.
    private const int MaxAttempts = 10;

    public async Task<Club> AddWithUniqueSlugAsync(string baseSlug, Func<string, Club> create, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            // Every slug that could collide: the base and "base-…" (other names that start the same way are ignored).
            var prefix = baseSlug + "-";
            var taken = await dbContext.Clubs.AsNoTracking()
                .Where(club => club.Slug == baseSlug || club.Slug.StartsWith(prefix))
                .Select(club => club.Slug)
                .ToListAsync(cancellationToken);

            var club = create(ClubSlug.NextAvailable(baseSlug, taken));
            dbContext.Clubs.Add(club);
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                return club;
            }
            catch (DbUpdateException exception) when (attempt < MaxAttempts && exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: ClubConfiguration.SlugIndex,
            })
            {
                // Another create took this slug between our read and our insert: try again with the next one.
                dbContext.Entry(club).State = EntityState.Detached;
            }
        }
    }
}
