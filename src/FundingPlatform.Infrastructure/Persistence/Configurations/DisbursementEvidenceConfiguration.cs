// Spec 045 — see specs/045-financial-disbursement-core/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 045 — maps <see cref="DisbursementEvidence"/> to <c>dbo.DisbursementEvidence</c>.
/// <c>Kind</c> is a TINYINT (<c>HasConversion&lt;byte&gt;()</c>); <c>Amount</c> is exact
/// <c>decimal(18,2)</c>; <c>Currency</c> is a fixed-length <c>char(3)</c>. The unique
/// <c>(DisbursementId, Kind)</c> index (schema-side) enforces one bank receipt + one
/// invoice (FR-006).
/// </summary>
public sealed class DisbursementEvidenceConfiguration : IEntityTypeConfiguration<DisbursementEvidence>
{
    public void Configure(EntityTypeBuilder<DisbursementEvidence> builder)
    {
        builder.ToTable("DisbursementEvidence");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.DisbursementId).IsRequired();
        builder.Property(e => e.Kind).HasConversion<byte>().IsRequired();
        builder.Property(e => e.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(e => e.Currency)
            .IsRequired()
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsFixedLength();
        builder.Property(e => e.DocumentReferenceNumber).IsRequired().HasMaxLength(100);
        builder.Property(e => e.DocumentDate).IsRequired();
        builder.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(500);
        builder.Property(e => e.BlobKey).IsRequired().HasMaxLength(1024);
        builder.Property(e => e.FileSize).IsRequired();
        builder.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.UploadedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(e => e.UploadedAtUtc).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => new { e.DisbursementId, e.Kind })
            .IsUnique()
            .HasDatabaseName("UX_DisbursementEvidence_Disbursement_Kind");
        builder.HasIndex(e => e.DisbursementId)
            .HasDatabaseName("IX_DisbursementEvidence_DisbursementId");

        builder.HasOne<Disbursement>()
            .WithMany()
            .HasForeignKey(e => e.DisbursementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
