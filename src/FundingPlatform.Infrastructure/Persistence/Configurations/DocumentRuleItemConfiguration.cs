// Spec 047 — see specs/047-evidence-graph-required-docs/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 047 — maps <see cref="DocumentRuleItem"/> to <c>dbo.DocumentRuleItems</c>. <c>EvidenceType</c>
/// is a TINYINT (<c>HasConversion&lt;byte&gt;()</c> — the byte→int32 gotcha). One row per
/// (set, type), enforced by <c>UNIQUE (DocumentRuleSetId, EvidenceType)</c>. The parent FK
/// (→ DocumentRuleSets CASCADE) is configured on <see cref="DocumentRuleSetConfiguration"/>.
/// </summary>
public sealed class DocumentRuleItemConfiguration : IEntityTypeConfiguration<DocumentRuleItem>
{
    public void Configure(EntityTypeBuilder<DocumentRuleItem> builder)
    {
        builder.ToTable("DocumentRuleItems");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.DocumentRuleSetId).IsRequired();
        builder.Property(i => i.EvidenceType).HasConversion<byte>().IsRequired();
        builder.Property(i => i.IsRequired).IsRequired();

        builder.HasIndex(i => new { i.DocumentRuleSetId, i.EvidenceType })
            .IsUnique()
            .HasDatabaseName("UX_DocumentRuleItems_Set_Type");
    }
}
