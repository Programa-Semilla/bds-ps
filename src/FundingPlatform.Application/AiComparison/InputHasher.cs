using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FundingPlatform.Application.Abstractions.AiComparison;

namespace FundingPlatform.Application.AiComparison;

/// <summary>
/// Spec 020 / FR-D2 — deterministic SHA-256 hash over a canonical-JSON
/// projection of <see cref="InputDescriptor"/>. Canonical = sorted keys,
/// declared array order, lower-case hex digest. No null-vs-missing ambiguity.
/// </summary>
public static class InputHasher
{
    public static string Compute(InputDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var canonical = ToCanonicalJson(descriptor);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Internal-but-public canonicalizer; testable + reused by the freshness
    /// analyzer for diagnostic logging.
    /// </summary>
    public static string ToCanonicalJson(InputDescriptor descriptor)
    {
        // Build a SortedDictionary tree so System.Text.Json emits keys in
        // deterministic order. Arrays preserve declared element order.
        var root = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["applicationItemId"] = descriptor.ApplicationItemId,
            ["orderedSupplierIds"] = descriptor.OrderedSupplierIds.ToArray(),
            ["orderedBranchIds"] = descriptor.OrderedBranchIds.ToArray(),
            ["blobReferences"] = descriptor.BlobReferences
                .Select(b => (object)new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["blobId"] = b.BlobId.ToString("D", CultureInfo.InvariantCulture),
                    ["contentHash"] = b.ContentHash,
                })
                .ToArray(),
            ["lineState"] = descriptor.LineState
                .Select(l => (object)new SortedDictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["quotationLineId"] = l.QuotationLineId,
                    ["quantity"] = l.Quantity.ToString(CultureInfo.InvariantCulture),
                    ["unitPrice"] = l.UnitPrice.ToString(CultureInfo.InvariantCulture),
                    ["currencyCode"] = l.CurrencyCode,
                    ["exchangeRateSnapshotId"] = l.ExchangeRateSnapshotId?.ToString("D", CultureInfo.InvariantCulture),
                })
                .ToArray(),
            ["promptVersion"] = descriptor.PromptVersion,
            ["schemaVersion"] = descriptor.SchemaVersion,
        };

        return JsonSerializer.Serialize(root, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
