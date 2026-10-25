using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRegisteredAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "registered_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: false,
                // Accounts that exist already were created some time ago: when this migration ran is the best answer left.
                defaultValueSql: "now()");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "registered_at",
                table: "users");
        }
    }
}
