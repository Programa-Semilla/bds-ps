using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FundingPlatform.Infrastructure.Persistence.Configurations;

/// <summary>
/// Spec 015 — EF mapping for the <c>Currencies</c> catalog table. Schema lives
/// in the dacpac (<c>dbo.Currencies.sql</c>); this configuration only describes
/// the C# ↔ column projection plus the value-object converter for
/// <see cref="CurrencyCode"/>.
/// </summary>
public class CurrencyConfiguration : IEntityTypeConfiguration<Currency>
{
    public void Configure(EntityTypeBuilder<Currency> builder)
    {
        builder.ToTable("Currencies");

        builder.HasKey(c => c.Code);

        builder.Property(c => c.Code)
            .HasConversion(
                v => v.Value,
                v => new CurrencyCode(v))
            .HasColumnType("char(3)")
            .HasMaxLength(3)
            .IsRequired()
            .IsFixedLength();

        builder.Property(c => c.Symbol).IsRequired().HasMaxLength(8);
        builder.Property(c => c.DisplayName).IsRequired().HasMaxLength(64);
        builder.Property(c => c.DecimalPrecision).IsRequired();
        builder.Property(c => c.IsEnabled).IsRequired();
        builder.Property(c => c.IsBaseCurrency).IsRequired();
        builder.Property(c => c.DisplayOrder).IsRequired();

        builder.Property(c => c.RowVersion).IsRowVersion();
    }
}
