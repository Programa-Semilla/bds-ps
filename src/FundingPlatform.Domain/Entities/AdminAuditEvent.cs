namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 016 / NFR-005 — append-only record of admin mutations on the group
/// catalog and on a user's group memberships. One row per mutation, written by
/// <c>IAdminAuditWriter</c>. No purge or retention policy in this spec.
/// </summary>
public class AdminAuditEvent
{
    /// <summary>Group catalog action keys.</summary>
    public const string ActionGroupCreate = "group.create";
    public const string ActionGroupRename = "group.rename";
    public const string ActionGroupDelete = "group.delete";
    /// <summary>Spec 021 / FR-001 — admin reparented a Group to a different Process.</summary>
    public const string ActionGroupMoveProcess = "group.move_process";
    /// <summary>User membership-update action key.</summary>
    public const string ActionUserMembershipsUpdate = "user.memberships.update";

    public const string TargetTypeGroup = "group";
    public const string TargetTypeUser = "user";

    // ---------- Spec 021 event-kind discriminators (OQ-9). ----------
    /// <summary>Spec 021 — admin created a Process row.</summary>
    public const string ProcessCreated = "process.created";
    /// <summary>Spec 021 — admin closed a Process (Status → Closed).</summary>
    public const string ProcessClosed = "process.closed";
    /// <summary>Spec 021 — admin set or cleared a per-Process stage-window override.</summary>
    public const string ProcessStageWindowOverridden = "process.stage_window.overridden";
    /// <summary>Spec 021 — admin attached a Plantilla to a Process (snapshot created).</summary>
    public const string PlantillaAssignedToProcess = "plantilla.assigned_to_process";
    /// <summary>Spec 021 — admin force-detached a Plantilla from a Process with a reason.</summary>
    public const string PlantillaForceDetached = "plantilla.force_detached";
    /// <summary>Spec 021 / FR-007 — SupplierAdmin attempted to reach a restricted admin route.</summary>
    public const string SupplierAdminDeniedAccess = "supplier_admin.denied_access";

    /// <summary>Spec 021 — target-type discriminators for the new event kinds.</summary>
    public const string TargetTypeProcess = "process";
    public const string TargetTypePlantilla = "plantilla";
    public const string TargetTypeAdminRoute = "admin_route";

    // ---------- Spec 029 — Fund (Fondo) catalog mutations. ----------
    /// <summary>Spec 029 — admin created a Fund.</summary>
    public const string ActionFundCreate = "fund.create";
    /// <summary>Spec 029 — admin edited a Fund's name/description.</summary>
    public const string ActionFundEdit = "fund.edit";
    /// <summary>Spec 029 — admin archived a Fund (freeze takes effect).</summary>
    public const string ActionFundArchive = "fund.archive";
    /// <summary>Spec 029 — admin reactivated an archived Fund.</summary>
    public const string ActionFundReactivate = "fund.reactivate";
    /// <summary>Spec 029 — admin uploaded/replaced a Fund's regulation PDF.</summary>
    public const string ActionFundRegulationSet = "fund.regulation.set";
    /// <summary>Spec 029 — admin removed a Fund's regulation PDF.</summary>
    public const string ActionFundRegulationRemove = "fund.regulation.remove";

    /// <summary>Spec 029 — target-type discriminator for Fund mutations.</summary>
    public const string TargetTypeFund = "fund";

    /// <summary>Spec 029 / FR-009 — admin reassigned a Process to a different Fund.</summary>
    public const string ActionProcessFundReassigned = "process.fund_reassigned";

    /// <summary>Spec 030 / FR-003 — admin renamed a Process. Payload carries the
    /// old and new name. The <c>process.</c> prefix routes to the Process target
    /// in <c>AdminAuditEventWriter</c>.</summary>
    public const string ProcessRenamed = "process.renamed";

