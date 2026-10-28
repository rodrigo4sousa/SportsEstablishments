using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Infrastructure.Pitches;

internal sealed class PitchPriceBandConfiguration : IEntityTypeConfiguration<PitchPriceBand>
{
    public void Configure(EntityTypeBuilder<PitchPriceBand> builder)
    {
        builder.ToTable("pitch_price_bands", table =>
        {
            table.HasCheckConstraint("ck_pitch_price_bands_days_mask", "days_mask BETWEEN 1 AND 127");
            // 00:00 as the end is midnight; bands past midnight are not supported (like opening hours).
            table.HasCheckConstraint("ck_pitch_price_bands_ends_after_starts", "ends = TIME '00:00' OR ends > starts");
            table.HasCheckConstraint("ck_pitch_price_bands_price", FormattableString.Invariant($"price > 0 AND price <= {PitchRules.MaximumPrice}"));
        });

        builder.HasKey(band => band.Id);
        builder.Property(band => band.Id).ValueGeneratedNever();
        builder.Property(band => band.Price).HasPrecision(10, PitchRules.PriceDecimals);
        builder.Ignore(band => band.Days);

        // The bands of a pitch are read with every read of the pitch (they decide its prices) and removed with a real delete.
        builder.HasOne<Pitch>().WithMany(pitch => pitch.PriceBands).HasForeignKey(band => band.PitchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(band => band.PitchId).HasDatabaseName("ix_pitch_price_bands_pitch_id");
    }
}
