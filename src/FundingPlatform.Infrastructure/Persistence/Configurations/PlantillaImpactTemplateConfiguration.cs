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

        builder.Metadata
            .FindNavigation(nameof(Plantilla.ImpactTemplates))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
