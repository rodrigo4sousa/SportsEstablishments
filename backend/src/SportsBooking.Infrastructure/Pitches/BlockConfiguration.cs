using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportsBooking.Domain.Pitches;

namespace SportsBooking.Infrastructure.Pitches;

internal sealed class BlockConfiguration : IEntityTypeConfiguration<Block>
{
    public void Configure(EntityTypeBuilder<Block> builder)
    {
        builder.ToTable("blocks", table => table.HasCheckConstraint("ck_blocks_ends_after_starts", "ends_at > starts_at"));

        builder.HasKey(block => block.Id);
        builder.Property(block => block.Id).ValueGeneratedNever();
        builder.Property(block => block.Reason).HasMaxLength(BlockRules.ReasonMaximumLength);

        // The list of a pitch's upcoming blocks, and the overlap checks under the pitch lock.
        builder.HasIndex(block => new { block.PitchId, block.StartsAt }).HasDatabaseName("ix_blocks_pitch_id_starts_at");

        // Blocks go with nothing: pitches are never deleted.
        builder.HasOne<Pitch>().WithMany().HasForeignKey(block => block.PitchId).OnDelete(DeleteBehavior.Restrict);
    }
}
