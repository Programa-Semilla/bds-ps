// Spec 045 — see specs/045-financial-disbursement-core/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 045 — maps <see cref="Disbursement"/> to <c>dbo.Disbursements</c>. The
/// <c>State</c> TINYINT MUST use <c>HasConversion&lt;byte&gt;()</c> — prior specs
/// (035/040) hit <c>Byte→Int32</c> materialization failures that EF-InMemory hid and
/// only real SQL caught (verified here by <c>DisbursementEnumMaterializationTests</c>).
/// Money is exact <c>decimal(18,2)</c>. No navigation collection on <c>Application</c>
/// (research R2 — queried flat by ApplicationId).
/// </summary>
public sealed class DisbursementConfiguration : IEntityTypeConfiguration<Disbursement>
{
    public void Configure(EntityTypeBuilder<Disbursement> builder)
    {
        builder.ToTable("Disbursements");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.ApplicationId).IsRequired();
        builder.Property(d => d.PaymentDate).IsRequired();
        builder.Property(d => d.Amount).IsRequired().HasColumnType("decimal(18,2)");
        builder.Property(d => d.BankTransactionReference).IsRequired().HasMaxLength(100);
        builder.Property(d => d.BankAccountReference).HasMaxLength(100);
        builder.Property(d => d.State).HasConversion<byte>().IsRequired();
        builder.Property(d => d.CreatedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(d => d.CreatedAtUtc).IsRequired();
        builder.Property(d => d.ValidatedByUserId).HasMaxLength(450);
        builder.Property(d => d.CancelledByUserId).HasMaxLength(450);
        builder.Property(d => d.RowVersion).IsRowVersion();

        builder.HasIndex(d => d.ApplicationId)
            .HasDatabaseName("IX_Disbursements_ApplicationId");
        builder.HasIndex(d => new { d.ApplicationId, d.State })
            .HasDatabaseName("IX_Disbursements_ApplicationId_State");

        builder.HasOne<AppEntity>()
            .WithMany()
            .HasForeignKey(d => d.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
