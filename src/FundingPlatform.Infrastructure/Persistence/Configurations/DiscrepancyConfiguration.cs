// Spec 048 — see specs/048-full-reconciliation-engine/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 048 — maps <see cref="Discrepancy"/> to <c>dbo.Discrepancies</c>, the persisted stateful
/// reconciliation discrepancy. All four enum columns (<c>ScopeType</c>/<c>Comparison</c>/<c>Severity</c>/
/// <c>State</c>) are TINYINT (<c>HasConversion&lt;byte&gt;()</c>); the four money columns are exact
/// <c>decimal(18,2)</c>. The stable-identity unique index backstops the one-row-per-identity invariant
/// (FR-003). FK to Applications is NO ACTION (soft-delete filter model, matches <c>Disbursement</c>);
/// the Assignee → AspNetUsers FK is enforced schema-side only (dacpac), like the other user FKs. Owns
/// the append-only <see cref="DiscrepancyEvent"/> chain (CASCADE, configured here on the parent side —
/// the <see cref="Evidence"/>/<see cref="EvidenceVersion"/> precedent).
/// </summary>
public sealed class DiscrepancyConfiguration : IEntityTypeConfiguration<Discrepancy>
{
    public void Configure(EntityTypeBuilder<Discrepancy> builder)
    {
        builder.ToTable("Discrepancies");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.ApplicationId).IsRequired();
        builder.Property(d => d.ScopeType).HasConversion<byte>().IsRequired();
        builder.Property(d => d.ScopeEntityId).IsRequired();
        builder.Property(d => d.Comparison).HasConversion<byte>().IsRequired();
        builder.Property(d => d.Severity).HasConversion<byte>().IsRequired();
        builder.Property(d => d.State).HasConversion<byte>().IsRequired();

        builder.Property(d => d.Expected).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(d => d.Actual).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(d => d.Difference).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(d => d.ToleranceApplied).IsRequired().HasColumnType("decimal(18,2)");

        builder.Property(d => d.SourceDocument).IsRequired().HasMaxLength(200);
        builder.Property(d => d.AssigneeUserId).HasMaxLength(450);
        builder.Property(d => d.FirstDetectedAt).IsRequired();
        builder.Property(d => d.LastEvaluatedAt).IsRequired();
        builder.Property(d => d.ResolvedAt);
        builder.Property(d => d.WaivedReason).HasMaxLength(500);
        builder.Property(d => d.RowVersion).IsRowVersion();

        // FR-003 — exactly one row per stable identity, ever.
        builder.HasIndex(d => new { d.ApplicationId, d.ScopeType, d.ScopeEntityId, d.Comparison })
            .IsUnique()
            .HasDatabaseName("UX_Discrepancies_Identity");

        // Dashboard / money-gate reads by application + lifecycle state.
        builder.HasIndex(d => new { d.ApplicationId, d.State })
            .IncludeProperties(d => d.Severity)
            .HasDatabaseName("IX_Discrepancies_App_State");

        // Filter by responsible user.
        builder.HasIndex(d => d.AssigneeUserId)
            .HasFilter("[AssigneeUserId] IS NOT NULL")
            .HasDatabaseName("IX_Discrepancies_Assignee");

        builder.HasOne<AppEntity>()
            .WithMany()
            .HasForeignKey(d => d.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Owned append-only event chain (CASCADE on discrepancy delete).
        builder.HasMany(d => d.Events)
            .WithOne()
            .HasForeignKey(e => e.DiscrepancyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata
            .FindNavigation(nameof(Discrepancy.Events))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
