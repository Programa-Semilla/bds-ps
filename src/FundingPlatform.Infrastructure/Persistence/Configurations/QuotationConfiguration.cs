using FundingPlatform.Domain.Entities;
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
        builder.Property(q => q.Currency)
            .IsRequired()
            .HasColumnType("NVARCHAR(3)")
            .HasMaxLength(3);
        builder.Property(q => q.CreatedAt).IsRequired();

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
    }
}
