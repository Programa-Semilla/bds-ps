// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Process aggregate).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 021 / FR-001 — maps <see cref="Process"/> to <c>dbo.Processes</c>.
/// Catalog uniqueness on <c>Name</c> across all rows is enforced by the
/// dacpac unique index <c>UX_Processes_Name</c>; application-layer code
/// further scopes "reuse-after-close" semantics.
/// </summary>
public class ProcessConfiguration : IEntityTypeConfiguration<Process>
{
    public void Configure(EntityTypeBuilder<Process> builder)
    {
        builder.ToTable("Processes");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        builder.Property(p => p.Name)
            .HasMaxLength(Process.MaxNameLength)
            .IsRequired();

        builder.Property(p => p.Status).IsRequired().HasConversion<byte>();

        // Spec 044 — SolicitudWindowDays removed (reception windows replace it).
        builder.Property(p => p.RevisionWindowDays);
        builder.Property(p => p.FacturacionWindowDays);

        builder.Property(p => p.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(p => p.ClosedAt);

        builder.Property(p => p.RowVersion).IsRowVersion();

        // Process.Name is unique PER FUND, not globally (mirrors UX_Processes_FundId_Name).
        builder.HasIndex(p => new { p.FundId, p.Name })
            .IsUnique()
            .HasDatabaseName("UX_Processes_FundId_Name");

        // One-to-many: Process → Groups (Group.ProcessId FK).
        builder.HasMany(p => p.Groups)
            .WithOne(g => g.Process)
            .HasForeignKey(g => g.ProcessId)
            .OnDelete(DeleteBehavior.NoAction);

        // One-to-one: Process → ProcessPlantilla snapshot (OQ-1).
        builder.HasOne(p => p.Plantilla)
            .WithOne(pp => pp.Process)
            .HasForeignKey<ProcessPlantilla>(pp => pp.ProcessId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Metadata
            .FindNavigation(nameof(Process.Groups))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Spec 044 — Process → ProcessEvents (the relationship + FK are configured
        // on the dependent side in ProcessEventConfiguration).
        builder.Metadata
            .FindNavigation(nameof(Process.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
