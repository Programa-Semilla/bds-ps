using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public class SupplierBranchConfiguration : IEntityTypeConfiguration<SupplierBranch>
{
    public void Configure(EntityTypeBuilder<SupplierBranch> builder)
    {
        builder.ToTable("SupplierBranches");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.SupplierId).IsRequired();
        builder.Property(b => b.BranchName).IsRequired().HasMaxLength(200);
        builder.Property(b => b.ContactName).HasMaxLength(200);
        builder.Property(b => b.Email).HasMaxLength(256);
        builder.Property(b => b.Phone).HasMaxLength(20);
        builder.Property(b => b.AddressLine).HasMaxLength(500);
        builder.Property(b => b.Province).HasMaxLength(100);
        builder.Property(b => b.ShippingDetails).HasMaxLength(500);
        builder.Property(b => b.WarrantyInfo).HasMaxLength(500);
        builder.Property(b => b.IsDefault).IsRequired();
        builder.Property(b => b.CreatedByApplicantId);
        builder.Property(b => b.CreatedAt).IsRequired();
        builder.Property(b => b.UpdatedAt).IsRequired();

        builder.HasIndex(b => b.SupplierId);

        // Filtered unique index — exactly one default branch per supplier (FR-021).
        // Mirrors UX_SupplierBranches_DefaultPerSupplier in dbo.SupplierBranches.sql.
        builder.HasIndex(b => b.SupplierId)
               .IsUnique()
               .HasFilter("[IsDefault] = 1")
               .HasDatabaseName("UX_SupplierBranches_DefaultPerSupplier");
    }
}
