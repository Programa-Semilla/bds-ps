using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>Spec 020 / FR-D1 — maps <see cref="ComparisonArtifact"/> to
/// <c>dbo.ComparisonArtifacts</c>. Primary key is <c>ApplicationItemId</c>.</summary>
public class ComparisonArtifactConfiguration : IEntityTypeConfiguration<ComparisonArtifact>
{
    public void Configure(EntityTypeBuilder<ComparisonArtifact> builder)
    {
        builder.ToTable("ComparisonArtifacts");
        builder.HasKey(a => a.ApplicationItemId);
        builder.Property(a => a.ApplicationItemId).ValueGeneratedNever();
        builder.Property(a => a.JsonContent).IsRequired();
        builder.Property(a => a.InputHash).HasMaxLength(64).IsRequired();
        builder.Property(a => a.PromptVersion).HasMaxLength(64).IsRequired();
        builder.Property(a => a.SchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(a => a.AiModel).HasMaxLength(128).IsRequired();
        builder.Property(a => a.GeneratedAt).IsRequired();
        builder.Property(a => a.GeneratedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(a => a.TokenCostInput).IsRequired();
        builder.Property(a => a.TokenCostOutput).IsRequired();
        builder.Property(a => a.LatencyMs).IsRequired();

        builder.HasIndex(a => a.InputHash).HasDatabaseName("IX_ComparisonArtifacts_InputHash");
    }
}
