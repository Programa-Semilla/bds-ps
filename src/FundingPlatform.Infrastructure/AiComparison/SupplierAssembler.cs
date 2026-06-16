using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.AiComparison;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.AiComparison.Redaction.Patterns;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.AiComparison;

/// <summary>
/// Spec 020 / FR-B1 — EF-backed supplier assembler. Loads Item + per-supplier
/// Quotation + Supplier + SupplierBranch + Document rows in a single
/// projection so the orchestrator's input building is one DB roundtrip.
/// </summary>
public class SupplierAssembler : ISupplierAssembler
{
    private readonly AppDbContext _context;

    public SupplierAssembler(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ItemAssembly?> AssembleAsync(int applicationItemId, CancellationToken ct)
    {
        var item = await _context.Items
            .AsNoTracking()
            .Include(i => i.Quotations).ThenInclude(q => q.Supplier)
            .Include(i => i.Quotations).ThenInclude(q => q.SupplierBranch)
            .Include(i => i.Quotations).ThenInclude(q => q.Document)
            // Spec 035 / D6 — product + category fields feed the AI comparison context.
            .Include(i => i.CategoryFieldValues).ThenInclude(cfv => cfv.CategoryField)
            .FirstOrDefaultAsync(i => i.Id == applicationItemId, ct);

        if (item is null) return null;

        var application = await _context.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == item.ApplicationId, ct);
        if (application is null) return null;

        var applicationIsClosed = application.State is ApplicationState.Resolved
            or ApplicationState.AgreementExecuted;

        // FINDING-6 — applicant-level PII (legal id / email / phone) is surfaced
        // once per item so the orchestrator can hand it to the PII redactor on
        // every supplier block it builds. Note: the live Applicant entity has
        // only one Email + Phone (no separate "personal" channel). Spec FR-B2
        // is reconciled in the spec.md follow-up.
        var applicant = await _context.Applicants
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == application.ApplicantId, ct);

        var suppliers = item.Quotations
            .OrderBy(q => q.SupplierId)
            .Select(q =>
            {
                var blobRefs = new List<BlobReference>();
                if (q.Document is not null)
                {
                    // Use the immutable blob key as the deterministic
                    // content hash — uploads write a new key per file revision.
                    blobRefs.Add(new BlobReference(
                        BlobId: DeriveBlobGuid(q.Document.Id),
                        ContentHash: q.Document.BlobKey ?? string.Empty));
                }

                return new SupplierAssembly(
                    SupplierId: q.SupplierId,
                    SupplierName: q.Supplier?.Name ?? "Proveedor",
                    SupplierLegalId: q.Supplier?.LegalId ?? string.Empty,
                    SupplierVerificationStatus: (q.Supplier?.VerificationStatus ?? SupplierVerificationStatus.Draft).ToString(),
                    SupplierBranchId: q.SupplierBranchId,
                    BranchName: q.SupplierBranch?.BranchName,
                    BranchContactEmail: q.SupplierBranch?.Email,
                    BranchContactPhone: q.SupplierBranch?.Phone,
                    Price: q.Price,
                    CurrencyCode: string.IsNullOrEmpty(q.Currency) ? "CRC" : q.Currency,
                    ConvertedCrcAmount: q.ConvertedCrcAmount,
                    SnapshotRateValue: q.Snapshot?.RateValue,
                    SnapshotRateId: q.Snapshot?.RateRecordId,
                    ValidUntil: q.ValidUntil,
                    DocumentId: q.DocumentId,
                    DocumentFileName: q.Document?.OriginalFileName ?? string.Empty,
                    DocumentBlobKey: q.Document?.BlobKey ?? string.Empty,
                    DocumentFileSize: q.Document?.FileSize ?? 0,
                    Blobs: blobRefs);
            })
            .ToList();

        // Spec 035 (evolved 2026-06-16, D16) — enrich the AI comparison header with the
        // product name + category field label/value pairs (what is being quoted) + the
        // per-item impact justification (why it's requested), scrubbed for incidental PII.
        // Raw impact PARAMETER values stay EXCLUDED (applicant-evaluation metadata, not
        // relevant to comparing quotes).
        var fichaPrefix = string.IsNullOrEmpty(item.LineCode)
            ? $"Ficha {item.Id}"
            : $"Ficha {item.LineCode}";
        var contextParts = new List<string> { $"Producto: {item.ProductName}" };
        contextParts.AddRange(item.CategoryFieldValues
            .OrderBy(cfv => cfv.CategoryField?.SortOrder ?? 0)
            .Where(cfv => !string.IsNullOrWhiteSpace(cfv.Value))
            .Select(cfv => $"{cfv.CategoryField?.DisplayLabel}: {cfv.Value}"));
        if (!string.IsNullOrWhiteSpace(item.ImpactJustification))
        {
            contextParts.Add($"Justificación de impacto: {item.ImpactJustification}");
        }
        var header = ScrubPii($"{fichaPrefix} — {string.Join("; ", contextParts)}");

        return new ItemAssembly(
            ApplicationItemId: item.Id,
            ApplicationId: item.ApplicationId,
            ItemHeader: header,
            ApplicationIsClosed: applicationIsClosed,
            ApplicantLegalId: applicant?.LegalId,
            ApplicantEmail: applicant?.Email,
            ApplicantPhone: applicant?.Phone,
            Suppliers: suppliers);
    }

    /// <summary>
    /// Spec 035 / D6 — scrub incidental PII (email/phone/cédula) from the free-text
    /// product + category context before it enters the AI payload, reusing the
    /// existing PII pattern catalog.
    /// </summary>
    private static string ScrubPii(string text)
    {
        var t = PiiPatterns.Email.Replace(text, "[correo]");
        t = PiiPatterns.Phone.Replace(t, "[teléfono]");
        t = PiiPatterns.Cedula.Replace(t, "[identificación]");
        return t;
    }

    /// <summary>
    /// Documents have INT ids; downstream hash + citation links want a Guid.
    /// Derive a stable Guid from the int id so the same document always maps
    /// to the same Guid across requests.
    /// </summary>
    public static Guid DeriveBlobGuid(int documentId)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, documentId);
        return new Guid(bytes);
    }
}
