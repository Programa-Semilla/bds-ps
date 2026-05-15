using System.Globalization;
using System.Text.Json;

namespace FundingPlatform.Application.AiComparison;

/// <summary>
/// Spec 020 / FR-C3 — pure server-side normalize stage. No AI call. Aligns
/// units (kg/lb, m/cm), normalizes dates to es-CR `MMM dd, yyyy`, converts
/// non-CRC amounts to CRC using the supplied per-supplier snapshot rate, and
/// passes structured-vs-file discrepancies through as both values + a flag
/// (per A-6).
/// </summary>
public static class ComparisonNormalizer
{
    private static readonly CultureInfo EsCr = CultureInfo.GetCultureInfo("es-CR");

    /// <summary>Convert a non-CRC amount to CRC using the supplied snapshot rate.</summary>
    public static decimal ToCrc(decimal amount, string currencyCode, decimal snapshotRate)
    {
        if (string.Equals(currencyCode, "CRC", StringComparison.OrdinalIgnoreCase))
            return amount;
        if (snapshotRate <= 0)
            throw new ArgumentException("Snapshot rate must be positive for non-CRC conversion.", nameof(snapshotRate));
        return Math.Round(amount * snapshotRate, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Convert a length value into metres regardless of source unit.</summary>
    public static decimal ToMetres(decimal value, string unit) => unit.ToLowerInvariant() switch
    {
        "m" => value,
        "cm" => value / 100m,
        "mm" => value / 1000m,
        "km" => value * 1000m,
        _ => throw new ArgumentException($"Unsupported length unit: {unit}", nameof(unit)),
    };

    /// <summary>Convert a mass value into kilograms regardless of source unit.</summary>
    public static decimal ToKilograms(decimal value, string unit) => unit.ToLowerInvariant() switch
    {
        "kg" => value,
        "g" => value / 1000m,
        "lb" => value * 0.45359237m,
        "oz" => value * 0.0283495231m,
        _ => throw new ArgumentException($"Unsupported mass unit: {unit}", nameof(unit)),
    };

    /// <summary>Format a date in es-CR `MMM dd, yyyy`.</summary>
    public static string FormatDateEsCr(DateOnly date) =>
        date.ToDateTime(TimeOnly.MinValue).ToString("MMM dd, yyyy", EsCr);

    /// <summary>
    /// Wrap an extract result into the normalized-supplier JSON the comparator
    /// receives: include conversion notes, applied rate, and discrepancy flags.
    /// </summary>
    public static string BuildNormalizedSuppliersJson(IReadOnlyList<NormalizedSupplier> suppliers)
    {
        var canonical = suppliers
            .Select(s => new
            {
                supplierIdx = s.SupplierIdx,
                supplierName = s.SupplierName,
                branchName = s.BranchName,
                verificationStatus = s.VerificationStatus,
                originalCurrency = s.OriginalCurrency,
                appliedRate = s.AppliedRate,
                totalCrc = s.TotalCrc,
                originalTotal = s.OriginalTotal,
                extracted = s.ExtractedFields,
                discrepancies = s.Discrepancies,
            })
            .ToArray();
        return JsonSerializer.Serialize(canonical);
    }
}

/// <summary>Wire-shape for a single supplier after the normalize pass.</summary>
public sealed record NormalizedSupplier(
    int SupplierIdx,
    string SupplierName,
    string? BranchName,
    string VerificationStatus,
    string OriginalCurrency,
    decimal? AppliedRate,
    decimal TotalCrc,
    decimal OriginalTotal,
    JsonElement ExtractedFields,
    IReadOnlyList<NormalizedDiscrepancy> Discrepancies);

/// <summary>
/// Per A-6 — when the structured DB row disagrees with the extracted file
/// value, both are surfaced and the comparator narrates the discrepancy.
/// </summary>
public sealed record NormalizedDiscrepancy(
    string FieldName,
    string? DbValue,
    string? FileValue);
