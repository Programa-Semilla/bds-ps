// Spec 047 — see specs/047-evidence-graph-required-docs/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 047 — maps <see cref="EvidenceVersion"/> to <c>dbo.EvidenceVersions</c>, the append-only
/// audit chain (D4). Exactly one current row per evidence, enforced by the filtered unique
/// <c>UX_EvidenceVersions_OneCurrent WHERE [IsCurrent] = 1</c> (copies
/// <c>UX_SignedUploads_OnePending_PerAgreement</c>). The parent FK (→ Evidence CASCADE) is
/// configured on <see cref="EvidenceConfiguration"/>.
/// </summary>
public sealed class EvidenceVersionConfiguration : IEntityTypeConfiguration<EvidenceVersion>
{
    public void Configure(EntityTypeBuilder<EvidenceVersion> builder)
    {
        builder.ToTable("EvidenceVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.EvidenceId).IsRequired();
        builder.Property(v => v.VersionNumber).IsRequired();
        builder.Property(v => v.IsCurrent).IsRequired();

        builder.Property(v => v.BlobKey).IsRequired().HasMaxLength(1024);
        builder.Property(v => v.OriginalFileName).IsRequired().HasMaxLength(500);
        builder.Property(v => v.FileSize).IsRequired();
        builder.Property(v => v.ContentType).IsRequired().HasMaxLength(100);

        builder.Property(v => v.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(v => v.Currency)
            .IsRequired()
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsFixedLength();
        builder.Property(v => v.DocumentReferenceNumber).IsRequired().HasMaxLength(100);
        builder.Property(v => v.DocumentDate).IsRequired();
        builder.Property(v => v.FileHash)
            .IsRequired()
            .HasColumnType("char(64)")
            .HasMaxLength(64)
            .IsFixedLength();
        builder.Property(v => v.Reason).HasMaxLength(500);
        builder.Property(v => v.CreatedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(v => v.CreatedAtUtc).IsRequired();

        builder.HasIndex(v => v.EvidenceId).HasDatabaseName("IX_EvidenceVersions_EvidenceId");
        builder.HasIndex(v => v.EvidenceId)
            .IsUnique()
            .HasFilter("[IsCurrent] = 1")
            .HasDatabaseName("UX_EvidenceVersions_OneCurrent");
    }
}
