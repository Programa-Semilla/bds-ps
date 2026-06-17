// Spec 036 — see specs/036-funds-usage-evidence/data-model.md.

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public sealed class FundsUsageEvidenceConfiguration : IEntityTypeConfiguration<FundsUsageEvidence>
{
    public void Configure(EntityTypeBuilder<FundsUsageEvidence> builder)
    {
        builder.ToTable("FundsUsageEvidence");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ApplicationId).IsRequired();
        builder.Property(e => e.UploadedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(e => e.OriginalFileName).IsRequired().HasMaxLength(500);
        builder.Property(e => e.BlobKey).IsRequired().HasMaxLength(1024);
        builder.Property(e => e.FileSize).IsRequired();
        builder.Property(e => e.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Note).HasMaxLength(250);
        builder.Property(e => e.UploadedAt).IsRequired();
        builder.Property(e => e.RowVersion).IsRowVersion();

        builder.HasIndex(e => e.ApplicationId)
            .HasDatabaseName("IX_FundsUsageEvidence_ApplicationId");

        // research D2 — the Application aggregate carries no navigation collection;
        // evidence is queried flat by ApplicationId.
        builder.HasOne<AppEntity>()
            .WithMany()
            .HasForeignKey(e => e.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
