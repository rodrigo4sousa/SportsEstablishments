using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportsBooking.Domain.Clubs;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Infrastructure.Pitches;

internal sealed class PitchConfiguration : IEntityTypeConfiguration<Pitch>
{
    /// <summary>One name per club, ignoring case: the loser of two concurrent creates gets a validation error (PitchStore).</summary>
    public const string NameIndex = "ix_pitches_club_id_name_key";

    private const string ActiveCode = "active";
    private const string InactiveCode = "inactive";

    public void Configure(EntityTypeBuilder<Pitch> builder)
    {
        builder.ToTable("pitches", table =>
        {
            table.HasCheckConstraint("ck_pitches_price_per_slot", FormattableString.Invariant($"price_per_slot > 0 AND price_per_slot <= {PitchRules.MaximumPrice}"));
            table.HasCheckConstraint("ck_pitches_sport", $"sport IN ({string.Join(", ", Sports.Codes.Select(code => $"'{code}'"))})");
            table.HasCheckConstraint("ck_pitches_status", $"status IN ('{ActiveCode}', '{InactiveCode}')");
        });

        builder.HasKey(pitch => pitch.Id);
        builder.Property(pitch => pitch.Id).ValueGeneratedNever();
        builder.Property(pitch => pitch.Name).HasMaxLength(PitchRules.NameMaximumLength).IsRequired();
        builder.Property(pitch => pitch.NameKey).HasMaxLength(PitchRules.NameMaximumLength).IsRequired();
        builder.Property(pitch => pitch.Sport)
            .HasConversion(sport => Sports.ToCode(sport), code => ParseSport(code))
            .HasMaxLength(16)
            .IsRequired();
        builder.Property(pitch => pitch.PricePerSlot).HasPrecision(10, PitchRules.PriceDecimals);
        builder.Property(pitch => pitch.Status)
            .HasConversion(status => status == PitchStatus.Active ? ActiveCode : InactiveCode, code => code == ActiveCode ? PitchStatus.Active : PitchStatus.Inactive)
            .HasMaxLength(16)
            .IsRequired();
        builder.Ignore(pitch => pitch.IsActive);

        builder.HasIndex(pitch => new { pitch.ClubId, pitch.NameKey }).IsUnique().HasDatabaseName(NameIndex);

        // Pitches are never deleted, and a club that has pitches is not deleted either.
        builder.HasOne<Club>().WithMany().HasForeignKey(pitch => pitch.ClubId).OnDelete(DeleteBehavior.Restrict);
    }

    private static Sport ParseSport(string code) =>
        Sports.TryParse(code, out var sport) ? sport : throw new InvalidOperationException($"Unknown sport code '{code}' in the database.");
}
