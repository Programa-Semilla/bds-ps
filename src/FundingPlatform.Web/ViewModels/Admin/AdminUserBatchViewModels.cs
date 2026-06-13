using Microsoft.AspNetCore.Http;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>
/// Spec 034 — the upload page. <see cref="ErrorMessage"/> carries the single
/// file-level (FR-003) rejection message shown when the whole file is refused.
/// </summary>
public sealed class AdminUserBatchUploadViewModel
{
    public string? ErrorMessage { get; set; }

    /// <summary>The posted CSV file (bound by name on POST).</summary>
    public IFormFile? Csv { get; set; }
}

/// <summary>Spec 034 — one report line: row number, key field, optional reason.</summary>
public sealed record AdminUserBatchResultRow(int RowNumber, string KeyField, string? Reason);

/// <summary>Spec 034 — the succeeded/errored report rendered after processing.</summary>
public sealed class AdminUserBatchResultViewModel
{
    public IReadOnlyList<AdminUserBatchResultRow> Succeeded { get; init; } = [];
    public IReadOnlyList<AdminUserBatchResultRow> Errored { get; init; } = [];
    public int TotalRows => Succeeded.Count + Errored.Count;
}
