// Spec 021 — see specs/021-feedback-session-may13/data-model.md (AdminAuditEvent
// new event-kind discriminators) and research.md OQ-9.

using FundingPlatform.Application.Abstractions;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;

namespace FundingPlatform.Infrastructure.Audit;

/// <summary>
/// Spec 021 / NFR-005 — terse-API writer for the new spec-021 admin audit
/// event kinds. Mirrors the spec-016
/// <c>FundingPlatform.Infrastructure.Audit.AdminAuditWriter</c> transaction
/// behavior: stages the entity on the shared <see cref="AppDbContext"/>
/// without calling <c>SaveChanges</c>; the caller owns the transaction
/// boundary so a failed parent mutation does not leave a dangling audit row.
///
/// The <c>TargetType</c> / <c>TargetId</c> columns required by the underlying
/// row are derived from <paramref name="eventKind"/> when known (e.g. the
/// <c>process.*</c> events target <c>process</c>); when the row needs a
/// non-derivable target, callers should instead use
/// <see cref="FundingPlatform.Application.Audit.IAdminAuditWriter"/> with a
/// pre-built <see cref="AdminAuditEvent"/>.
/// </summary>
public sealed class AdminAuditEventWriter : IAdminAuditEventWriter
{
    private readonly AppDbContext _db;

    public AdminAuditEventWriter(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task WriteAsync(string eventKind, string actorUserId, string? payloadJson, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUserId);

        // Derive TargetType from the discriminator prefix; for kinds the
        // map does not cover, the caller MUST go through IAdminAuditWriter
        // which accepts an explicit target. We default to a "system" target
        // with id "0" for prefix-unknown kinds so the row is still well-formed
        // (the columns are NOT NULL).
        var (targetType, targetId) = DeriveTarget(eventKind, payloadJson);
        var entity = AdminAuditEvent.Record(actorUserId, eventKind, targetType, targetId, payloadJson);
        _db.AdminAuditEvents.Add(entity);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Maps the spec-021 event-kind discriminator to the
    /// (<c>TargetType</c>, <c>TargetId</c>) tuple used by the audit table. The
    /// real target id is only known to the caller — we use a sentinel "0"
    /// placeholder when none is supplied, matching the existing
    /// <c>AdminAuditEvent.Record</c> guard.
    /// </summary>
    private static (string TargetType, string TargetId) DeriveTarget(string eventKind, string? payloadJson)
    {
        // Spec 038 — provider regulatory mutations (supplier.regulatory_changed/…).
        // Unlike the other prefixes, the real supplier id is set as TargetId (parsed
        // from the payload's `supplierId`) so the trail is queryable per provider via
        // IX_AdminAuditEvents_Target. Falls back to "0" if the payload omits it.
        if (eventKind.StartsWith("supplier.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeSupplier, ExtractIntId(payloadJson, "supplierId"));
        }
        // Spec 045 — disbursement lifecycle mutations (disbursement.recorded/edited/…).
        // Like supplier.*, the real disbursement id is set as TargetId (parsed from the
        // payload's `disbursementId`) so the trail is queryable per disbursement.
        if (eventKind.StartsWith("disbursement.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeDisbursement, ExtractIntId(payloadJson, "disbursementId"));
        }
        if (eventKind.StartsWith("process.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeProcess, "0");
        }
        if (eventKind.StartsWith("plantilla.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypePlantilla, "0");
        }
        if (eventKind.StartsWith("supplier_admin.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeAdminRoute, "0");
        }
        if (eventKind.StartsWith("group.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeGroup, "0");
        }
        // Spec 029 — Fund (Fondo) mutations (fund.create/edit/archive/…). The
        // real fund id is carried in the payload JSON (TargetId stays the "0"
        // sentinel, matching the process.* pattern above).
        if (eventKind.StartsWith("fund.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeFund, "0");
        }
        if (eventKind.StartsWith("user.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeUser, "0");
        }
        // Spec 036 — funds-usage evidence mutations (funds_evidence.uploaded/…).
        // The real evidence/application ids ride in the payload JSON; TargetId
        // stays the "0" sentinel, matching the fund.*/process.* patterns above.
        if (eventKind.StartsWith("funds_evidence.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeFundsEvidence, "0");
        }
        // Spec 037 — applicant company mutations (company.create/rename/archive/…).
        // The real company/applicant ids ride in the payload JSON; TargetId stays
        // the "0" sentinel, matching the fund.*/funds_evidence.* patterns above.
        if (eventKind.StartsWith("company.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeCompany, "0");
        }
        // Spec 040 — checklist-template mutations (checklist.create/edit/activate/…).
        // The real template id rides in the payload JSON; TargetId stays "0".
        if (eventKind.StartsWith("checklist.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeChecklist, "0");
        }
        // Spec 046 — tranche mutations (tranche.created/renamed/deleted/item_assigned/…).
        // Like disbursement.*, the real tranche id is parsed from the payload's `trancheId`
        // so the trail is queryable per tranche. tranche.item_unassigned carries no tranche
        // id and falls back to the "0" sentinel.
        if (eventKind.StartsWith("tranche.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeTranche, ExtractIntId(payloadJson, "trancheId"));
        }
        // Spec 046 — budget-line commit mutations (line.committed/uncommitted). Target the
        // line Item; the real item id is parsed from the payload's `itemId`.
        if (eventKind.StartsWith("line.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeItem, ExtractIntId(payloadJson, "itemId"));
        }
        // Spec 047 — evidence-graph mutations (evidence.attached/replaced/allocated/deleted).
        // Like disbursement.*, the real evidence id is parsed from the payload's `evidenceId`.
        if (eventKind.StartsWith("evidence.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeEvidence, ExtractIntId(payloadJson, "evidenceId"));
        }
        // Spec 047 — budget-line closure mutations (closure.line_closed/line_reopened). Target the
        // line Item; the real item id is parsed from the payload's `itemId`.
        if (eventKind.StartsWith("closure.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeClosure, ExtractIntId(payloadJson, "itemId"));
        }
        // Spec 047 — required-document rule mutations (docrule.upserted). The category id is parsed
        // from the payload's `categoryId` ("0" for the global-default set).
        if (eventKind.StartsWith("docrule.", StringComparison.Ordinal))
        {
            return (AdminAuditEvent.TargetTypeDocRule, ExtractIntId(payloadJson, "categoryId"));
        }
        return ("system", "0");
    }

    /// <summary>Spec 038/045 — pull an integer id property out of the payload JSON for the
    /// per-target id; "0" sentinel when absent/unparseable.</summary>
    private static string ExtractIntId(string? payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
            return "0";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == System.Text.Json.JsonValueKind.Number && prop.TryGetInt32(out var id))
                    return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (prop.ValueKind == System.Text.Json.JsonValueKind.String && prop.GetString() is { Length: > 0 } s)
                    return s;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Malformed payload — fall through to the sentinel.
        }
        return "0";
    }
}
