// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Province catalog).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 021 / FR-014 — maps <see cref="Province"/> to <c>dbo.Provinces</c>.
/// Static catalog of Costa Rica's seven provinces; rows are seeded via the
/// PostDeployment script and never user-mutated in scope 021.
/// </summary>
public class ProvinceConfiguration : IEntityTypeConfiguration<Province>
{
    public void Configure(EntityTypeBuilder<Province> builder)
    {
        builder.ToTable("Provinces");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        builder.Property(p => p.Code)
            .HasMaxLength(2)
            .IsFixedLength()
            .IsRequired();
        builder.Property(p => p.Name)
            .HasMaxLength(40)
            .IsRequired();

        builder.HasIndex(p => p.Code)
            .IsUnique()
            .HasDatabaseName("UX_Provinces_Code");

        builder.HasMany(p => p.Cantons)
            .WithOne(c => c.Province)
            .HasForeignKey(c => c.ProvinceId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Metadata
            .FindNavigation(nameof(Province.Cantons))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
