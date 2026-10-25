using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SportsBooking.Application.Admin;
using SportsBooking.Infrastructure.Identity;

namespace SportsBooking.Infrastructure.Admin;

/// <summary>
/// One row of the audit of an owner's status (US29): the owner, what happened, why, which admin did it and when. The row is
/// only ever created: there is no update or delete anywhere in the code, and a trigger in the database rejects them too
/// (see the <c>AddOwnerStatusChanges</c> migration), so the record cannot be edited, even by a mistake in the code.
/// </summary>
public sealed class OwnerStatusChange
{
    private OwnerStatusChange()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public OwnerStatusAction Action { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public Guid AdminId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public static OwnerStatusChange Record(Guid ownerId, OwnerStatusAction action, string reason, Guid adminId, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        OwnerId = ownerId,
        Action = action,
        Reason = reason,
        AdminId = adminId,
        CreatedAt = now,
    };
}

internal sealed class OwnerStatusChangeConfiguration : IEntityTypeConfiguration<OwnerStatusChange>
{
    public void Configure(EntityTypeBuilder<OwnerStatusChange> builder)
    {
        builder.ToTable("owner_status_changes", table => table.HasCheckConstraint(
            "ck_owner_status_changes_action", "action IN ('Deactivated', 'Reactivated')"));
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Action).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(change => change.Reason).HasMaxLength(OwnerStatusRequest.ReasonMaximumLength).IsRequired();

        // The accounts are never deleted, and the audit never lets them be (the foreign keys refuse).
        builder.HasOne<AppUser>().WithMany().HasForeignKey(change => change.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(change => change.AdminId).OnDelete(DeleteBehavior.Restrict);

        // The history of one owner, newest first.
        builder.HasIndex(change => new { change.OwnerId, change.CreatedAt });
    }
}
