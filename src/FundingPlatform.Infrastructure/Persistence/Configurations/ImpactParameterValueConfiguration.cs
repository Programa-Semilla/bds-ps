// Spec 035 — see specs/035-line-item-category-templates/data-model.md
// (ImpactParameterValue re-keyed from Application to Item).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 035 / D2 — maps <see cref="ImpactParameterValue"/> to
/// <c>dbo.ImpactParameterValues</c>, re-keyed from the legacy shadow
/// <c>ApplicationId</c> to an explicit <c>ItemId</c> FK (impact relocated to the
/// line item). The Item→values relationship + cascade is owned by
/// <see cref="ItemConfiguration"/>.
/// </summary>
public class ImpactParameterValueConfiguration : IEntityTypeConfiguration<ImpactParameterValue>
{
    public void Configure(EntityTypeBuilder<ImpactParameterValue> builder)
    {
        builder.ToTable("ImpactParameterValues");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedOnAdd();

        builder.Property(v => v.ItemId).IsRequired();
        builder.Property(v => v.ImpactTemplateParameterId).IsRequired();
        builder.Property(v => v.Value);

        builder.HasIndex(v => v.ItemId)
            .HasDatabaseName("IX_ImpactParameterValues_ItemId");

        builder.HasIndex(v => new { v.ItemId, v.ImpactTemplateParameterId })
            .IsUnique()
            .HasDatabaseName("UX_ImpactParamValues_ItemId_ParamId");

        builder.HasOne(v => v.ImpactTemplateParameter)
            .WithMany()
            .HasForeignKey(v => v.ImpactTemplateParameterId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
