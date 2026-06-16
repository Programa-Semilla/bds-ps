// Spec 035 (evolved 2026-06-16) — see specs/035-line-item-category-templates/data-model.md (D13).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 035 / D13 — maps <see cref="ApplicationImpact"/> to <c>dbo.ApplicationImpacts</c>:
/// one declared impact per row (template + its values), one-or-more per application.
/// </summary>
public class ApplicationImpactConfiguration : IEntityTypeConfiguration<ApplicationImpact>
{
    public void Configure(EntityTypeBuilder<ApplicationImpact> builder)
    {
        builder.ToTable("ApplicationImpacts");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();

        builder.Property(i => i.ApplicationId).IsRequired();
        builder.Property(i => i.ImpactTemplateId).IsRequired();

        builder.HasIndex(i => i.ApplicationId)
            .HasDatabaseName("IX_ApplicationImpacts_ApplicationId");

        builder.HasIndex(i => new { i.ApplicationId, i.ImpactTemplateId })
            .IsUnique()
            .HasDatabaseName("UX_ApplicationImpacts_AppId_TemplateId");

        // No-action so deactivating/keeping a template never cascades into declared impacts.
        builder.HasOne(i => i.ImpactTemplate)
            .WithMany()
            .HasForeignKey(i => i.ImpactTemplateId)
            .OnDelete(DeleteBehavior.NoAction);

        // Declared-impact → its parameter values (EAV), cascade on impact delete.
        builder.HasMany(i => i.ParameterValues)
            .WithOne()
            .HasForeignKey(v => v.ApplicationImpactId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata
            .FindNavigation(nameof(ApplicationImpact.ParameterValues))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
