using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 040 — maps <see cref="ChecklistTemplate"/> to <c>dbo.ChecklistTemplates</c>.
/// Mirrors <see cref="CategoryConfiguration"/>: an owned 1:N item set cascaded within
/// the aggregate, plus optimistic concurrency via <c>RowVersion</c>.
/// </summary>
public class ChecklistTemplateConfiguration : IEntityTypeConfiguration<ChecklistTemplate>
{
    public void Configure(EntityTypeBuilder<ChecklistTemplate> builder)
    {
        builder.ToTable("ChecklistTemplates");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(500);
        // Stored as TINYINT (dacpac); convert the int-backed enum to byte so SQL
        // materialization does not throw Byte→Int32 InvalidCastException.
        builder.Property(t => t.AppliesToStage).HasConversion<byte>().IsRequired();
        builder.Property(t => t.IsActive).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.CreatedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasMany(t => t.Items)
            .WithOne(i => i.ChecklistTemplate)
            .HasForeignKey(i => i.ChecklistTemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Items)
            .HasField("_items")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
