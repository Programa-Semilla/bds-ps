using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>Spec 020 / data-model.md — maps <see cref="ComparisonJob"/> to
/// <c>dbo.ComparisonJobs</c>. Status persists as a string for SQL-side
/// readability + admin queries.</summary>
public class ComparisonJobConfiguration : IEntityTypeConfiguration<ComparisonJob>
{
    public void Configure(EntityTypeBuilder<ComparisonJob> builder)
    {
        builder.ToTable("ComparisonJobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.ApplicationItemId).IsRequired();
        builder.Property(j => j.RequestedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(j => j.ActorRole).HasMaxLength(16).IsRequired();
        builder.Property(j => j.Status).HasMaxLength(16).HasConversion<string>().IsRequired();
        builder.Property(j => j.BypassedRateLimit).IsRequired();
        builder.Property(j => j.BypassedTokenCap).IsRequired();
        builder.Property(j => j.LastStatusChangeAt).IsRequired();
        builder.Property(j => j.ResultingArtifactId);
        builder.Property(j => j.FailureReason).HasMaxLength(128);
        builder.Property(j => j.StartedAt);
        builder.Property(j => j.FinishedAt);

        builder.HasIndex(j => new { j.Status, j.LastStatusChangeAt })
            .HasDatabaseName("IX_ComparisonJobs_Status_LastStatusChangeAt");
        builder.HasIndex(j => new { j.ApplicationItemId, j.Status })
            .HasDatabaseName("IX_ComparisonJobs_ApplicationItemId_Status");
    }
}
