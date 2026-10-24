using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndexBookingsByPlayerAndStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_player_id",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_player_id_starts_at",
                table: "bookings",
                columns: new[] { "player_id", "starts_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_player_id_starts_at",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_player_id",
                table: "bookings",
                column: "player_id");
        }
    }
}
