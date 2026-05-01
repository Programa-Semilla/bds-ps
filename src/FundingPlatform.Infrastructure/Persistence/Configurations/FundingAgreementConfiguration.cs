using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public class FundingAgreementConfiguration : IEntityTypeConfiguration<FundingAgreement>
{
    public void Configure(EntityTypeBuilder<FundingAgreement> builder)
    {
        builder.ToTable("FundingAgreements");

        builder.HasKey(f => f.Id);

        builder.Property(f => f.ApplicationId).IsRequired();
        builder.HasIndex(f => f.ApplicationId)
            .IsUnique()
            .HasDatabaseName("UQ_FundingAgreements_ApplicationId");

        builder.Property(f => f.FileName).IsRequired().HasMaxLength(260);
        builder.Property(f => f.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(f => f.Size).IsRequired();
        builder.Property(f => f.StoragePath).IsRequired().HasMaxLength(500);
        // Spec 014 — canonical object-storage key, always populated on insert.
        builder.Property(f => f.BlobKey).IsRequired().HasMaxLength(1024);
        builder.Property(f => f.GeneratedAtUtc).IsRequired();
        builder.Property(f => f.GeneratedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(f => f.GeneratedVersion).IsRequired().HasDefaultValue(1);

        builder.Property(f => f.RowVersion).IsRowVersion();

        builder
            .HasOne<AppEntity>()
            .WithOne(a => a.FundingAgreement!)
            .HasForeignKey<FundingAgreement>(f => f.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(f => f.SignedUploads)
            .WithOne()
            .HasForeignKey(u => u.FundingAgreementId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(f => f.SignedUploads)
            .HasField("_signedUploads")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
