using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNoShowNotificationsAndReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pending_next_attempt_at",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_sent_or_failed",
                table: "outbox_messages");

            migrationBuilder.AddColumn<DateTime>(
                name: "skipped_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_on_new_booking",
                table: "clubs",
                type: "boolean",
                nullable: false,
                // US23: on by default, for the clubs that already exist too.
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending_next_attempt_at",
                table: "outbox_messages",
                column: "next_attempt_at",
                filter: "sent_at IS NULL AND failed_at IS NULL AND skipped_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_one_outcome",
                table: "outbox_messages",
                sql: "num_nonnulls(sent_at, failed_at, skipped_at) <= 1");

            // US21 and US23: one reminder and one owner's email per booking, whatever the code or a restart does. The payload holds
            // the booking id (jsonb, key "bookingId"); a second insert for the same booking and type is a unique violation.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_outbox_messages_booking_notification
                  ON outbox_messages (type, (payload ->> 'bookingId'))
                  WHERE type IN ('booking_reminder', 'owner_new_booking');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX ux_outbox_messages_booking_notification;");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pending_next_attempt_at",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_one_outcome",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "skipped_at",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "notify_on_new_booking",
                table: "clubs");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending_next_attempt_at",
                table: "outbox_messages",
                column: "next_attempt_at",
                filter: "sent_at IS NULL AND failed_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_sent_or_failed",
                table: "outbox_messages",
                sql: "sent_at IS NULL OR failed_at IS NULL");
        }
    }
}
