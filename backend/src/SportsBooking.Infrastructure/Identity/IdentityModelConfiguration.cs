using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.Infrastructure.Identity;

internal static class IdentityModelConfiguration
{
    // Fixed ids and stamps keep the seeded roles stable across migrations.
    private static readonly IdentityRole<Guid>[] ProfileRoles =
    [
        Role("0199f7a0-0000-7000-8000-000000000001", Profiles.Player),
        Role("0199f7a0-0000-7000-8000-000000000002", Profiles.Owner),
        Role("0199f7a0-0000-7000-8000-000000000003", Profiles.Admin),
    ];

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(user =>
        {
            user.ToTable("users");
            user.Property(u => u.Email).IsRequired();
            user.Property(u => u.NormalizedEmail).IsRequired();
            // One email = one account, enforced by the database and not only by UserManager.
            user.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName("ix_users_normalized_email");
            user.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");
        });

        modelBuilder.Entity<IdentityRole<Guid>>(role =>
        {
            role.ToTable("roles");
            role.HasIndex(r => r.NormalizedName).HasDatabaseName("ix_roles_normalized_name");
            role.HasData(ProfileRoles);
        });

        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
    }

    private static IdentityRole<Guid> Role(string id, string name) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        NormalizedName = name.ToUpperInvariant(),
        ConcurrencyStamp = id,
    };
}
