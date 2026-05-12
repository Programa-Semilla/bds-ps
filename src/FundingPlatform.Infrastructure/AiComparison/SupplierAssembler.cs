using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.AiComparison;
using FundingPlatform.Domain.Enums;
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
            .FirstOrDefaultAsync(i => i.Id == applicationItemId, ct);

        if (item is null) return null;

        var application = await _context.Applications
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == item.ApplicationId, ct);
        if (application is null) return null;

        var applicationIsClosed = application.State is ApplicationState.Resolved
            or ApplicationState.AgreementExecuted;

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

        var header = string.IsNullOrEmpty(item.LineCode)
            ? $"Ficha {item.Id}"
            : $"Ficha {item.LineCode}";

        return new ItemAssembly(
            ApplicationItemId: item.Id,
            ApplicationId: item.ApplicationId,
            ItemHeader: header,
            ApplicationIsClosed: applicationIsClosed,
            Suppliers: suppliers);
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
