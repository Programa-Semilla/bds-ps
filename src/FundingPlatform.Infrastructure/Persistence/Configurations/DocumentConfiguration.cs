using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents");

        builder.HasKey(d => d.Id);

        builder.Property(d => d.OriginalFileName).IsRequired().HasMaxLength(500);
        builder.Property(d => d.StoragePath).IsRequired().HasMaxLength(1000);
        builder.Property(d => d.BlobKey).IsRequired().HasMaxLength(1024);
        builder.Property(d => d.FileSize).IsRequired();
        builder.Property(d => d.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(d => d.UploadedAt).IsRequired();
    }
}