    // ---------- Spec 036 — funds-usage evidence mutations. ----------
    /// <summary>Spec 036 / FR-003 — reviewer/admin uploaded an evidence file.</summary>
    public const string FundsEvidenceUploaded = "funds_evidence.uploaded";
    /// <summary>Spec 036 / FR-006 — reviewer/admin set/cleared/changed an evidence note.</summary>
    public const string FundsEvidenceNoteEdited = "funds_evidence.note_edited";
    /// <summary>Spec 036 / FR-007 — reviewer/admin deleted an evidence file.</summary>
    public const string FundsEvidenceDeleted = "funds_evidence.deleted";

    /// <summary>Spec 036 — target-type discriminator for funds-usage evidence mutations.
    /// The <c>funds_evidence.</c> prefix routes here in <c>AdminAuditEventWriter</c>.</summary>
    public const string TargetTypeFundsEvidence = "funds_evidence";

    // ---------- Spec 037 — applicant company (Empresa) mutations. ----------
    /// <summary>Spec 037 / FR-005 — admin attached a company to an applicant (at creation or via Add).</summary>
    public const string ActionCompanyCreate = "company.create";
    /// <summary>Spec 037 / FR-006 — admin renamed a company (payload carries old/new name).</summary>
    public const string ActionCompanyRename = "company.rename";
    /// <summary>Spec 037 / FR-007 — admin archived a company.</summary>
    public const string ActionCompanyArchive = "company.archive";
    /// <summary>Spec 037 / FR-007 — admin unarchived a company.</summary>
    public const string ActionCompanyUnarchive = "company.unarchive";

    /// <summary>Spec 037 — target-type discriminator for company mutations.
    /// The <c>company.</c> prefix routes here in <c>AdminAuditEventWriter</c>.</summary>
    public const string TargetTypeCompany = "company";

    // ---------- Spec 038 — provider regulatory compliance mutations (auditor). ----------
    /// <summary>Spec 038 — auditor changed a Hacienda/CCSS/SICOP regulatory status.</summary>
    public const string SupplierRegulatoryChanged = "supplier.regulatory_changed";
    /// <summary>Spec 038 — auditor re-authorized a regulatory status with no value change.</summary>
    public const string SupplierRegulatoryReviewed = "supplier.regulatory_reviewed";
    /// <summary>Spec 038 — auditor toggled the PME/PYME flag.</summary>
    public const string SupplierPmeChanged = "supplier.pme_changed";
    /// <summary>Spec 038 — auditor changed the provider warning flag/note.</summary>
    public const string SupplierWarningChanged = "supplier.warning_changed";

    /// <summary>Spec 038 — target-type discriminator for provider regulatory mutations.
    /// The <c>supplier.</c> prefix routes here in <c>AdminAuditEventWriter</c> and,
    /// unlike the other prefixes, sets <c>TargetId = supplierId</c> (not the "0"
    /// sentinel) so the trail is queryable per provider.</summary>
    public const string TargetTypeSupplier = "supplier";

    public long Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public string TargetId { get; private set; } = string.Empty;
    public string? PayloadJson { get; private set; }

    private AdminAuditEvent() { }

    private AdminAuditEvent(string actorUserId, string action, string targetType, string targetId, string? payloadJson)
    {
        ActorUserId = actorUserId;
        Action = action;
        TargetType = targetType;
        TargetId = targetId;
        PayloadJson = payloadJson;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    /// <summary>NFR-005 — every audit row carries an actor + timestamp. The
    /// factory validates non-empty fields so callers cannot persist a
    /// malformed row.</summary>
    public static AdminAuditEvent Record(
        string actorUserId,
        string action,
        string targetType,
        string targetId,
        string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new ArgumentException("ActorUserId is required.", nameof(actorUserId));
        }
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Action is required.", nameof(action));
        }
        if (string.IsNullOrWhiteSpace(targetType))
        {
            throw new ArgumentException("TargetType is required.", nameof(targetType));
        }
        if (string.IsNullOrWhiteSpace(targetId))
        {
            throw new ArgumentException("TargetId is required.", nameof(targetId));
        }
        return new AdminAuditEvent(actorUserId, action, targetType, targetId, payloadJson);
    }
}
