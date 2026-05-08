using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>Spec 016 / NFR-005 — maps <see cref="AdminAuditEvent"/> to
/// <c>dbo.AdminAuditEvents</c>.</summary>
public class AdminAuditEventConfiguration : IEntityTypeConfiguration<AdminAuditEvent>
{
    public void Configure(EntityTypeBuilder<AdminAuditEvent> builder)
    {
        builder.ToTable("AdminAuditEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.OccurredAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(64).IsRequired();
        builder.Property(e => e.TargetType).HasMaxLength(64).IsRequired();
        builder.Property(e => e.TargetId).HasMaxLength(64).IsRequired();
        builder.Property(e => e.PayloadJson);

        builder.HasIndex(e => e.OccurredAt).HasDatabaseName("IX_AdminAuditEvents_OccurredAt");
        builder.HasIndex(e => new { e.TargetType, e.TargetId, e.OccurredAt })
            .HasDatabaseName("IX_AdminAuditEvents_Target");

        // No navigation to ApplicationUser — audit rows survive user deletion;
        // the FK is enforced by the dacpac with ON DELETE NO ACTION.
    }
}
