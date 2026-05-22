// Spec 021 — see specs/021-feedback-session-may13/data-model.md
// (ImpactParameterValue re-parented from Impact to Application).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 021 / FR-005 — maps <see cref="ImpactParameterValue"/> to
/// <c>dbo.ImpactParameterValues</c>. The FK is re-parented from the legacy
/// <c>ImpactId</c> (dropped with the <c>dbo.Impacts</c> table) to a shadow
/// <c>ApplicationId</c> FK declared by the inverse side in
/// <see cref="ApplicationConfiguration"/>.
///
/// The Domain entity still carries vestigial <c>ImpactId</c> + <c>Impact</c>
/// members from the pre-021 model. We <see cref="EntityTypeBuilder.Ignore"/>
/// them so EF does not attempt to materialise a column / navigation.
/// </summary>
public class ImpactParameterValueConfiguration : IEntityTypeConfiguration<ImpactParameterValue>
{
    public void Configure(EntityTypeBuilder<ImpactParameterValue> builder)
    {
        builder.ToTable("ImpactParameterValues");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedOnAdd();

        // Spec 021 — unmap the dead-from-Phase-2a fields. The schema column is
        // gone; only the C# property remains, scheduled for removal in a later
        // domain-cleanup pass.
        builder.Ignore(v => v.ImpactId);
        builder.Ignore(v => v.Impact);

        builder.Property(v => v.ImpactTemplateParameterId).IsRequired();
        builder.Property(v => v.Value);

        // FK to Application configured by the inverse side
        // (ApplicationConfiguration.HasMany(a => a.ImpactParameterValues)
        //  .WithOne().HasForeignKey("ApplicationId"));
        // declare the shadow property explicitly here so its NOT NULL bit
        // matches the dacpac column.
        builder.Property<int>("ApplicationId").IsRequired();
        builder.HasIndex("ApplicationId")
            .HasDatabaseName("IX_ImpactParameterValues_ApplicationId");

        builder.HasIndex("ApplicationId", nameof(ImpactParameterValue.ImpactTemplateParameterId))
            .IsUnique()
            .HasDatabaseName("UX_ImpactParamValues_AppId_ParamId");

        builder.HasOne(v => v.ImpactTemplateParameter)
            .WithMany()
            .HasForeignKey(v => v.ImpactTemplateParameterId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
