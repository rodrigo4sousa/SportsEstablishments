using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPitchPriceBands : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pitch_price_bands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pitch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    days_mask = table.Column<int>(type: "integer", nullable: false),
                    starts = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    ends = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pitch_price_bands", x => x.id);
                    table.CheckConstraint("ck_pitch_price_bands_days_mask", "days_mask BETWEEN 1 AND 127");
                    table.CheckConstraint("ck_pitch_price_bands_ends_after_starts", "ends = TIME '00:00' OR ends > starts");
                    table.CheckConstraint("ck_pitch_price_bands_price", "price > 0 AND price <= 500");
                    table.ForeignKey(
                        name: "fk_pitch_price_bands_pitches_pitch_id",
                        column: x => x.pitch_id,
                        principalTable: "pitches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pitch_price_bands_pitch_id",
                table: "pitch_price_bands",
                column: "pitch_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pitch_price_bands");
        }
    }
}
