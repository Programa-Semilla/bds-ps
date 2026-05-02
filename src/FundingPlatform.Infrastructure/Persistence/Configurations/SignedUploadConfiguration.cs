using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public class SignedUploadConfiguration : IEntityTypeConfiguration<SignedUpload>
{
    public void Configure(EntityTypeBuilder<SignedUpload> builder)
    {
        builder.ToTable("SignedUploads");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.FundingAgreementId).IsRequired();
        builder.Property(u => u.UploaderUserId).IsRequired().HasMaxLength(450);
        builder.Property(u => u.GeneratedVersionAtUpload).IsRequired();
        builder.Property(u => u.FileName).IsRequired().HasMaxLength(260);
        builder.Property(u => u.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Size).IsRequired();
        // Spec 014 — canonical object-storage key, always populated on insert.
        builder.Property(u => u.BlobKey).IsRequired().HasMaxLength(1024);
        builder.Property(u => u.UploadedAtUtc).IsRequired();
        builder.Property(u => u.Status).IsRequired().HasConversion<int>();

        builder.Property(u => u.RowVersion).IsRowVersion();

        builder.HasIndex(u => new { u.FundingAgreementId, u.Status })
            .HasDatabaseName("IX_SignedUploads_FundingAgreementId_Status");

        builder.HasIndex(u => u.UploaderUserId)
            .HasDatabaseName("IX_SignedUploads_UploaderUserId");

        builder.HasIndex(u => u.FundingAgreementId)
            .HasDatabaseName("UX_SignedUploads_OnePending_PerAgreement")
            .IsUnique()
            .HasFilter("[Status] = 0");

        builder.HasOne(u => u.ReviewDecision)
            .WithOne()
            .HasForeignKey<SigningReviewDecision>(d => d.SignedUploadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(u => u.ReviewDecision)
            .HasField("_reviewDecision")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
