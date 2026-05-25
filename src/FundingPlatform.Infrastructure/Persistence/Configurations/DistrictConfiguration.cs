// Spec 025 — see specs/025-supplier-location-cascade/data-model.md (District catalog).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 025 / FR-001 — maps <see cref="District"/> to <c>dbo.Districts</c>.
/// ~488 rows seeded via PostDeployment MERGE. Mirrors <see cref="CantonConfiguration"/>.
/// The cross-FK invariant (<c>District.CantonId = SupplierBranch.CantonId</c>) is
/// enforced in the domain on the branch aggregate, NOT here.
/// </summary>
public class DistrictConfiguration : IEntityTypeConfiguration<District>
{
    public void Configure(EntityTypeBuilder<District> builder)
    {
        builder.ToTable("Districts");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedOnAdd();

        builder.Property(d => d.CantonId).IsRequired();
        builder.Property(d => d.Code)
            .HasMaxLength(8)
            .IsFixedLength()
            .IsRequired();
        builder.Property(d => d.Name)
            .HasMaxLength(80)
            .IsRequired();

        builder.HasIndex(d => d.Code)
            .IsUnique()
            .HasDatabaseName("UX_Districts_Code");

        builder.HasIndex(d => d.CantonId)
            .HasDatabaseName("IX_Districts_CantonId");

        builder.HasOne(d => d.Canton)
            .WithMany()
            .HasForeignKey(d => d.CantonId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
