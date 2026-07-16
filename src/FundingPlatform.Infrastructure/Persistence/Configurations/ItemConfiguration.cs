using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("Items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.ApplicationId).IsRequired();
        builder.HasIndex(i => i.ApplicationId).HasDatabaseName("IX_Items_ApplicationId");

        // Spec 018 / FR-013 — reviewer-assigned line code, nullable until assigned,
        // ≤16 chars. Per-Application uniqueness is enforced by the filtered unique
        // index (see below) so the reviewer flow can rely on the DB to backstop the
        // aggregate-root invariant under concurrency.
        builder.Property(i => i.LineCode).HasMaxLength(16).IsRequired(false);
        builder.HasIndex(i => new { i.ApplicationId, i.LineCode })
            .IsUnique()
            .HasFilter("[LineCode] IS NOT NULL")
            .HasDatabaseName("UX_Items_Application_LineCode");

        builder.Property(i => i.ProductName).IsRequired().HasMaxLength(500);

        builder.Property(i => i.CategoryId).IsRequired();
        builder.HasIndex(i => i.CategoryId).HasDatabaseName("IX_Items_CategoryId");

        // Spec 035 (evolved 2026-06-16, FR-008) — single short per-item justification (≤300).
        builder.Property(i => i.ImpactJustification).HasMaxLength(Item.ImpactJustificationMaxLength);

        builder.Property(i => i.ReviewStatus).IsRequired().HasDefaultValue(Domain.Enums.ItemReviewStatus.Pending);
        builder.Property(i => i.ReviewComment).HasMaxLength(2000);
        builder.Property(i => i.SelectedSupplierId);
        builder.Property(i => i.IsNotTechnicallyEquivalent).IsRequired().HasDefaultValue(false);

        // Spec 046 — off-ledger commit status. HasConversion<byte>() is MANDATORY (035/040/045
        // Byte→Int32 lesson: EF-InMemory hides it, real SQL throws on materialization).
        builder.Property(i => i.CommitState)
            .HasConversion<byte>()
            .IsRequired()
            .HasDefaultValue(Domain.Enums.ItemCommitState.Uncommitted);

        // Spec 046 — tranche membership (null = synthetic default). FK Restrict: deleting a tranche
        // re-parents its lines to null in the domain first (Application.DeleteTranche), so EF never
        // cascades here. Filtered index mirrors the dacpac IX_Items_TrancheId.
        builder.Property(i => i.TrancheId);
        builder.HasOne<Tranche>()
            .WithMany()
            .HasForeignKey(i => i.TrancheId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.TrancheId)
            .HasFilter("[TrancheId] IS NOT NULL")
            .HasDatabaseName("IX_Items_TrancheId");

        builder.Property(i => i.CreatedAt).IsRequired();
        builder.Property(i => i.UpdatedAt).IsRequired();

        builder.HasOne(i => i.Category)
            .WithMany()
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Spec 035 (evolved 2026-06-16, D14) — per-item impact ATTRIBUTION (join to the
        // application's declared impacts), cascade on item delete. The ApplicationImpact
        // side is NO ACTION (configured on ItemImpact) to avoid a multi-cascade path.
        builder.HasMany(i => i.ItemImpacts)
            .WithOne()
            .HasForeignKey(ii => ii.ItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata
            .FindNavigation(nameof(Item.ItemImpacts))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Spec 035 / D1 — per-item category field values (EAV), cascade on item delete.
        builder.HasMany(i => i.CategoryFieldValues)
            .WithOne()
            .HasForeignKey(v => v.ItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata
            .FindNavigation(nameof(Item.CategoryFieldValues))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(i => i.Quotations)
            .WithOne()
            .HasForeignKey(q => q.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.SelectedSupplier)
            .WithMany()
            .HasForeignKey(i => i.SelectedSupplierId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
