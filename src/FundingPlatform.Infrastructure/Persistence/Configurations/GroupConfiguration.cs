using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>Spec 016 — maps <see cref="Group"/> to <c>dbo.Groups</c>.</summary>
public class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("Groups");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedOnAdd();
        builder.Property(g => g.Name)
            .HasMaxLength(Group.MaxNameLength)
            .IsRequired()
            // Match the `Latin1_General_CI_AI` collation declared in dbo.Groups.sql so
            // EF queries that compare names use the same case/accent-insensitive
            // semantics as the unique index.
            .UseCollation("Latin1_General_CI_AI");
        builder.Property(g => g.CreatedAt)
            .HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(g => g.UpdatedAt)
            .HasDefaultValueSql("SYSUTCDATETIME()");
        // Group.Name is unique PER PROCESS, not globally (mirrors UX_Groups_ProcessId_Name).
        builder.HasIndex(g => new { g.ProcessId, g.Name }).IsUnique().HasDatabaseName("UX_Groups_ProcessId_Name");

        // Spec 021 / FR-001 — every Group lives under a Process. The Process-end
        // of the relationship is configured in ProcessConfiguration; the FK
        // declaration here is implicit through that one-to-many.
        builder.Property(g => g.ProcessId).IsRequired();
        builder.HasIndex(g => g.ProcessId).HasDatabaseName("IX_Groups_ProcessId");

        builder.Metadata
            .FindNavigation(nameof(Group.Memberships))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
