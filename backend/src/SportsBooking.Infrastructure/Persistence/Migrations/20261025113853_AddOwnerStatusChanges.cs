using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerStatusChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "owner_status_changes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_owner_status_changes", x => x.id);
                    table.CheckConstraint("ck_owner_status_changes_action", "action IN ('Deactivated', 'Reactivated')");
                    table.ForeignKey(
                        name: "fk_owner_status_changes_users_admin_id",
                        column: x => x.admin_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_owner_status_changes_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_owner_status_changes_admin_id",
                table: "owner_status_changes",
                column: "admin_id");

            migrationBuilder.CreateIndex(
                name: "ix_owner_status_changes_owner_id_created_at",
                table: "owner_status_changes",
                columns: new[] { "owner_id", "created_at" });

            // US29: the audit is append-only. Nothing in the code updates or deletes a row, and the database refuses it too, so the
            // record cannot be changed even by a mistake or by hand. (A superuser can still drop the trigger: that is a deliberate act.)
            migrationBuilder.Sql("""
                CREATE FUNCTION owner_status_changes_append_only() RETURNS trigger AS $$
                BEGIN
                  RAISE EXCEPTION 'owner_status_changes is append-only: % is not allowed', TG_OP USING ERRCODE = 'restrict_violation';
                END;
                $$ LANGUAGE plpgsql;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER owner_status_changes_no_update_delete
                  BEFORE UPDATE OR DELETE ON owner_status_changes
                  FOR EACH ROW EXECUTE FUNCTION owner_status_changes_append_only();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER owner_status_changes_no_truncate
                  BEFORE TRUNCATE ON owner_status_changes
                  FOR EACH STATEMENT EXECUTE FUNCTION owner_status_changes_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "owner_status_changes");
            migrationBuilder.Sql("DROP FUNCTION owner_status_changes_append_only();");
        }
    }
}
