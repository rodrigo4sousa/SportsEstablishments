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
            table.HasCheckConstraint("ck_pitches_slot_duration_minutes", "slot_duration_minutes IN (30, 60, 90)");
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
        builder.Ignore(pitch => pitch.SlotDuration);

        // US08: one row per open weekday, local times without a zone (T04); a weekday without a row is closed.
        builder.OwnsMany(pitch => pitch.OpeningDays, day =>
        {
            day.ToTable("pitch_opening_hours", table =>
            {
                table.HasCheckConstraint("ck_pitch_opening_hours_day_of_week", "day_of_week BETWEEN 0 AND 6");
                // 00:00 as closing is midnight; hours past midnight are not supported (T04).
                table.HasCheckConstraint("ck_pitch_opening_hours_closes_after_opens", "closes = TIME '00:00' OR closes > opens");
            });
            day.WithOwner().HasForeignKey("PitchId");
            day.Property<Guid>("PitchId");
            day.Property(openingDay => openingDay.Day).HasColumnName("day_of_week");
            day.HasKey("PitchId", nameof(PitchOpeningDay.Day));
            day.Ignore(openingDay => openingDay.Hours);
        });
        builder.Navigation(pitch => pitch.OpeningDays).UsePropertyAccessMode(PropertyAccessMode.Field);

        // US10: the price bands decide the prices of the pitch, so every read of a pitch brings them (a few rows at most).
        builder.Navigation(pitch => pitch.PriceBands).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
        builder.Ignore(pitch => pitch.HasPriceBands);

        builder.HasIndex(pitch => new { pitch.ClubId, pitch.NameKey }).IsUnique().HasDatabaseName(NameIndex);

        // Pitches are never deleted, and a club that has pitches is not deleted either.
        builder.HasOne<Club>().WithMany().HasForeignKey(pitch => pitch.ClubId).OnDelete(DeleteBehavior.Restrict);
    }

    private static Sport ParseSport(string code) =>
        Sports.TryParse(code, out var sport) ? sport : throw new InvalidOperationException($"Unknown sport code '{code}' in the database.");
}
