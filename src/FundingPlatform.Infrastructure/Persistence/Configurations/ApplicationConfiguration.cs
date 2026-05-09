using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

public class ApplicationConfiguration : IEntityTypeConfiguration<AppEntity>
{
    public void Configure(EntityTypeBuilder<AppEntity> builder)
    {
        builder.ToTable("Applications");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.ApplicantId).IsRequired();
        builder.HasIndex(a => a.ApplicantId).HasDatabaseName("IX_Applications_ApplicantId");

        // Spec 018 / FR-015 / FR-016 — required commercial entity name, ≤200 chars.
        builder.Property(a => a.CompanyName).IsRequired().HasMaxLength(200);

        builder.Property(a => a.State).IsRequired();
        builder.HasIndex(a => a.State).HasDatabaseName("IX_Applications_State");

        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();
        builder.Property(a => a.SubmittedAt);

        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasMany(a => a.Items)
            .WithOne()
            .HasForeignKey(i => i.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.VersionHistory)
            .WithOne(v => v.Application)
            .HasForeignKey(v => v.ApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        var applicantResponsesNav = builder.Metadata.FindNavigation(nameof(AppEntity.ApplicantResponses))!;
        applicantResponsesNav.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(a => a.ApplicantResponses)
            .WithOne()
            .HasForeignKey(r => r.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        var appealsNav = builder.Metadata.FindNavigation(nameof(AppEntity.Appeals))!;
        appealsNav.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(a => a.Appeals)
            .WithOne()
            .HasForeignKey(ap => ap.ApplicationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
