using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferredLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "preferred_language",
                table: "users",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_preferred_language",
                table: "users",
                sql: "preferred_language IS NULL OR preferred_language IN ('pt-PT', 'en')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_preferred_language",
                table: "users");

            migrationBuilder.DropColumn(
                name: "preferred_language",
                table: "users");
        }
    }
}
