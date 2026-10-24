using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pitch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_ends_after_starts", "ends_at > starts_at");
                    table.CheckConstraint("ck_bookings_has_player", "player_id IS NOT NULL");
                    table.CheckConstraint("ck_bookings_price", "price > 0");
                    table.CheckConstraint("ck_bookings_status", "status IN ('Confirmed', 'CancelledByPlayer', 'CancelledByClub', 'NoShow')");
                    table.ForeignKey(
                        name: "fk_bookings_pitches_pitch_id",
                        column: x => x.pitch_id,
                        principalTable: "pitches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bookings_users_player_id",
                        column: x => x.player_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_pitch_id_starts_at",
                table: "bookings",
                columns: new[] { "pitch_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_player_id",
                table: "bookings",
                column: "player_id");

            // T03: no two confirmed bookings of a pitch overlap, enforced by the database (docs/stack.md). EF has no
            // model for exclusion constraints, so this lives here only; a violation is SQLSTATE 23P01 (BookingStore).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql("""
                ALTER TABLE bookings ADD CONSTRAINT bookings_no_overlap
                  EXCLUDE USING gist (
                    pitch_id WITH =,
                    tstzrange(starts_at, ends_at) WITH &&
                  ) WHERE (status = 'Confirmed');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The constraint goes with the table; btree_gist stays, other objects may use it.
            migrationBuilder.DropTable(
                name: "bookings");
        }
    }
}
