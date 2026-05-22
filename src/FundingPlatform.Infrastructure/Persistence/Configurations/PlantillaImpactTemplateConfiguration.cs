// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Plantilla ↔ ImpactTemplate M2M).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 021 / FR-003 — wires the many-to-many between <see cref="Plantilla"/>
/// and <see cref="ImpactTemplate"/> through the join table
/// <c>dbo.PlantillaImpactTemplates</c>. The base Plantilla collects candidate
/// ImpactTemplates here; <see cref="Plantilla.AssignTo"/> later flattens the
/// chosen ids into the <c>ProcessPlantilla.ImpactTemplateIdsCsv</c> snapshot.
/// </summary>
public class PlantillaImpactTemplateConfiguration : IEntityTypeConfiguration<Plantilla>
{
    public void Configure(EntityTypeBuilder<Plantilla> builder)
    {
        builder.HasMany(p => p.ImpactTemplates)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "PlantillaImpactTemplates",
                right => right.HasOne<ImpactTemplate>()
                    .WithMany()
                    .HasForeignKey("ImpactTemplateId")
                    .OnDelete(DeleteBehavior.NoAction),
                left => left.HasOne<Plantilla>()
                    .WithMany()
                    .HasForeignKey("PlantillaId")
                    .OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("PlantillaImpactTemplates");
                    join.HasKey("PlantillaId", "ImpactTemplateId");
                    join.Property<DateTimeOffset>("CreatedAt")
                        .HasDefaultValueSql("SYSUTCDATETIME()");
                });

        // The many-to-many is registered as a *skip*-navigation on the
        // parent type (Plantilla → ImpactTemplate via the join entity), so
        // FindNavigation returns null. FindSkipNavigation is the right hook,
        // and we set field-access there because `Plantilla.ImpactTemplates`
        // is a get-only property backed by the private `_impactTemplates`
        // list. (Pre-021 mistake — fix lands here as part of T074 because
        // the new EF round-trip surfaces it.)
        builder.Metadata
            .FindSkipNavigation(nameof(Plantilla.ImpactTemplates))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
