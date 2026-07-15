// Spec 044 — see specs/044-process-reception-windows/data-model.md (EF configuration).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 044 — maps <see cref="ProcessEvent"/> to <c>dbo.ProcessEvents</c>. Mirrors
/// <c>FundConfiguration</c>. The <c>EventType</c> TINYINT MUST use
/// <c>HasConversion&lt;byte&gt;()</c> — prior specs (035/040) hit
/// <c>Byte→Int32</c> materialization failures that EF-InMemory hid and only real
/// SQL (E2E) caught.
/// </summary>
public class ProcessEventConfiguration : IEntityTypeConfiguration<ProcessEvent>
{
    public void Configure(EntityTypeBuilder<ProcessEvent> builder)
    {
        builder.ToTable("ProcessEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();

        builder.Property(e => e.EventType)
            .HasConversion<byte>()
            .IsRequired();

        builder.Property(e => e.Name)
            .HasMaxLength(ProcessEvent.MaxNameLength)
            .IsRequired();

        builder.Property(e => e.Description).HasMaxLength(ProcessEvent.MaxTextLength);
        builder.Property(e => e.ApplicantFacingMessage).HasMaxLength(ProcessEvent.MaxTextLength);

        builder.Property(e => e.StartUtc).IsRequired();
        builder.Property(e => e.EndUtc).IsRequired();
        builder.Property(e => e.ControlsSubmissionAvailability).IsRequired();
        builder.Property(e => e.IsActive).IsRequired();
        builder.Property(e => e.DisplayOrder).IsRequired();

        builder.Property(e => e.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.CreatedByUserId).HasMaxLength(450);
        builder.Property(e => e.UpdatedByUserId).HasMaxLength(450);

        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasOne(e => e.Process)
            .WithMany(p => p.Events)
            .HasForeignKey(e => e.ProcessId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(e => e.ProcessId);
    }
}
