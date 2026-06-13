// Spec 021 — see specs/021-feedback-session-may13/data-model.md (ProcessPlantilla snapshot)
// and research.md OQ-1 (one-to-one cardinality with Process).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 021 / FR-004 / OQ-1 — maps the frozen
/// <see cref="ProcessPlantilla"/> snapshot to <c>dbo.ProcessPlantillas</c>.
/// The Process-end of the one-to-one relationship is configured in
/// <see cref="ProcessConfiguration"/>; this file owns the SourcePlantilla FK.
/// Spec 035 / D4 — the ImpactTemplateIdsCsv payload column was dropped.
/// </summary>
public class ProcessPlantillaConfiguration : IEntityTypeConfiguration<ProcessPlantilla>
{
    public void Configure(EntityTypeBuilder<ProcessPlantilla> builder)
    {
        builder.ToTable("ProcessPlantillas");
        builder.HasKey(pp => pp.Id);
        builder.Property(pp => pp.Id).ValueGeneratedOnAdd();

        builder.Property(pp => pp.ProcessId).IsRequired();
        builder.Property(pp => pp.SourcePlantillaId).IsRequired();
        builder.Property(pp => pp.MinimumQuotationsPerItem).IsRequired();
        builder.Property(pp => pp.RequiredFieldFlags).IsRequired();

        builder.Property(pp => pp.AssignedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        // OQ-1 — UNIQUE on ProcessId; mirrors UX_ProcessPlantillas_ProcessId.
        builder.HasIndex(pp => pp.ProcessId)
            .IsUnique()
            .HasDatabaseName("UX_ProcessPlantillas_ProcessId");

        builder.HasOne(pp => pp.SourcePlantilla)
            .WithMany()
            .HasForeignKey(pp => pp.SourcePlantillaId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
