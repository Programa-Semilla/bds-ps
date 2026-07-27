// Spec 048 — see specs/048-full-reconciliation-engine/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 048 — maps <see cref="DiscrepancyEvent"/> to <c>dbo.DiscrepancyEvents</c>, the append-only
/// lifecycle-history chain. <c>FromState</c>/<c>ToState</c> are TINYINT (<c>HasConversion&lt;byte&gt;()</c>).
/// The parent FK (→ Discrepancies CASCADE, single-ownership child) is configured on
/// <see cref="DiscrepancyConfiguration"/>; the Actor → AspNetUsers FK is enforced schema-side only
/// (dacpac), like the other user FKs. No <c>RowVersion</c> — the rows are immutable.
/// </summary>
public sealed class DiscrepancyEventConfiguration : IEntityTypeConfiguration<DiscrepancyEvent>
{
    public void Configure(EntityTypeBuilder<DiscrepancyEvent> builder)
    {
        builder.ToTable("DiscrepancyEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.DiscrepancyId).IsRequired();
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.ActorUserId).IsRequired().HasMaxLength(450);
        builder.Property(e => e.FromState).HasConversion<byte>().IsRequired();
        builder.Property(e => e.ToState).HasConversion<byte>().IsRequired();
        builder.Property(e => e.Kind).IsRequired().HasMaxLength(30);
        builder.Property(e => e.Reason).HasMaxLength(500);
        builder.Property(e => e.Note).HasMaxLength(500);

        builder.HasIndex(e => new { e.DiscrepancyId, e.OccurredAt })
            .HasDatabaseName("IX_DiscrepancyEvents_Discrepancy");
    }
}
