using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Clubs;
using SportsBooking.Domain.Pitches;
using SportsBooking.Infrastructure.Auth;
using SportsBooking.Infrastructure.Identity;

namespace SportsBooking.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IAppDbContext
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Club> Clubs => Set<Club>();

    public DbSet<Pitch> Pitches => Set<Pitch>();

    public DbSet<Block> Blocks => Set<Block>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public IQueryable<Guid> ActiveAccountIds => Users.Where(user => user.DeactivatedAt == null).Select(user => user.Id);

    public IQueryable<AccountName> AccountNames => Users.Select(user => new AccountName { Id = user.Id, Name = user.Name });

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        IdentityModelConfiguration.Configure(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
