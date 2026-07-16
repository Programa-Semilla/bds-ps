using FundingPlatform.Application.Admin.Users.DTOs;

namespace FundingPlatform.Application.Tranches;

/// <summary>
/// Spec 046 / US1 — reviewer-owned tranche (funding-phase) setup on the pre-audit review surface.
/// Every method resolves the application, enforces the execution freeze (<c>State !=
/// AgreementExecuted</c>, else a <c>TrancheFrozen</c> reason — research D4), routes CRUD through
/// the <c>Application</c> aggregate methods, and writes a <c>tranche.*</c> <c>AdminAuditEvent</c>
/// with the two-SaveChanges pattern (mirrors <c>FundService</c>). Duplicate name → <c>TrancheNameInUse</c>
/// (accent/case pre-check via <c>CompanyNameNormalizer</c> + <c>UX_Tranches_ApplicationId_Name</c> backstop).
/// The caller (controller) owns role + group-scope authorization. Implementation
/// <c>Infrastructure/Services/TrancheService.cs</c>.
/// </summary>
public interface ITrancheService
{
    /// <summary>The application's tranches with derived amounts + assigned line ids, ordered by ordinal.</summary>
    Task<IReadOnlyList<TrancheView>> GetForApplicationAsync(int applicationId, CancellationToken ct);

    /// <summary>The application's budget-lines with their budgets + current tranche membership (for the editor).</summary>
    Task<IReadOnlyList<TrancheEditorLine>> GetEditorLinesAsync(int applicationId, CancellationToken ct);

    /// <summary>FR-001 — create a tranche; returns the new id.</summary>
    Task<Result<int>> CreateAsync(int applicationId, string name, string actorUserId, CancellationToken ct);

    /// <summary>FR-001 — rename a tranche.</summary>
    Task<Result> RenameAsync(int applicationId, int trancheId, string name, string actorUserId, CancellationToken ct);

    /// <summary>FR-001 — delete a tranche (member lines re-parent to the synthetic default).</summary>
    Task<Result> DeleteAsync(int applicationId, int trancheId, string actorUserId, CancellationToken ct);

    /// <summary>FR-001 — assign a line to a tranche (or, with <paramref name="trancheId"/> null, unassign it).</summary>
    Task<Result> AssignItemAsync(int applicationId, int itemId, int? trancheId, string actorUserId, CancellationToken ct);
}
