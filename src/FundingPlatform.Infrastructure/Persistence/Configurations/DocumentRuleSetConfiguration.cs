// Spec 047 — see specs/047-evidence-graph-required-docs/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 047 — maps <see cref="DocumentRuleSet"/> to <c>dbo.DocumentRuleSets</c>, the admin
/// required-document matrix (D5). One set per category (+ one global-default row where
/// <c>CategoryId IS NULL</c>), enforced by <c>UNIQUE (CategoryId)</c> — SQL Server treats the single
/// NULL as unique-eligible. Owns <see cref="DocumentRuleItem"/> rows (CASCADE); the Category FK is
/// NO ACTION.
/// </summary>
public sealed class DocumentRuleSetConfiguration : IEntityTypeConfiguration<DocumentRuleSet>
{
    public void Configure(EntityTypeBuilder<DocumentRuleSet> builder)
    {
        builder.ToTable("DocumentRuleSets");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.CategoryId);
        builder.Property(s => s.RowVersion).IsRowVersion();

        builder.HasIndex(s => s.CategoryId)
            .IsUnique()
            .HasDatabaseName("UX_DocumentRuleSets_CategoryId");

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Items)
            .WithOne()
            .HasForeignKey(i => i.DocumentRuleSetId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Metadata
            .FindNavigation(nameof(DocumentRuleSet.Items))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
