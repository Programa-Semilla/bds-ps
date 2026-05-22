// Spec 021 — see specs/021-feedback-session-may13/data-model.md (PasswordResetToken)
// and research.md R-3.

using FundingPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 021 / FR-028 / R-3 — maps <see cref="PasswordResetToken"/> to
/// <c>dbo.PasswordResetTokens</c>. The raw token is never persisted, only
/// the SHA-256 hash (<c>VARBINARY(64)</c> — over-allocated relative to the
/// 32-byte digest to allow algorithm upgrades without a schema change).
/// </summary>
public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("PasswordResetTokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        builder.Property(t => t.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(t => t.TokenHash)
            .HasColumnType("VARBINARY(64)")
            .IsRequired();

        builder.Property(t => t.IssuedAt).HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.ConsumedAt);

        // UX_PasswordResetTokens_TokenHash — single-use enforcement piggybacks
        // on the UNIQUE index. Hash collisions are negligible (SHA-256) so a
        // racing duplicate-Issue surfaces as a DbUpdateException, which the
        // caller maps to "fresh attempt only" UX.
        builder.HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_PasswordResetTokens_TokenHash");

        builder.HasIndex(t => new { t.UserId, t.IssuedAt })
            .HasDatabaseName("IX_PasswordResetTokens_UserId_IssuedAt");
    }
}
