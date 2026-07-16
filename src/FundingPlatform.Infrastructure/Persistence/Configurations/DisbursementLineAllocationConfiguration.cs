// Spec 046 — see specs/046-tranches-budget-lines/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 046 — maps <see cref="DisbursementLineAllocation"/> to <c>dbo.DisbursementLineAllocations</c>,
/// the M:N payment↔line join. Mirrors <see cref="ItemImpactConfiguration"/>'s FK topology: the
/// <c>Disbursement</c> FK is CASCADE (single ownership path), the <c>Item</c> FK is ClientCascade
/// (the DB FK stays NO ACTION, so <c>Application</c> reaching this table via both
/// <c>Disbursements</c> and <c>Items</c> does not trip the multiple-cascade-path publish failure).
/// No navigation collection is exposed on <see cref="Disbursement"/>; the service persists the row
/// set directly (replace-all on Record/Edit), like <c>DisbursementEvidence</c>.
/// </summary>
public sealed class DisbursementLineAllocationConfiguration : IEntityTypeConfiguration<DisbursementLineAllocation>
{
    public void Configure(EntityTypeBuilder<DisbursementLineAllocation> builder)
    {
        builder.ToTable("DisbursementLineAllocations");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.DisbursementId).IsRequired();
        builder.Property(a => a.ItemId).IsRequired();
        builder.Property(a => a.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasIndex(a => a.ItemId).HasDatabaseName("IX_DisbLineAlloc_ItemId");
        builder.HasIndex(a => new { a.DisbursementId, a.ItemId })
            .IsUnique()
            .HasDatabaseName("UX_DisbLineAlloc_Disbursement_Item");

        builder.HasOne<Disbursement>()
            .WithMany()
            .HasForeignKey(a => a.DisbursementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Item>()
            .WithMany()
            .HasForeignKey(a => a.ItemId)
            .OnDelete(DeleteBehavior.ClientCascade);
    }
}
