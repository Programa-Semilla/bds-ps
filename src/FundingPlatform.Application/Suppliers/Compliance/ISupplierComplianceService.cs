using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Application.Suppliers.Compliance;

/// <summary>
/// Spec 038 — auditor-facing mutations of a provider's regulatory compliance,
/// PME/PYME flag, and warning. Mirrors the audited-mutation precedent
/// (<c>CompanyAdministrationService</c>): load → domain method → stage one
/// <c>AdminAuditEvent</c> per change → single atomic <c>SaveChangesAsync</c>;
/// optimistic concurrency via the supplier <c>RowVersion</c>.
/// </summary>
public interface ISupplierComplianceService
{
    /// <summary>US1/US2/US3 — the supplier Detail "Edit compliance" POST.</summary>
    Task<SupplierComplianceResult> EditComplianceAsync(EditSupplierComplianceCommand cmd, CancellationToken ct);

    /// <summary>US2 — "reviewed — no change" re-authorization for one regulatory field.</summary>
    Task<SupplierComplianceResult> ConfirmReviewedAsync(
        int supplierId, RegulatoryField field, string actorUserId, byte[] rowVersion, CancellationToken ct);
}

public sealed record EditSupplierComplianceCommand(
    int SupplierId,
    string Name,
    HaciendaStatus? Hacienda,
    CcssStatus? Ccss,
    SicopStatus? Sicop,
    bool IsPmeOrPyme,
    bool HasWarning,
    string? WarningNote,
    string ActorUserId,
    byte[] RowVersion);

public sealed record SupplierComplianceResult(bool Ok, string? ErrorEsCr)
{
    public static SupplierComplianceResult Success() => new(true, null);
    public static SupplierComplianceResult Fail(string errorEsCr) => new(false, errorEsCr);
}
