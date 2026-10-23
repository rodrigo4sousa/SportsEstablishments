using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportsBooking.Domain.Clubs;
using SportsBooking.Infrastructure.Identity;

namespace SportsBooking.Infrastructure.Clubs;

internal sealed class ClubConfiguration : IEntityTypeConfiguration<Club>
{
    /// <summary>Settles concurrent creates with the same name: the loser retries with the next suffix (ClubStore).</summary>
    public const string SlugIndex = "ix_clubs_slug";

    public void Configure(EntityTypeBuilder<Club> builder)
    {
        builder.ToTable("clubs", table => table.HasCheckConstraint(
            "ck_clubs_cancellation_window_hours",
            $"cancellation_window_hours BETWEEN {ClubRules.MinimumCancellationWindowHours} AND {ClubRules.MaximumCancellationWindowHours}"));

        builder.HasKey(club => club.Id);
        builder.Property(club => club.Id).ValueGeneratedNever();
        builder.Property(club => club.Slug).HasMaxLength(ClubSlug.MaximumLength).IsRequired();
        builder.Property(club => club.Name).HasMaxLength(ClubRules.NameMaximumLength).IsRequired();
        builder.Property(club => club.Address).HasMaxLength(ClubRules.AddressMaximumLength).IsRequired();
        builder.Property(club => club.Locality).HasMaxLength(ClubRules.LocalityMaximumLength).IsRequired();
        builder.Property(club => club.Phone).HasMaxLength(ClubRules.PhoneMaximumLength).IsRequired();
        builder.Property(club => club.ContactEmail).HasMaxLength(ClubRules.ContactEmailMaximumLength).IsRequired();
        builder.Property(club => club.TimeZoneId).HasColumnName("time_zone").HasMaxLength(64).IsRequired();

        builder.HasIndex(club => club.Slug).IsUnique().HasDatabaseName(SlugIndex);
        // The owner's list: filtered by owner, ordered by name.
        builder.HasIndex(club => new { club.OwnerId, club.Name }).HasDatabaseName("ix_clubs_owner_id_name");

        // A club is never deleted with its owner's account; deleting an owner with clubs fails instead.
        builder.HasOne<AppUser>().WithMany().HasForeignKey(club => club.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
