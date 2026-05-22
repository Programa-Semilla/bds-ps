// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Canton catalog).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 021 / FR-014 — maps <see cref="Canton"/> to <c>dbo.Cantons</c>.
/// ~82 rows seeded via PostDeployment MERGE. Cross-FK invariant
/// (<c>Canton.ProvinceId = SupplierBranch.ProvinceId</c>) is enforced in
/// the domain on the branch aggregate, NOT here.
/// </summary>
public class CantonConfiguration : IEntityTypeConfiguration<Canton>
{
    public void Configure(EntityTypeBuilder<Canton> builder)
    {
        builder.ToTable("Cantons");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();

        builder.Property(c => c.ProvinceId).IsRequired();
        builder.Property(c => c.Code)
            .HasMaxLength(5)
            .IsFixedLength()
            .IsRequired();
        builder.Property(c => c.Name)
            .HasMaxLength(60)
            .IsRequired();

        builder.HasIndex(c => c.Code)
            .IsUnique()
            .HasDatabaseName("UX_Cantons_Code");

        builder.HasIndex(c => c.ProvinceId)
            .HasDatabaseName("IX_Cantons_ProvinceId");

        // Inverse side (Province.Cantons) configured in ProvinceConfiguration.
    }
}
