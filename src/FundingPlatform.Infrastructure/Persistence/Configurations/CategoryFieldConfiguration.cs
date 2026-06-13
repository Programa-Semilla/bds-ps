using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 035 — maps <see cref="CategoryField"/> to <c>dbo.CategoryFields</c>.
/// Mirrors <see cref="ImpactTemplateParameterConfiguration"/>. The Category→Fields
/// relationship is owned by <see cref="CategoryConfiguration"/>.
/// </summary>
public class CategoryFieldConfiguration : IEntityTypeConfiguration<CategoryField>
{
    public void Configure(EntityTypeBuilder<CategoryField> builder)
    {
        builder.ToTable("CategoryFields");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.CategoryId).IsRequired();
        builder.HasIndex(f => f.CategoryId).HasDatabaseName("IX_CategoryFields_CategoryId");

        builder.Property(f => f.Name).IsRequired().HasMaxLength(200);
        builder.Property(f => f.DisplayLabel).IsRequired().HasMaxLength(300);
        builder.Property(f => f.DataType).IsRequired();
        builder.Property(f => f.IsRequired).IsRequired();
        builder.Property(f => f.SortOrder).IsRequired();
    }
}
