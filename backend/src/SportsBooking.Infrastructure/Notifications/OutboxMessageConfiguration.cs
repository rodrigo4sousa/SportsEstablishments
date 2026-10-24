using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportsBooking.Application.Notifications;

namespace SportsBooking.Infrastructure.Notifications;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages", table =>
        {
            table.HasCheckConstraint("ck_outbox_messages_attempts", "attempts >= 0");
            table.HasCheckConstraint("ck_outbox_messages_sent_or_failed", "sent_at IS NULL OR failed_at IS NULL");
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Type).HasMaxLength(64).IsRequired();
        builder.Property(message => message.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(OutboxMessage.LastErrorMaximumLength);
        builder.Ignore(message => message.IsPending);

        // What the worker asks for: the pending messages that are due, oldest due first.
        builder.HasIndex(message => message.NextAttemptAt)
            .HasDatabaseName("ix_outbox_messages_pending_next_attempt_at")
            .HasFilter("sent_at IS NULL AND failed_at IS NULL");
    }
}
