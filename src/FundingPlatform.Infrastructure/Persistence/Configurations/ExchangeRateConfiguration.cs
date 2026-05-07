using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 015 — EF mapping for the <c>ExchangeRates</c> table. Decimal precisions
/// match data-model.md (Buy/Sell at 18,6). FKs to Currencies use the string
/// form of <see cref="CurrencyCode"/>; the value-object converter is applied so
/// app code reads/writes <c>CurrencyCode</c> while the DB stores <c>char(3)</c>.
/// </summary>
public class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("ExchangeRates");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.SourceCurrency)
            .HasColumnName("SourceCurrencyCode")
            .HasConversion(v => v.Value, v => new CurrencyCode(v))
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsRequired()
            .IsFixedLength();

        builder.Property(r => r.TargetCurrency)
            .HasColumnName("TargetCurrencyCode")
            .HasConversion(v => v.Value, v => new CurrencyCode(v))
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsRequired()
            .IsFixedLength();

        builder.Property(r => r.BuyRate).IsRequired().HasColumnType("decimal(18, 6)");
        builder.Property(r => r.SellRate).IsRequired().HasColumnType("decimal(18, 6)");
        builder.Property(r => r.EffectiveAtUtc).IsRequired().HasColumnType("datetime2(3)");
        builder.Property(r => r.CreatedByUserId).IsRequired().HasMaxLength(450);
        builder.Property(r => r.CreatedAtUtc).IsRequired().HasColumnType("datetime2(3)");
        builder.Property(r => r.IsUsed).IsRequired();

        builder.Property(r => r.RowVersion).IsRowVersion();

        // FR-007: at most one rate per (pair, effectiveAt). Schema-side index lives
        // in dbo.ExchangeRates.sql (UQ_ExchangeRates_PairAt); this declaration lets
        // EF know about it for translated-query usage and keeps the model in sync.
        builder.HasIndex(r => new { r.SourceCurrency, r.TargetCurrency, r.EffectiveAtUtc })
            .IsUnique()
            .HasDatabaseName("UQ_ExchangeRates_PairAt");

        builder.HasIndex(r => new { r.SourceCurrency, r.TargetCurrency, r.EffectiveAtUtc })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_ExchangeRates_PairEffectiveAtDesc");

        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(r => r.SourceCurrency)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ExchangeRates_Currencies_Source");

        builder.HasOne<Currency>()
            .WithMany()
            .HasForeignKey(r => r.TargetCurrency)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ExchangeRates_Currencies_Target");
    }
}
