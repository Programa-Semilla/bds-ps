// Spec 029 — see specs/029-fund-entity/data-model.md (Fund aggregate).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 029 / FR-001 — maps <see cref="Fund"/> to <c>dbo.Funds</c>. Catalog
/// uniqueness on <c>Name</c> is enforced by the dacpac unique index
/// <c>UX_Funds_Name</c>; the regulation reference is a set of optional columns on
/// the aggregate (single document per Fund, mirrors FundingAgreement).
/// </summary>
public class FundConfiguration : IEntityTypeConfiguration<Fund>
{
    public void Configure(EntityTypeBuilder<Fund> builder)
    {
        builder.ToTable("Funds");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedOnAdd();

        builder.Property(f => f.Name)
            .HasMaxLength(Fund.MaxNameLength)
            .IsRequired();

        builder.Property(f => f.Description)
            .HasMaxLength(Fund.MaxDescriptionLength)
            .IsRequired();

        builder.Property(f => f.Status).IsRequired().HasConversion<byte>();

        builder.Property(f => f.RegulationBlobKey).HasMaxLength(1024);
        builder.Property(f => f.RegulationFileName).HasMaxLength(260);
        builder.Property(f => f.RegulationContentType).HasMaxLength(100);
        builder.Property(f => f.RegulationSizeBytes);
        builder.Property(f => f.RegulationUploadedAtUtc);
        builder.Property(f => f.RegulationUploadedByUserId).HasMaxLength(450);

        builder.Property(f => f.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(f => f.RowVersion).IsRowVersion();

        builder.HasIndex(f => f.Name)
            .IsUnique()
            .HasDatabaseName("UX_Funds_Name");

        // One-to-many: Fund → Processes (Process.FundId FK).
        builder.HasMany(f => f.Processes)
            .WithOne(p => p.Fund!)
            .HasForeignKey(p => p.FundId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Metadata
            .FindNavigation(nameof(Fund.Processes))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
