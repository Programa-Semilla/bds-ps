// Spec 047 — see specs/047-evidence-graph-required-docs/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 047 — maps <see cref="EvidenceLineAllocation"/> to <c>dbo.EvidenceLineAllocations</c>, the
/// M:N evidence↔line join. Mirrors <see cref="DisbursementLineAllocationConfiguration"/>'s FK
/// topology exactly: the <c>Evidence</c> FK is CASCADE (single ownership path), the <c>Item</c> FK is
/// ClientCascade (the DB FK stays NO ACTION), so <c>Application</c> reaching this table via both
/// <c>Evidence</c> and <c>Items</c> does not trip the multiple-cascade-path publish failure. No
/// navigation collection is exposed on <see cref="Evidence"/>; the service persists the row set
/// directly (replace-all on Allocate).
/// </summary>
public sealed class EvidenceLineAllocationConfiguration : IEntityTypeConfiguration<EvidenceLineAllocation>
{
    public void Configure(EntityTypeBuilder<EvidenceLineAllocation> builder)
    {
        builder.ToTable("EvidenceLineAllocations");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EvidenceId).IsRequired();
        builder.Property(a => a.ItemId).IsRequired();
        builder.Property(a => a.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.ItemId).HasDatabaseName("IX_EvidenceLineAlloc_ItemId");
        builder.HasIndex(a => new { a.EvidenceId, a.ItemId })
            .IsUnique()
            .HasDatabaseName("UX_EvidenceLineAlloc_Evidence_Item");

        builder.HasOne<Evidence>()
            .WithMany()
            .HasForeignKey(a => a.EvidenceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(a => a.ItemId)
            .OnDelete(DeleteBehavior.ClientCascade);
    }
}
