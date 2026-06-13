using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 035 — maps <see cref="CategoryFieldValue"/> to
/// <c>dbo.CategoryFieldValues</c> (EAV, keyed by Item). Mirrors
/// <see cref="ImpactParameterValueConfiguration"/>. The Item→values relationship
/// (and the cascade) is owned by <see cref="ItemConfiguration"/>.
/// </summary>
public class CategoryFieldValueConfiguration : IEntityTypeConfiguration<CategoryFieldValue>
{
    public void Configure(EntityTypeBuilder<CategoryFieldValue> builder)
    {
        builder.ToTable("CategoryFieldValues");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedOnAdd();

        builder.Property(v => v.ItemId).IsRequired();
        builder.Property(v => v.CategoryFieldId).IsRequired();
        builder.Property(v => v.Value);

        builder.HasIndex(v => v.ItemId)
            .HasDatabaseName("IX_CategoryFieldValues_ItemId");

        builder.HasIndex(v => new { v.ItemId, v.CategoryFieldId })
            .IsUnique()
            .HasDatabaseName("UX_CategoryFieldValues_ItemId_FieldId");

        builder.HasOne(v => v.CategoryField)
            .WithMany()
            .HasForeignKey(v => v.CategoryFieldId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
