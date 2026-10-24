using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_has_player",
                table: "bookings");

            migrationBuilder.AddColumn<string>(
                name: "customer_name",
                table: "bookings",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "customer_phone",
                table: "bookings",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_player_or_customer",
                table: "bookings",
                sql: "(player_id IS NOT NULL AND customer_name IS NULL AND customer_phone IS NULL) OR (player_id IS NULL AND customer_name IS NOT NULL AND customer_phone IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_player_or_customer",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "customer_name",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "customer_phone",
                table: "bookings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_has_player",
                table: "bookings",
                sql: "player_id IS NOT NULL");
        }
    }
}
