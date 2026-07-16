// Spec 046 — see specs/046-tranches-budget-lines/data-model.md (EF configuration notes).

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 046 — maps <see cref="Tranche"/> to <c>dbo.Tranches</c>. The Application→Tranche
/// relationship (backing field <c>_tranches</c>, OnDelete Restrict) is owned by
/// <see cref="ApplicationConfiguration"/>; this class handles the table, columns, RowVersion,
/// and indexes. Amount is not mapped — it is derived at projection time (research D4).
/// </summary>
public sealed class TrancheConfiguration : IEntityTypeConfiguration<Tranche>
{
    public void Configure(EntityTypeBuilder<Tranche> builder)
    {
        builder.ToTable("Tranches");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.ApplicationId).IsRequired();
        builder.Property(t => t.Name).IsRequired().HasMaxLength(Tranche.NameMaxLength);
        builder.Property(t => t.Ordinal).IsRequired();
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.UpdatedAtUtc).IsRequired();
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasIndex(t => t.ApplicationId).HasDatabaseName("IX_Tranches_ApplicationId");
        builder.HasIndex(t => new { t.ApplicationId, t.Name })
            .IsUnique()
            .HasDatabaseName("UX_Tranches_ApplicationId_Name");
    }
}
