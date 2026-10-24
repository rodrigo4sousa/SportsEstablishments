using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportsBooking.Domain.Bookings;
using SportsBooking.Domain.Pitches;
using SportsBooking.Infrastructure.Identity;

namespace SportsBooking.Infrastructure.Bookings;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    /// <summary>
    /// T03: two confirmed bookings of one pitch never overlap. Created in the migration with raw SQL (EF has no model
    /// for exclusion constraints); a violation is PostgreSQL 23P01, turned into 409 <c>slot_taken</c> by BookingStore.
    /// </summary>
    public const string NoOverlapConstraint = "bookings_no_overlap";

    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings", table =>
        {
            table.HasCheckConstraint("ck_bookings_ends_after_starts", "ends_at > starts_at");
            table.HasCheckConstraint("ck_bookings_price", "price > 0");
            table.HasCheckConstraint(
                "ck_bookings_status",
                $"status IN ({string.Join(", ", Enum.GetNames<BookingStatus>().Select(name => $"'{name}'"))})");
            // US13 replaces this with "a player, or a customer name and phone".
            table.HasCheckConstraint("ck_bookings_has_player", "player_id IS NOT NULL");
        });

        builder.HasKey(booking => booking.Id);
        builder.Property(booking => booking.Id).ValueGeneratedNever();
        builder.Property(booking => booking.Price).HasPrecision(10, 2);
        // As text, so the constraint's WHERE (status = 'Confirmed') works (docs/stack.md).
        builder.Property(booking => booking.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(booking => booking.CancellationReason).HasMaxLength(BookingRules.CancellationReasonMaximumLength);
        builder.Ignore(booking => booking.IsConfirmed);

        // A pitch's bookings in time order: overlap checks, counts and, later, the calendar.
        builder.HasIndex(booking => new { booking.PitchId, booking.StartsAt }).HasDatabaseName("ix_bookings_pitch_id_starts_at");
        // A player's bookings in time order: "my bookings" (US19). Also serves lookups by player alone.
        builder.HasIndex(booking => new { booking.PlayerId, booking.StartsAt }).HasDatabaseName("ix_bookings_player_id_starts_at");

        // Bookings are never deleted, and neither are pitches or accounts that have them.
        builder.HasOne<Pitch>().WithMany().HasForeignKey(booking => booking.PitchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(booking => booking.PlayerId).OnDelete(DeleteBehavior.Restrict);
    }
}
