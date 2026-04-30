using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.LegalId).IsRequired().HasMaxLength(50);
        builder.HasIndex(s => s.LegalId).IsUnique().HasDatabaseName("UX_Suppliers_LegalId");

        builder.Property(s => s.Name).IsRequired().HasMaxLength(300);

        builder.Property(s => s.HasElectronicInvoice).IsRequired();
        builder.Property(s => s.IsCompliantCCSS).IsRequired();
        builder.Property(s => s.IsCompliantHacienda).IsRequired();
        builder.Property(s => s.IsCompliantSICOP).IsRequired();

        // Spec 013 lifecycle.
        builder.Property(s => s.VerificationStatus)
               .HasConversion<byte>()
               .IsRequired();
        builder.Property(s => s.CreatedByApplicantId);
        builder.Property(s => s.VerifiedByUserId).HasMaxLength(450);
        builder.Property(s => s.VerifiedAt);
        builder.Property(s => s.RejectionReason).HasMaxLength(1000);

        builder.Property(s => s.CreatedAt).IsRequired();
        builder.Property(s => s.UpdatedAt).IsRequired();

        // 1:N branches (aggregate boundary). Backing field gives the entity sole
        // write authority via Supplier.AddBranch / EditBranch.
        builder.HasMany(s => s.Branches)
               .WithOne()
               .HasForeignKey(b => b.SupplierId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Branches)
               .HasField("_branches")
               .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
