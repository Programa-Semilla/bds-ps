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

        builder.Property(i => i.TechnicalSpecifications).IsRequired();

        builder.Property(i => i.ReviewStatus).IsRequired().HasDefaultValue(Domain.Enums.ItemReviewStatus.Pending);
        builder.Property(i => i.ReviewComment).HasMaxLength(2000);
        builder.Property(i => i.SelectedSupplierId);
        builder.Property(i => i.IsNotTechnicallyEquivalent).IsRequired().HasDefaultValue(false);

        builder.Property(i => i.CreatedAt).IsRequired();
        builder.Property(i => i.UpdatedAt).IsRequired();

        builder.HasOne(i => i.Category)
            .WithMany()
            .HasForeignKey(i => i.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(i => i.Impact)
            .WithOne(imp => imp.Item)
            .HasForeignKey<Impact>(imp => imp.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

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
