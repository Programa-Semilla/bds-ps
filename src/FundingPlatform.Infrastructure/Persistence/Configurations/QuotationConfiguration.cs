using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public class QuotationConfiguration : IEntityTypeConfiguration<Quotation>
{
    public void Configure(EntityTypeBuilder<Quotation> builder)
    {
        builder.ToTable("Quotations");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.ItemId).IsRequired();
        builder.Property(q => q.SupplierId).IsRequired();
        builder.Property(q => q.SupplierBranchId).IsRequired();
        builder.Property(q => q.Price).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(q => q.ValidUntil).IsRequired();
        builder.Property(q => q.DocumentId).IsRequired();
        // Spec 015 column-type note: the dacpac post-deploy tightens this to
        // CHAR(3) NOT NULL with a FK to dbo.Currencies(Code). EF must agree on the
        // type so model-validation against the live schema does not flag a drift.
        builder.Property(q => q.Currency)
            .IsRequired()
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsFixedLength();
        builder.Property(q => q.CreatedAt).IsRequired();

        // Spec 015 — multi-currency snapshot fields.
        builder.Property(q => q.ConvertedCrcAmount).HasColumnType("decimal(18,2)");
        builder.Property(q => q.LegacyNeedsReview).IsRequired();

        // Embedded ExchangeRateSnapshot. OwnsOne maps the four snapshot columns
        // declaratively without a separate table. Property is shadowed via the
        // Quotation's private setter so the entity owns mutation.
        builder.OwnsOne(q => q.Snapshot, s =>
        {
            s.Property(p => p.RateRecordId)
                .HasColumnName("SnapshotRateId");
            s.Property(p => p.RateValue)
                .HasColumnName("SnapshotRateValue")
                .HasColumnType("decimal(18,6)");
            s.Property(p => p.RateType)
                .HasColumnName("SnapshotRateType")
                .HasConversion<byte>();
            s.Property(p => p.EffectiveAtUtc)
                .HasColumnName("SnapshotEffectiveAtUtc")
                .HasColumnType("datetime2(3)");
        });

        // Spec 039 — delivery lead time and warranty as owned TimeDuration value
        // objects mapped to flat columns (mirrors the Snapshot OwnsOne above). Units
        // stored as TINYINT via HasConversion<byte>. Both required; the entity
        // guarantees they are always set on a constructed quotation.
        builder.OwnsOne(q => q.DeliveryLeadTime, d =>
        {
            d.Property(x => x.Value)
                .HasColumnName("DeliveryLeadTimeValue")
                .IsRequired();
            d.Property(x => x.Unit)
                .HasColumnName("DeliveryLeadTimeUnit")
                .HasConversion<byte>()
                .IsRequired();
        });
        builder.Navigation(q => q.DeliveryLeadTime).IsRequired();

        builder.OwnsOne(q => q.Warranty, w =>
        {
            w.Property(x => x.Value)
                .HasColumnName("WarrantyValue")
                .IsRequired();
            w.Property(x => x.Unit)
                .HasColumnName("WarrantyUnit")
                .HasConversion<byte>()
                .IsRequired();
        });
        builder.Navigation(q => q.Warranty).IsRequired();

        builder.HasIndex(q => new { q.ItemId, q.SupplierId })
            .IsUnique()
            .HasDatabaseName("UX_Quotations_ItemId_SupplierId");

        builder.HasIndex(q => q.SupplierBranchId)
            .HasDatabaseName("IX_Quotations_SupplierBranchId");

        builder.HasOne(q => q.Supplier)
            .WithMany()
            .HasForeignKey(q => q.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(q => q.SupplierBranch)
            .WithMany()
            .HasForeignKey(q => q.SupplierBranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(q => q.Document)
            .WithMany()
            .HasForeignKey(q => q.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Spec 015 FK to ExchangeRates via the snapshot's RateRecordId column.
        // Declared as a shadow FK because the FK target lives inside the OwnsOne
        // value object; EF creates the constraint name to match the dacpac.
    }
}
