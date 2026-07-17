// Spec 047 — see specs/047-evidence-graph-required-docs/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 047 — maps <see cref="Evidence"/> to <c>dbo.Evidence</c>, the evidence-graph node that lives
/// alongside the untouched P1 <see cref="DisbursementEvidence"/> money-gate (D1). <c>Type</c> is a
/// TINYINT (<c>HasConversion&lt;byte&gt;()</c>); <c>Amount</c> is exact <c>decimal(18,2)</c>;
/// <c>Currency</c> a fixed <c>char(3)</c>; <c>FileHash</c> a fixed <c>char(64)</c>. FK to Applications
/// is NO ACTION (soft-delete filter model, matches <c>Disbursement</c>). Owns the append-only
/// <see cref="EvidenceVersion"/> chain (CASCADE).
/// </summary>
public sealed class EvidenceConfiguration : IEntityTypeConfiguration<Evidence>
{
    public void Configure(EntityTypeBuilder<Evidence> builder)
    {
        builder.ToTable("Evidence");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ApplicationId).IsRequired();
        builder.Property(e => e.DisbursementId);
        builder.Property(e => e.Type).HasConversion<byte>().IsRequired();
        builder.Property(e => e.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(e => e.Currency)
            .IsRequired()
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsFixedLength();
        builder.Property(e => e.DocumentReferenceNumber).IsRequired().HasMaxLength(100);
        builder.Property(e => e.DocumentDate).IsRequired();
        builder.Property(e => e.SupplierId);
        builder.Property(e => e.BlobKey).IsRequired().HasMaxLength(1024);
        builder.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(500);
        builder.Property(e => e.FileSize).IsRequired();
        builder.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FileHash)
            .IsRequired()
            .HasColumnType("char(64)")
            .HasMaxLength(64)
            .IsFixedLength();
        builder.Property(e => e.UploadedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(e => e.UploadedAtUtc).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => e.ApplicationId).HasDatabaseName("IX_Evidence_ApplicationId");

        // Scope + optional anchors — all NO ACTION (Restrict); the graph is soft-delete-filtered.
        builder.HasOne<Domain.Entities.Application>()
            .WithMany()
            .HasForeignKey(e => e.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Disbursement>()
            .WithMany()
            .HasForeignKey(e => e.DisbursementId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        // Owned append-only version chain (CASCADE on evidence delete).
        builder.HasMany(e => e.Versions)
            .WithOne()
            .HasForeignKey(v => v.EvidenceId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata
            .FindNavigation(nameof(Evidence.Versions))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
