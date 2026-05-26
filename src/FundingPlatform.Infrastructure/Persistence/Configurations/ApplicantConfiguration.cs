using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public class ApplicantConfiguration : IEntityTypeConfiguration<Applicant>
{
    public void Configure(EntityTypeBuilder<Applicant> builder)
    {
        builder.ToTable("Applicants");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.UserId).IsRequired().HasMaxLength(450);
        builder.HasIndex(a => a.UserId).IsUnique().HasDatabaseName("UX_Applicants_UserId");

        builder.Property(a => a.LegalId).IsRequired().HasMaxLength(50);
        builder.HasIndex(a => a.LegalId).IsUnique().HasDatabaseName("UX_Applicants_LegalId");

        // Spec 026 — nullable byte-enum stored as TINYINT.
        builder.Property(a => a.IdentificationType).HasConversion<byte?>();

        builder.Property(a => a.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(a => a.LastName).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Email).IsRequired().HasMaxLength(256);
        builder.Property(a => a.Phone).HasMaxLength(20);
        builder.Property(a => a.PerformanceScore).HasColumnType("decimal(5,2)");
        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();

        builder.HasMany(a => a.Applications)
            .WithOne(app => app.Applicant)
            .HasForeignKey(app => app.ApplicantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
