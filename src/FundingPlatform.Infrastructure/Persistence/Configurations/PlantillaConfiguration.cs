// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Plantilla aggregate)
// and research.md OQ-1 (one ProcessPlantilla per Process).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 021 / FR-003 — maps <see cref="Plantilla"/> to <c>dbo.Plantillas</c>.
/// The many-to-many to <c>ImpactTemplate</c> is wired in
/// <see cref="PlantillaImpactTemplateConfiguration"/> (join table
/// <c>dbo.PlantillaImpactTemplates</c>).
/// </summary>
public class PlantillaConfiguration : IEntityTypeConfiguration<Plantilla>
{
    public void Configure(EntityTypeBuilder<Plantilla> builder)
    {
        builder.ToTable("Plantillas");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        builder.Property(p => p.Name)
            .HasMaxLength(Plantilla.MaxNameLength)
            .IsRequired();

        builder.Property(p => p.MinimumQuotationsPerItem).IsRequired();
        builder.Property(p => p.RequiredFieldFlags).IsRequired();
        builder.Property(p => p.IsArchived).IsRequired();
        builder.Property(p => p.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasIndex(p => p.Name)
            .IsUnique()
            .HasDatabaseName("UX_Plantillas_Name");
    }
}
