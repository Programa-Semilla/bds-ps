// Spec 045 — see specs/045-financial-disbursement-core/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 045 — maps <see cref="DisbursementLedgerEntry"/> to <c>dbo.DisbursementLedgerEntries</c>.
/// <c>EntryType</c> is a TINYINT (<c>HasConversion&lt;byte&gt;()</c>); <c>Amount</c> is exact
/// <c>decimal(18,2)</c>. The filtered-unique indexes (one Allocation per application, one
/// Disbursement entry per validated disbursement) live schema-side (FR-018 idempotency).
/// Append-only — never updated or deleted.
/// </summary>
public sealed class DisbursementLedgerEntryConfiguration : IEntityTypeConfiguration<DisbursementLedgerEntry>
{
    public void Configure(EntityTypeBuilder<DisbursementLedgerEntry> builder)
    {
        builder.ToTable("DisbursementLedgerEntries");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.ApplicationId).IsRequired();
        builder.Property(l => l.EntryType).HasConversion<byte>().IsRequired();
        builder.Property(l => l.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(l => l.DisbursementId);
        builder.Property(l => l.PostedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(l => l.PostedAtUtc).IsRequired();
        builder.Property(l => l.RowVersion).IsRowVersion();

        builder.HasIndex(l => new { l.ApplicationId, l.EntryType })
            .HasDatabaseName("IX_DisbursementLedgerEntries_ApplicationId_EntryType");

        builder.HasOne<AppEntity>()
            .WithMany()
            .HasForeignKey(l => l.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Disbursement>()
            .WithMany()
            .HasForeignKey(l => l.DisbursementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
