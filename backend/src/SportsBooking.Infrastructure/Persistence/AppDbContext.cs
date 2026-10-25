using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Application.Admin;
using SportsBooking.Application.Common;
using SportsBooking.Application.Notifications;
using SportsBooking.Domain.Accounts;
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

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public IQueryable<Guid> ActiveAccountIds => Users.Where(user => user.DeactivatedAt == null).Select(user => user.Id);

    public IQueryable<AccountProfile> AccountProfiles =>
        from user in Users
        join userRole in UserRoles on user.Id equals userRole.UserId
        join role in Roles on userRole.RoleId equals role.Id
        select new AccountProfile { Id = user.Id, Profile = role.Name!, RegisteredAt = user.RegisteredAt, IsDeactivated = user.DeactivatedAt != null };

    public IQueryable<OwnerAccount> OwnerAccounts =>
        from user in Users
        join userRole in UserRoles on user.Id equals userRole.UserId
        join role in Roles on userRole.RoleId equals role.Id
        where role.Name == Profiles.Owner
        select new OwnerAccount { Id = user.Id, Name = user.Name, Email = user.Email!, RegisteredAt = user.RegisteredAt, IsDeactivated = user.DeactivatedAt != null };

    public IQueryable<AccountName> AccountNames => Users.Select(user => new AccountName { Id = user.Id, Name = user.Name });

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        IdentityModelConfiguration.Configure(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
