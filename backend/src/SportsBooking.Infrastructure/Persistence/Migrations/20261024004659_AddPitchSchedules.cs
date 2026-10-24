using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPitchSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "slot_duration_minutes",
                table: "pitches",
                type: "integer",
                nullable: false,
                // Existing pitches get the default slot length of a new pitch (US08).
                defaultValue: 60);

            migrationBuilder.CreateTable(
                name: "pitch_opening_hours",
                columns: table => new
                {
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    pitch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opens = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    closes = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pitch_opening_hours", x => new { x.pitch_id, x.day_of_week });
                    table.CheckConstraint("ck_pitch_opening_hours_closes_after_opens", "closes = TIME '00:00' OR closes > opens");
                    table.CheckConstraint("ck_pitch_opening_hours_day_of_week", "day_of_week BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "fk_pitch_opening_hours_pitches_pitch_id",
                        column: x => x.pitch_id,
                        principalTable: "pitches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_pitches_slot_duration_minutes",
                table: "pitches",
                sql: "slot_duration_minutes IN (30, 60, 90)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pitch_opening_hours");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pitches_slot_duration_minutes",
                table: "pitches");

            migrationBuilder.DropColumn(
                name: "slot_duration_minutes",
                table: "pitches");
        }
    }
}
