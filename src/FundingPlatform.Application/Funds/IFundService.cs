// Spec 029 — see specs/029-fund-entity/contracts/ui-and-routes.md (Admin Fund management)
// and research D7.

using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Funds;

/// <summary>
/// Spec 029 / US1 — admin lifecycle for the <see cref="FundingPlatform.Domain.Entities.Fund"/>
/// aggregate. Every mutation writes an <c>AdminAuditEvent</c> (fund.*) in the
/// same UnitOfWork. Mirrors the spec-021 <c>IProcessService</c> shape. Regulation
/// uploads stream through spec-014 <c>IObjectStorage</c> under
/// <c>FileCategory.FundRegulation</c>; the caller has already validated the PDF
/// magic bytes + size at the controller boundary.
/// </summary>
public interface IFundService
{
    /// <summary>Catalog rows, optionally filtered by status.</summary>
    Task<IReadOnlyList<FundListRow>> ListAsync(FundStatus? statusFilter, CancellationToken ct);

    /// <summary>Detail (incl. the Processes belonging to the Fund), or null if missing.</summary>
    Task<FundDetail?> GetDetailAsync(int id, CancellationToken ct);

    /// <summary>FR-003 — creates a Fund (Active); optional regulation upload. Returns the id.</summary>
    Task<int> CreateAsync(CreateFundCommand command, string actorUserId, CancellationToken ct);

    /// <summary>FR-004 — edits name/description. Rejected while Archived (domain guard).</summary>
    Task EditAsync(EditFundCommand command, string actorUserId, CancellationToken ct);

    /// <summary>FR-006 — Active → Archived (freeze takes effect). Idempotent.</summary>
    Task ArchiveAsync(int fundId, string actorUserId, CancellationToken ct);

    /// <summary>FR-006 — Archived → Active. Idempotent.</summary>
    Task ReactivateAsync(int fundId, string actorUserId, CancellationToken ct);

    /// <summary>FR-005 — uploads/replaces the regulation PDF (deletes any superseded blob).</summary>
    Task SetRegulationAsync(SetFundRegulationCommand command, string actorUserId, CancellationToken ct);

    /// <summary>FR-005 — removes the regulation PDF (deletes the blob).</summary>
    Task RemoveRegulationAsync(int fundId, string actorUserId, CancellationToken ct);
}

/// <summary>A PDF regulation payload streamed from the controller boundary.</summary>
public sealed record FundRegulationUpload(Stream Content, string FileName, string ContentType, long SizeBytes);

public sealed record CreateFundCommand(string Name, string Description, FundRegulationUpload? Regulation);
public sealed record EditFundCommand(int FundId, string Name, string Description);
public sealed record SetFundRegulationCommand(int FundId, FundRegulationUpload Regulation);

/// <summary>Index row: name, status, #Processes, whether a regulation is attached.</summary>
public sealed record FundListRow(int Id, string Name, FundStatus Status, int ProcessCount, bool HasRegulation);

/// <summary>A Process belonging to a Fund (Details list).</summary>
public sealed record FundProcessRow(int Id, string Name, ProcessStatus Status);

/// <summary>Detail projection for <c>/Admin/Funds/{id}</c>.</summary>
public sealed record FundDetail(
    int Id,
    string Name,
    string Description,
    FundStatus Status,
    bool HasRegulation,
    string? RegulationFileName,
    IReadOnlyList<FundProcessRow> Processes);
