// Spec 035 (evolved 2026-06-16) — see specs/035-line-item-category-templates/data-model.md
// (ImpactParameterValue re-keyed to ApplicationImpact — impact data is application-level).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 035 / D13 — maps <see cref="ImpactParameterValue"/> to
/// <c>dbo.ImpactParameterValues</c>, keyed by an explicit <c>ApplicationImpactId</c> FK
/// (impact data collection lives at the application level). The
/// ApplicationImpact→values relationship + cascade is owned by
/// <see cref="ApplicationImpactConfiguration"/>.
/// </summary>
public class ImpactParameterValueConfiguration : IEntityTypeConfiguration<ImpactParameterValue>
{
    public void Configure(EntityTypeBuilder<ImpactParameterValue> builder)
    {
        builder.ToTable("ImpactParameterValues");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedOnAdd();

        builder.Property(v => v.ApplicationImpactId).IsRequired();
        builder.Property(v => v.ImpactTemplateParameterId).IsRequired();
        builder.Property(v => v.Value);

        builder.HasIndex(v => v.ApplicationImpactId)
            .HasDatabaseName("IX_ImpactParameterValues_ApplicationImpactId");

        builder.HasIndex(v => new { v.ApplicationImpactId, v.ImpactTemplateParameterId })
            .IsUnique()
            .HasDatabaseName("UX_ImpactParamValues_AppImpactId_ParamId");

        builder.HasOne(v => v.ImpactTemplateParameter)
            .WithMany()
            .HasForeignKey(v => v.ImpactTemplateParameterId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
