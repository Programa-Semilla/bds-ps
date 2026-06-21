using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 040 — maps <see cref="ChecklistTemplateItem"/> to <c>dbo.ChecklistTemplateItems</c>.
/// Mirrors <see cref="CategoryFieldConfiguration"/>. The template→items relationship is
/// owned by <see cref="ChecklistTemplateConfiguration"/>.
/// </summary>
public class ChecklistTemplateItemConfiguration : IEntityTypeConfiguration<ChecklistTemplateItem>
{
    public void Configure(EntityTypeBuilder<ChecklistTemplateItem> builder)
    {
        builder.ToTable("ChecklistTemplateItems");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.ChecklistTemplateId).IsRequired();
        builder.HasIndex(i => i.ChecklistTemplateId)
            .HasDatabaseName("IX_ChecklistTemplateItems_ChecklistTemplateId");

        builder.Property(i => i.Text).IsRequired().HasMaxLength(500);
        builder.Property(i => i.DisplayOrder).IsRequired();
        builder.Property(i => i.IsRequired).IsRequired();
        builder.Property(i => i.IsActive).IsRequired();
    }
}
