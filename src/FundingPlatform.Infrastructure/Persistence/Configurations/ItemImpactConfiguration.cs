// Spec 035 (evolved 2026-06-16) — see specs/035-line-item-category-templates/data-model.md (D14).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 035 / D14 — maps <see cref="ItemImpact"/> to <c>dbo.ItemImpacts</c>: the
/// per-item attribution join to the application's declared impacts. The
/// <c>ApplicationImpactId</c> FK is <c>NO ACTION</c> (not cascade) because
/// <c>Application</c> already reaches this table via <c>Items</c>; two cascade paths
/// are illegal in SQL Server. Attribution cleanup on impact removal is done in the
/// domain (<c>Application.RemoveImpact</c>). The Item→attribution cascade is owned by
/// <see cref="ItemConfiguration"/>.
/// </summary>
public class ItemImpactConfiguration : IEntityTypeConfiguration<ItemImpact>
{
    public void Configure(EntityTypeBuilder<ItemImpact> builder)
    {
        builder.ToTable("ItemImpacts");
        builder.HasKey(ii => ii.Id);
        builder.Property(ii => ii.Id).ValueGeneratedOnAdd();

        builder.Property(ii => ii.ItemId).IsRequired();
        builder.Property(ii => ii.ApplicationImpactId).IsRequired();

        builder.HasIndex(ii => ii.ApplicationImpactId)
            .HasDatabaseName("IX_ItemImpacts_ApplicationImpactId");

        builder.HasIndex(ii => new { ii.ItemId, ii.ApplicationImpactId })
            .IsUnique()
            .HasDatabaseName("UX_ItemImpacts_ItemId_AppImpactId");

        // ClientCascade: EF cascade-deletes the attribution in the change tracker when its
        // ApplicationImpact is removed (so Application.RemoveImpact works through EF), while
        // the DB FK stays NO ACTION (the hand-authored dacpac avoids the multi-cascade-path
        // conflict — Application reaches ItemImpacts via Items already).
        builder.HasOne(ii => ii.ApplicationImpact)
            .WithMany()
            .HasForeignKey(ii => ii.ApplicationImpactId)
            .OnDelete(DeleteBehavior.ClientCascade);
    }
}
