using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Entities;

public class Application
{
    private readonly List<Item> _items = [];
    private readonly List<ApplicationImpact> _impacts = [];
    private readonly List<VersionHistory> _versionHistory = [];
    private readonly List<ApplicantResponse> _applicantResponses = [];
    private readonly List<Appeal> _appeals = [];
    private readonly List<Tranche> _tranches = []; // Spec 046 — per-application funding phases.
    private FundingAgreement? _fundingAgreement;

    public int Id { get; private set; }
    public int ApplicantId { get; private set; }

    /// <summary>
    /// Spec 029 / FR-017 — authoritative anchor captured at creation. The Group
    /// fixes the application's Process (<c>Group.Process</c>) and Fund
    /// (<c>Group.Process.Fund</c>) exactly, replacing the prior nondeterministic
    /// group-membership inference. Immutable post-creation (FR-018; re-anchoring
    /// is out of scope).
    /// </summary>
    public int GroupId { get; private set; }
    public Group? Group { get; private set; }
    /// <summary>
    /// Spec 018 → 037 — commercial entity name (`Empresa solicitante`). Since spec
    /// 037 this is a <b>frozen name snapshot</b> copied from the selected
    /// <see cref="Company"/> at creation (and re-copied on draft re-select), not
    /// applicant free text. Required (non-nullable), trimmed, ≤200 chars. Renaming
    /// the source Company never rewrites this snapshot (FR-016 historical preservation).
    /// </summary>
    public string CompanyName { get; private set; } = string.Empty;

    /// <summary>
    /// Spec 037 / FR-002 — live reference to the admin-managed <see cref="Company"/>
    /// the applicant selected. Nullable (greenfield; pre-037 rows + test builders
    /// keep <c>null</c> + their snapshot). Set at creation and on draft re-select
    /// (<see cref="SetCompany"/>); frozen at submission via <see cref="EnsureNotFrozen"/>.
    /// </summary>
    public int? CompanyId { get; private set; }

    /// <summary>
    /// Spec 021 / FR-008 — opaque, human-readable identifier surfaced on every
    /// applicant-facing surface (dashboard, reviewer queue, signing inbox,
    /// Funding Agreement PDF, notification emails). Immutable post-construction:
    /// once the Infrastructure generator stamps it via <see cref="AssignPublicCode"/>,
    /// the code stays for the life of the Application.
    /// </summary>
    public PublicCode? PublicCode { get; private set; }

    /// <summary>
    /// Spec 035 (evolved 2026-06-16, D13) — the impacts this application declares
    /// (one or more). Each is a chosen impact template + its values. Line items
    /// attribute themselves to these via <see cref="Item.ItemImpacts"/>. Mutated
    /// through <see cref="AddImpact"/> / <see cref="RemoveImpact"/>.
    /// </summary>
    public IReadOnlyList<ApplicationImpact> Impacts => _impacts.AsReadOnly();

    /// <summary>
    /// Spec 021 / FR-006 / R-2 — bitfield tracking which reminder emails have
    /// already been sent for the active stage. Bits per <c>StageExpiryReminderService</c>:
    /// 0x1 = T-72h, 0x2 = T-24h, 0x4 = expiry. Reset by <see cref="ResetStageState"/>
    /// when the Application enters a new stage.
    /// </summary>
    public byte RemindersSentMask { get; private set; }

    /// <summary>
    /// Spec 021 / FR-006 — UTC instant at which the Application entered the current
    /// stage. Combined with the (per-Process override or platform default) window
    /// duration to compute the stage-closure timestamp consulted by the banner +
    /// reminder service + Submit guard.
    /// </summary>
    public DateTimeOffset StageEnteredAt { get; private set; }

    /// <summary>
    /// Spec 021 / FR-021 — soft-delete column. Dashboard projections filter on
    /// <c>DeletedAt IS NULL</c> via <c>IApplicationQueryFilter.ExcludeDeleted</c>;
    /// rows are never hard-deleted.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>True when the row has been soft-deleted (FR-021).</summary>
    public bool IsDeleted => DeletedAt is not null;

    /// <summary>
    /// Spec 029 / FR-021 — freeze overlay: true when the governing Fund (via the
    /// anchored <c>Group.Process.Fund</c>) is Archived. Derived from the loaded
    /// navigation chain, so the service layer must Include
    /// <c>Group.Process.Fund</c> for the domain guard to fire (T048); the
    /// controller boundary guard is the primary enforcement (defense-in-depth, D6).
    /// Orthogonal to the Draft→Submitted→… state machine — it gates mutation
    /// without changing the persisted <see cref="State"/>.
    /// </summary>
    public bool IsFrozen => Group?.Process?.Fund?.Status == FundStatus.Archived;

    public ApplicationState State { get; private set; } = ApplicationState.Draft;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public Applicant Applicant { get; private set; } = null!;

    public IReadOnlyList<Item> Items => _items.AsReadOnly();
    /// <summary>Spec 046 — the reviewer-defined funding phases. Unassigned lines
    /// (<see cref="Item.TrancheId"/> == null) fall into a virtual default tranche with no row.</summary>
    public IReadOnlyList<Tranche> Tranches => _tranches.AsReadOnly();
    public IReadOnlyList<VersionHistory> VersionHistory => _versionHistory.AsReadOnly();
    public IReadOnlyList<ApplicantResponse> ApplicantResponses => _applicantResponses.AsReadOnly();
    public IReadOnlyList<Appeal> Appeals => _appeals.AsReadOnly();
    public FundingAgreement? FundingAgreement => _fundingAgreement;

    private Application() { }

    /// <summary>
    /// Spec 037 / D7 — creates a draft anchored to its Group, referencing the
    /// selected <see cref="Company"/> (<paramref name="companyId"/>) and freezing
    /// its name into the <see cref="CompanyName"/> snapshot. <paramref name="companyId"/>
    /// is nullable: the production applicant-create path always supplies a real,
    /// ownership-validated company id, while test builders and pre-037 rows pass
    /// <c>null</c> (the FK is nullable).
    /// </summary>
    public Application(int applicantId, int groupId, int? companyId, string companyName)
    {
        if (groupId <= 0)
        {
            throw new ArgumentException("An application must be anchored to a Group.", nameof(groupId));
        }
        ApplicantId = applicantId;
        GroupId = groupId;
        CompanyId = companyId;
        State = ApplicationState.Draft;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        StageEnteredAt = DateTimeOffset.UtcNow;
        SetCompanyName(companyName);
    }

    /// <summary>
    /// Spec 029 / FR-021 — guard invoked by every applicant/reviewer-facing
    /// mutating method. Throws <see cref="FundArchivedException"/> when the
    /// governing Fund is Archived. No-op when the navigation chain is not loaded
    /// (the controller boundary guard remains the primary enforcement).
    /// </summary>
    private void EnsureNotFrozen()
    {
        if (IsFrozen)
        {
            throw new FundArchivedException();
        }
    }

    /// <summary>
    /// Spec 021 / FR-008 — stamps the Infrastructure-generated PublicCode onto
    /// the Application exactly once. Subsequent calls throw — the public code is
    /// immutable for the life of the row.
    /// </summary>
    /// <exception cref="InvalidOperationException">A PublicCode is already assigned.</exception>
    public void AssignPublicCode(PublicCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (PublicCode is not null)
        {
            throw new InvalidOperationException(
                $"PublicCode is already assigned ({PublicCode}); it cannot be replaced.");
        }
        PublicCode = code;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 035 / D5 — counts quotations across all items that reference the given
    /// document, supporting reference-counted blob retention (a document is shared
    /// when an applicant reuses a quotation across sibling items). The blob is
    /// deleted only when this returns 0 after a quotation row is detached.
    /// </summary>
    public int CountQuotationsReferencingDocument(int documentId)
    {
        return _items.Sum(i => i.Quotations.Count(q => q.DocumentId == documentId));
    }

    /// <summary>
    /// Spec 021 / FR-021 — admin soft-delete. Idempotent. Hard-delete is not used
    /// in scope 021; the row remains for audit/history but every dashboard query
    /// filters it out via <c>IApplicationQueryFilter.ExcludeDeleted</c>.
    /// </summary>
    public void SoftDelete()
    {
        if (IsDeleted) return;
        DeletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 021 / US9 / FR-035–FR-037, FR-040 — applicant-initiated removal. A
    /// <c>Draft</c> is deleted; a <c>Submitted</c>/<c>UnderReview</c> Application is
    /// withdrawn. Both reuse <see cref="SoftDelete"/> (no distinct Withdrawn state).
    /// Reviewer notification is requested only for an <c>UnderReview</c> withdrawal.
    /// Terminal states (<c>Resolved</c>, <c>AppealOpen</c>, <c>ResponseFinalized</c>,
    /// <c>AgreementExecuted</c>) reject the operation. Idempotent: a repeat on an
    /// already soft-deleted row is a no-op and never re-requests notification.
    /// </summary>
    /// <exception cref="InvalidOperationException">The Application is past the point an applicant may remove it.</exception>
    public ApplicantRemovalOutcome RemoveByApplicant()
    {
        EnsureNotFrozen();
        if (IsDeleted)
        {
            return new ApplicantRemovalOutcome(ApplicantRemovalKind.NoOp, NotifyReviewers: false, PriorState: State);
        }

        var priorState = State;
        switch (State)
        {
            case ApplicationState.Draft:
                SoftDelete();
                return new ApplicantRemovalOutcome(ApplicantRemovalKind.DraftDeleted, NotifyReviewers: false, priorState);

            case ApplicationState.Submitted:
                SoftDelete();
                return new ApplicantRemovalOutcome(ApplicantRemovalKind.Withdrawn, NotifyReviewers: false, priorState);

            case ApplicationState.UnderReview:
                SoftDelete();
                return new ApplicantRemovalOutcome(ApplicantRemovalKind.Withdrawn, NotifyReviewers: true, priorState);

            default:
                throw new InvalidOperationException(
                    $"Application in '{State}' state cannot be deleted or withdrawn by the applicant.");
        }
    }

    /// <summary>
    /// Spec 021 / FR-006 — reset reminder state and stamp <see cref="StageEnteredAt"/>
    /// to now. Invoked on every transition that crosses a stage boundary
    /// (Draft → Submitted, Submitted → UnderReview, Resolved → ResponseFinalized,
    /// AppealOpen → Draft|UnderReview, …).
    /// </summary>
    private void ResetStageState()
    {
        StageEnteredAt = DateTimeOffset.UtcNow;
        RemindersSentMask = 0;
    }

    /// <summary>
    /// Spec 021 / R-2 — marks the given reminder bit (0x1 / 0x2 / 0x4) as sent so
    /// the hourly hosted service does not double-fire on the same Application.
    /// </summary>
    public void MarkReminderSent(byte bit)
    {
        if (bit is not (0x1 or 0x2 or 0x4))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bit), bit, "Bit must be 0x1 (T-72h), 0x2 (T-24h), or 0x4 (expiry).");
        }
        RemindersSentMask = (byte)(RemindersSentMask | bit);
    }

    /// <summary>
    /// Spec 037 / FR-015 / FR-016 — re-selects the application's company while it is
    /// still a mutable Draft (autosave draft re-select). Updates the live
    /// <see cref="CompanyId"/> reference and re-copies the name into the frozen
    /// <see cref="CompanyName"/> snapshot. Guarded by <see cref="EnsureNotFrozen"/>;
    /// the snapshot is frozen once the application is submitted.
    /// </summary>
    public void SetCompany(int companyId, string nameSnapshot)
    {
        EnsureNotFrozen();
        // Spec 037 / FR-015 — the selected company is mutable only while Draft and is
        // frozen at submission. Guard here (not just in the UI / autosave handler) so
        // a forged re-select against a submitted application is rejected (FR-019).
        if (State != ApplicationState.Draft)
        {
            throw new InvalidOperationException(
                "La empresa solo puede cambiarse mientras la solicitud es un borrador.");
        }
        CompanyId = companyId;
        SetCompanyName(nameSnapshot);
    }

    /// <summary>
    /// Spec 018 → 037 — private snapshot setter for the commercial entity name.
    /// Trims whitespace, rejects null/empty/whitespace-only input, and enforces a
    /// 200-character maximum after trim. Persists the trimmed value and bumps
    /// <see cref="UpdatedAt"/>. Since spec 037 the only callers are the constructor
    /// and <see cref="SetCompany"/> (the applicant free-text path is gone).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="companyName"/> is null/whitespace or exceeds 200 chars after trim.
    /// </exception>
    private void SetCompanyName(string companyName)
    {
        EnsureNotFrozen();
        if (companyName is null)
        {
            var ex = new ArgumentException("Company name is required.", nameof(companyName));
            ex.Data[Item.ValidationReasonKey] = CompanyNameRequiredReason;
            throw ex;
        }
        var trimmed = companyName.Trim();
        if (trimmed.Length == 0)
        {
            var ex = new ArgumentException("Company name is required.", nameof(companyName));
            ex.Data[Item.ValidationReasonKey] = CompanyNameRequiredReason;
            throw ex;
        }
        if (trimmed.Length > 200)
        {
            var ex = new ArgumentException("Company name must be 200 characters or fewer.", nameof(companyName));
            ex.Data[Item.ValidationReasonKey] = CompanyNameTooLongReason;
            throw ex;
        }

        CompanyName = trimmed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Spec 018 — stable discriminator value for the CompanyName-required validation branch.</summary>
    public const string CompanyNameRequiredReason = "CompanyNameRequired";
    /// <summary>Spec 018 — stable discriminator value for the CompanyName-too-long validation branch.</summary>
    public const string CompanyNameTooLongReason = "CompanyNameTooLong";

    /// <summary>
    /// Adds an item to the application.
    /// </summary>
    public void AddItem(Item item)
    {
        EnsureNotFrozen();
        _items.Add(item);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 018 / FR-012 / FR-013 / FR-014 — assigns the reviewer-supplied line code
    /// to the item identified by <paramref name="itemId"/>. Trims whitespace, rejects
    /// null/empty/whitespace-only input, enforces a 16-character maximum after trim,
    /// and rejects duplicates against any sibling item in this Application
    /// (case-sensitive, per-Application uniqueness scope).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the item is not found in this Application or another sibling item
    /// already carries the same trimmed code.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown via <see cref="Item.AssignLineCode"/> when the trimmed code is empty or > 16 chars.
    /// </exception>
    public void AssignLineCodeToItem(int itemId, string lineCode)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"Item {itemId} is not part of this application.");

        var trimmed = (lineCode ?? string.Empty).Trim();

        // Per-Application uniqueness: only check sibling items (exclude self).
        // Allows reviewers to re-assign the same code to the same item idempotently.
        if (trimmed.Length > 0)
        {
            var collision = _items
                .Where(i => i.Id != itemId)
                .Any(i => string.Equals(i.LineCode, trimmed, StringComparison.Ordinal));
            if (collision)
            {
                throw new InvalidOperationException(
                    $"Line code '{trimmed}' is already assigned to another item in this application.");
            }
        }

        item.AssignLineCode(trimmed);
        UpdatedAt = DateTime.UtcNow;
    }

    // ---------- Spec 046 — tranche (funding-phase) structure, reviewer-owned, frozen at execution. ----------

    /// <summary>
    /// Spec 046 / FR-002 (research D4) — the tranche structure (create/rename/delete/assign) is
    /// frozen once the agreement executes. Mutations after <see cref="ApplicationState.AgreementExecuted"/>
    /// throw. There is no execution-time hook (<see cref="ExecuteAgreement"/> is a pure state flip) —
    /// the freeze is enforced by this guard at every tranche mutation entry point.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown once the application is executed.</exception>
    private void EnsureTranchesEditable()
    {
        if (State == ApplicationState.AgreementExecuted)
        {
            var ex = new InvalidOperationException(
                "Tranche structure is frozen: the funding agreement has been executed.");
            ex.Data[TrancheFrozenKey] = true;
            throw ex;
        }
    }

    /// <summary>Spec 046 — discriminator on <see cref="System.Exception.Data"/> so the service layer
    /// can map the freeze throw to the es-CR <c>TrancheFrozen</c> reason without message-string matching.</summary>
    public const string TrancheFrozenKey = "FundingPlatform.TrancheFrozen";

    /// <summary>
    /// Spec 046 / FR-001 — creates a funding phase with the next display ordinal. Rejects a
    /// duplicate name within this application (case-insensitive; the service adds accent-insensitivity
    /// and the DB unique index backstops races). Frozen after execution.
    /// </summary>
    /// <exception cref="InvalidOperationException">Frozen, or a sibling tranche already has the name.</exception>
    /// <exception cref="ArgumentException">Via <see cref="Tranche.Create"/> when the name is empty/too long.</exception>
    public Tranche CreateTranche(string name)
    {
        EnsureTranchesEditable();
        var trimmed = (name ?? string.Empty).Trim();
        EnsureTrancheNameAvailable(trimmed, excludeTrancheId: null);

        var nextOrdinal = _tranches.Count == 0 ? 1 : _tranches.Max(t => t.Ordinal) + 1;
        var tranche = Tranche.Create(Id, trimmed, nextOrdinal);
        _tranches.Add(tranche);
        UpdatedAt = DateTime.UtcNow;
        return tranche;
    }

    /// <summary>Spec 046 / FR-001 — renames a funding phase. Rejects a duplicate sibling name.
    /// Frozen after execution.</summary>
    public void RenameTranche(int trancheId, string name)
    {
        EnsureTranchesEditable();
        var tranche = _tranches.FirstOrDefault(t => t.Id == trancheId)
            ?? throw new InvalidOperationException($"Tranche {trancheId} is not part of this application.");

        var trimmed = (name ?? string.Empty).Trim();
        EnsureTrancheNameAvailable(trimmed, excludeTrancheId: trancheId);

        tranche.Rename(trimmed);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Spec 046 / FR-001 — deletes a funding phase, re-parenting its member lines to the
    /// synthetic default tranche (<see cref="Item.TrancheId"/> = null). Frozen after execution.</summary>
    public void DeleteTranche(int trancheId)
    {
        EnsureTranchesEditable();
        var tranche = _tranches.FirstOrDefault(t => t.Id == trancheId)
            ?? throw new InvalidOperationException($"Tranche {trancheId} is not part of this application.");

        foreach (var item in _items.Where(i => i.TrancheId == trancheId))
        {
            item.AssignTranche(null);
        }
        _tranches.Remove(tranche);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 046 / FR-001 — assigns a line to a tranche (or, with <paramref name="trancheId"/> null,
    /// unassigns it → synthetic default). Both the item and the target tranche must belong to this
    /// application. Frozen after execution.
    /// </summary>
    /// <exception cref="InvalidOperationException">Frozen, or the item / tranche is not part of this application.</exception>
    public void AssignItemToTranche(int itemId, int? trancheId)
    {
        EnsureTranchesEditable();
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"Item {itemId} is not part of this application.");

        if (trancheId is { } tid && _tranches.All(t => t.Id != tid))
        {
            throw new InvalidOperationException($"Tranche {tid} is not part of this application.");
        }

        item.AssignTranche(trancheId);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 046 / FR-009 — obligates a budget-line (Financial Operator). Post-execution and
    /// operator-owned, so it is deliberately NOT subject to the tranche-structure freeze; the
    /// disbursement service enforces the executed-state gate. Idempotent.
    /// </summary>
    /// <exception cref="InvalidOperationException">The item is not part of this application.</exception>
    public void CommitLine(int itemId)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"Item {itemId} is not part of this application.");
        item.Commit();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 046 / FR-007 — reverses a commitment. The "no recorded payment" guard is enforced by the
    /// disbursement service (it can see attributions); the aggregate just flips the state. Idempotent.
    /// </summary>
    /// <exception cref="InvalidOperationException">The item is not part of this application.</exception>
    public void UncommitLine(int itemId)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"Item {itemId} is not part of this application.");
        item.Uncommit();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 047 / FR-016 — closes a budget-line. The closure gate (required docs + payments validated
    /// + paid==accepted + fully allocated) is enforced by the closure service (it can see attributions/
    /// evidence); the aggregate just flips the stored state + stamp. Idempotent.
    /// </summary>
    /// <exception cref="InvalidOperationException">The item is not part of this application.</exception>
    public void CloseLine(int itemId, string userId, string? reason)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"Item {itemId} is not part of this application.");
        item.Close(userId, reason);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 047 / FR-017 — reopens a closed budget-line with a required reason. Off-ledger — no
    /// balance change. Idempotent on an already-open line.
    /// </summary>
    /// <exception cref="InvalidOperationException">The item is not part of this application.</exception>
    public void ReopenLine(int itemId, string userId, string reason)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId)
            ?? throw new InvalidOperationException($"Item {itemId} is not part of this application.");
        item.Reopen(userId, reason);
        UpdatedAt = DateTime.UtcNow;
    }

    private void EnsureTrancheNameAvailable(string trimmedName, int? excludeTrancheId)
    {
        if (trimmedName.Length == 0)
        {
            return; // Tranche.Create/Rename raises the required-name error.
        }
        var collision = _tranches
            .Where(t => excludeTrancheId is null || t.Id != excludeTrancheId)
            .Any(t => string.Equals(t.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
        if (collision)
        {
            throw new InvalidOperationException($"A tranche named '{trimmedName}' already exists in this application.");
        }
    }

    /// <summary>
    /// Removes an item from the application by its identifier.
    /// </summary>
    public void RemoveItem(int itemId)
    {
        EnsureNotFrozen();
        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is not null)
        {
            _items.Remove(item);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Validates the application and transitions its state to Submitted.
    /// Throws <see cref="InvalidOperationException"/> if any validation errors are found.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the application fails validation.</exception>
    public void Submit(int minQuotations)
    {
        EnsureNotFrozen();
        var errors = Validate(minQuotations);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot submit application: {string.Join("; ", errors)}");
        }

        State = ApplicationState.Submitted;
        SubmittedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        ResetStageState();
    }

    /// <summary>
    /// Records a version history entry for this application.
    /// </summary>
    public void AddVersionHistory(VersionHistory entry)
    {
        _versionHistory.Add(entry);
    }

    /// <summary>
    /// Spec 035 (evolved 2026-06-16, D13 / FR-006) — declares a new impact on the
    /// application: an impact template plus its entered parameter values. Rejects a
    /// duplicate template (mirrors the DB UNIQUE(ApplicationId, ImpactTemplateId)).
    /// </summary>
    /// <exception cref="InvalidOperationException">When the template is already declared.</exception>
    public ApplicationImpact AddImpact(ImpactTemplate template, IEnumerable<ImpactParameterValue> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        if (_impacts.Any(i => i.ImpactTemplateId == template.Id))
        {
            throw new InvalidOperationException(
                $"Impact template '{template.Name}' is already declared on this application.");
        }

        var impact = new ApplicationImpact(template.Id);
        impact.SetValues(values);
        _impacts.Add(impact);
        UpdatedAt = DateTime.UtcNow;
        return impact;
    }

    /// <summary>
    /// Spec 035 (evolved 2026-06-16, D14 / SC-007) — removes a declared impact AND strips
    /// every line item's attribution to it. The DB FK on <c>ItemImpacts.ApplicationImpactId</c>
    /// is NO ACTION (multi-cascade-path avoidance), so this cleanup MUST happen in the domain.
    /// No-op when the id is not a declared impact.
    /// </summary>
    public void RemoveImpact(int applicationImpactId)
    {
        var impact = _impacts.FirstOrDefault(i => i.Id == applicationImpactId);
        if (impact is null)
        {
            return;
        }

        foreach (var item in _items)
        {
            item.RemoveAttribution(applicationImpactId);
        }

        _impacts.Remove(impact);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Validates the application and returns a list of validation error messages.
    /// Spec 035 (evolved 2026-06-16, D13/D14/D16) — impact data is per application
    /// (≥1 declared impact, required values checked service-side); each line item must
    /// be attributed to ≥1 of those declared impacts, carry a non-empty justification,
    /// and have its required category fields. Collected all-at-once (Constitution gate).
    /// </summary>
    public List<string> Validate(int minQuotations)
    {
        var errors = new List<string>();

        if (_items.Count == 0)
        {
            errors.Add("Application must have at least one item.");
        }

        if (_impacts.Count == 0)
        {
            // Spec 035 / FR-006 / SC-006 — es-CR submit-block message. ("impacto"
            // carries the "impact" substring the ApplicationSubmitGuardTests assert.)
            errors.Add("La solicitud debe declarar al menos un impacto.");
        }

        var declaredImpactIds = _impacts.Select(i => i.Id).ToHashSet();

        foreach (var item in _items)
        {
            if (!item.HasMinimumQuotations(minQuotations))
            {
                errors.Add(
                    $"Item '{item.ProductName}' must have at least {minQuotations} quotation(s).");
            }

            if (item.ItemImpacts.Count == 0)
            {
                // Spec 035 / FR-007 / SC-006 — es-CR, names the line item.
                errors.Add(
                    $"El ítem '{item.ProductName}' debe estar asociado al menos a un impacto.");
            }
            else if (declaredImpactIds.Count > 0 &&
                     item.ItemImpacts.Any(ii => !declaredImpactIds.Contains(ii.ApplicationImpactId)))
            {
                // Spec 035 / SC-007 — attribution must target a declared impact.
                errors.Add(
                    $"El ítem '{item.ProductName}' está asociado a un impacto que ya no existe en la solicitud.");
            }

            // Spec 035 / FR-008 made the per-item impact justification required; this was
            // relaxed (2026-06-18, stakeholder request) — the justification is now OPTIONAL
            // and no longer blocks submission. The field is still captured + displayed when
            // present.

            foreach (var missingLabel in item.MissingRequiredCategoryFields())
            {
                // Spec 035 / FR-013 / SC-006 — es-CR message naming the line item and
                // the missing required category field.
                errors.Add(
                    $"Al ítem '{item.ProductName}' le falta el campo requerido '{missingLabel}'.");
            }
        }

        return errors;
    }

    /// <summary>
    /// Transitions the application from Submitted to Under Review.
    /// Idempotent — no-op if already Under Review.
    /// </summary>
    public void StartReview()
    {
        if (State == ApplicationState.UnderReview)
            return;

        if (State != ApplicationState.Submitted)
        {
            throw new InvalidOperationException(
                $"Cannot start review: application is in '{State}' state, expected 'Submitted'.");
        }

        State = ApplicationState.UnderReview;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Sends the application back to Draft. Resets all item review statuses to Pending.
    /// Preserves item review comments.
    /// </summary>
    public void SendBack()
    {
        if (State != ApplicationState.UnderReview)
        {
            throw new InvalidOperationException(
                $"Cannot send back: application is in '{State}' state, expected 'UnderReview'.");
        }

        State = ApplicationState.Draft;
        SubmittedAt = null;
        UpdatedAt = DateTime.UtcNow;

        foreach (var item in _items)
        {
            item.ResetReviewStatus();
        }
    }

    /// <summary>
    /// Finalizes the review, transitioning the application to Resolved.
    /// If force is false and there are unresolved items (Pending or NeedsInfo), throws an exception.
    /// If force is true, unresolved items are implicitly rejected.
    /// </summary>
    public void Finalize(bool force)
    {
        if (State != ApplicationState.UnderReview)
        {
            throw new InvalidOperationException(
                $"Cannot finalize: application is in '{State}' state, expected 'UnderReview'.");
        }

        var unresolvedItems = _items
            .Where(i => i.ReviewStatus == Enums.ItemReviewStatus.Pending
                     || i.ReviewStatus == Enums.ItemReviewStatus.NeedsInfo)
            .ToList();

        if (unresolvedItems.Count > 0 && !force)
        {
            var itemNames = string.Join(", ", unresolvedItems.Select(i => $"'{i.ProductName}'"));
            throw new InvalidOperationException(
                $"Cannot finalize: the following items are unresolved: {itemNames}. Use force to implicitly reject them.");
        }

        if (force)
        {
            foreach (var item in unresolvedItems)
            {
                item.Reject("Implicitly rejected during finalization");
            }
        }

        State = ApplicationState.Resolved;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Submits the applicant's per-item response. Transitions the application from
    /// Resolved to ResponseFinalized. Requires a decision for every item on the application.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the application is not in the Resolved state, or when the decision map
    /// does not cover every item exactly once.
    /// </exception>
    public ApplicantResponse SubmitResponse(
        IReadOnlyDictionary<int, ItemResponseDecision> itemDecisions,
        string submittedByUserId)
    {
        if (State != ApplicationState.Resolved)
        {
            throw new InvalidOperationException(
                $"Cannot submit response: application is in '{State}' state, expected 'Resolved'.");
        }

        var cycleNumber = _applicantResponses.Count + 1;
        var itemIds = _items.Select(i => i.Id).ToList();

        var response = ApplicantResponse.Submit(
            Id,
            cycleNumber,
            submittedByUserId,
            itemIds,
            itemDecisions);

        _applicantResponses.Add(response);
        State = ApplicationState.ResponseFinalized;
        UpdatedAt = DateTime.UtcNow;

        return response;
    }

    /// <summary>
    /// Opens an appeal against the most recent applicant response. Freezes the application
    /// by transitioning to AppealOpen.
    /// </summary>
    public Appeal OpenAppeal(string openedByUserId, int maxAppeals)
    {
        if (State != ApplicationState.ResponseFinalized)
        {
            throw new InvalidOperationException(
                $"Cannot open appeal: application is in '{State}' state, expected 'ResponseFinalized'.");
        }

        if (_appeals.Count >= maxAppeals)
        {
            throw new InvalidOperationException(
                $"Cannot open appeal: maximum appeal count ({maxAppeals}) reached.");
        }

        var latestResponse = _applicantResponses
            .OrderByDescending(r => r.CycleNumber)
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Cannot open appeal: no applicant response exists for this application.");

        if (!latestResponse.ItemResponses.Any(ir => ir.Decision == ItemResponseDecision.Reject))
        {
            throw new InvalidOperationException(
                "Cannot open appeal: the response does not include any rejected items.");
        }

        var appeal = Appeal.Open(Id, latestResponse.Id, openedByUserId);
        _appeals.Add(appeal);
        State = ApplicationState.AppealOpen;
        UpdatedAt = DateTime.UtcNow;

        return appeal;
    }

    /// <summary>
    /// Resolves the active appeal as Uphold. Application returns to ResponseFinalized.
    /// </summary>
    public void ResolveAppealAsUphold(string resolvedByUserId)
    {
        var appeal = GetActiveAppealOrThrow();
        appeal.Resolve(resolvedByUserId, AppealResolution.Uphold);

        State = ApplicationState.ResponseFinalized;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Resolves the active appeal as Grant — Reopen to Draft. Application returns to Draft
    /// so the applicant can revise the submission.
    /// </summary>
    public void ResolveAppealAsGrantReopenToDraft(string resolvedByUserId)
    {
        var appeal = GetActiveAppealOrThrow();
        appeal.Resolve(resolvedByUserId, AppealResolution.GrantReopenToDraft);

        State = ApplicationState.Draft;
        SubmittedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Resolves the active appeal as Grant — Reopen to Review. Application returns to
    /// UnderReview WITHOUT resetting item review statuses (unlike SendBack).
    /// </summary>
    public void ResolveAppealAsGrantReopenToReview(string resolvedByUserId)
    {
        var appeal = GetActiveAppealOrThrow();
        appeal.Resolve(resolvedByUserId, AppealResolution.GrantReopenToReview);

        State = ApplicationState.UnderReview;
        UpdatedAt = DateTime.UtcNow;
    }

    private Appeal GetActiveAppealOrThrow()
    {
        if (State != ApplicationState.AppealOpen)
        {
            throw new InvalidOperationException(
                $"Cannot resolve appeal: application is in '{State}' state, expected 'AppealOpen'.");
        }

        return _appeals
            .OrderByDescending(a => a.OpenedAt)
            .FirstOrDefault(a => a.Status == AppealStatus.Open)
            ?? throw new InvalidOperationException("Cannot resolve appeal: no open appeal found.");
    }

    // --- Funding Agreement (spec 005) ---

    /// <summary>
    /// Evaluates FR-002 preconditions for generating a Funding Agreement.
    /// Returns true when all preconditions hold; otherwise false with user-presentable
    /// messages describing each failed precondition.
    /// </summary>
    public bool CanGenerateFundingAgreement(out IReadOnlyList<string> errors)
    {
        var failures = new List<string>();

        // Spec 040 / D11 — PDF generation moved to the auditor stage. The agreement is
        // generated while the application is in PendingAudit; the legacy ResponseFinalized
        // path stays valid (e.g. an admin acting before/around the audit hand-off and the
        // unchanged signing-phase regenerate). Both states satisfy the state precondition.
        if (State is not (ApplicationState.ResponseFinalized or ApplicationState.PendingAudit))
        {
            if (State == ApplicationState.AppealOpen)
            {
                failures.Add("An appeal is currently open on this application.");
            }
            else
            {
                failures.Add("Review is still in progress.");
            }
        }

        AddFundingContentPreconditionFailures(failures);

        errors = failures
            .Distinct(StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        return errors.Count == 0;
    }

    /// <summary>
    /// Spec 040 / D3 — the content preconditions for generating a Funding Agreement,
    /// independent of the workflow state check: no open appeal, an applicant response
    /// exists, and at least one item was accepted. Shared by
    /// <see cref="CanGenerateFundingAgreement"/> and
    /// <see cref="CanAuditorGenerateFundingAgreement"/>.
    /// </summary>
    private void AddFundingContentPreconditionFailures(List<string> failures)
    {
        if (_appeals.Any(a => a.Status == AppealStatus.Open))
        {
            failures.Add("An appeal is currently open on this application.");
        }

        var latestResponse = _applicantResponses
            .OrderByDescending(r => r.CycleNumber)
            .FirstOrDefault();

        if (latestResponse is null)
        {
            failures.Add("Applicant has not yet responded to every approved item.");
        }
        else
        {
            if (latestResponse.ItemResponses.Count == 0)
            {
                failures.Add("Applicant has not yet responded to every approved item.");
            }

            if (!latestResponse.ItemResponses.Any(ir => ir.Decision == ItemResponseDecision.Accept))
            {
                failures.Add("Nothing to fund: all items were rejected.");
            }
        }
    }

    /// <summary>
    /// Spec 040 / D3 / D11 — the auditor generation gate. Composes the
    /// PendingAudit state requirement, audit-checklist completeness (evaluated by the
    /// Application service against the active Auditor-stage template items and passed
    /// in), and the shared content preconditions. es-CR refusals (FR-015).
    /// </summary>
    public bool CanAuditorGenerateFundingAgreement(
        bool auditChecklistComplete, out IReadOnlyList<string> errors)
    {
        var failures = new List<string>();

        if (State != ApplicationState.PendingAudit)
        {
            failures.Add("La solicitud no está en auditoría.");
        }

        if (!auditChecklistComplete)
        {
            failures.Add("La lista de verificación de auditoría está incompleta.");
        }

        AddFundingContentPreconditionFailures(failures);

        errors = failures
            .Distinct(StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        return errors.Count == 0;
    }

    /// <summary>
    /// Creates a FundingAgreement for this application. Requires no existing agreement
    /// and passing preconditions.
    /// </summary>
    public FundingAgreement GenerateFundingAgreement(
        string fileName,
        string contentType,
        long size,
        string blobKey,
        string generatingUserId)
    {
        if (!CanGenerateFundingAgreement(out var errors))
        {
            throw new InvalidOperationException(
                $"Cannot generate Funding Agreement: {string.Join(" ", errors)}");
        }

        if (_fundingAgreement is not null)
        {
            throw new InvalidOperationException(
                "A Funding Agreement already exists for this application. Use RegenerateFundingAgreement.");
        }

        _fundingAgreement = new FundingAgreement(
            Id,
            fileName,
            contentType,
            size,
            blobKey,
            generatingUserId);
        UpdatedAt = DateTime.UtcNow;

        return _fundingAgreement;
    }

    /// <summary>
    /// Replaces the existing Funding Agreement's file metadata in place. Requires an
    /// existing agreement and passing preconditions.
    /// </summary>
    public FundingAgreement RegenerateFundingAgreement(
        string fileName,
        string contentType,
        long size,
        string blobKey,
        string regeneratingUserId)
    {
        if (!CanRegenerateFundingAgreement(out var errors))
        {
            throw new InvalidOperationException(
                $"Cannot regenerate Funding Agreement: {string.Join(" ", errors)}");
        }

        _fundingAgreement!.Replace(fileName, contentType, size, blobKey, regeneratingUserId);
        UpdatedAt = DateTime.UtcNow;

        return _fundingAgreement;
    }

    /// <summary>
    /// Authorization: download / read access. Applicant-owner, any administrator, or
    /// a reviewer explicitly assigned to this application's review.
    /// </summary>
    public bool CanUserAccessFundingAgreement(
        string? applicantUserId,
        bool isAdministrator,
        bool isReviewerAssignedToThisApplication)
    {
        if (isAdministrator) return true;
        if (isReviewerAssignedToThisApplication) return true;
        if (applicantUserId is not null &&
            Applicant is not null &&
            Applicant.UserId == applicantUserId)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Authorization: generate / regenerate access. Same as access, minus the applicant branch.
    /// </summary>
    public bool CanUserGenerateFundingAgreement(
        bool isAdministrator,
        bool isReviewerAssignedToThisApplication)
    {
        return isAdministrator || isReviewerAssignedToThisApplication;
    }

    // --- Digital Signatures (spec 006) ---

    /// <summary>
    /// Evaluates preconditions for regenerating the Funding Agreement. Composes
    /// the existing generation preconditions with a lockdown check against any
    /// signed upload already submitted.
    /// </summary>
    public bool CanRegenerateFundingAgreement(out IReadOnlyList<string> errors)
    {
        var failures = new List<string>();

        if (!CanGenerateFundingAgreement(out var baseErrors))
            failures.AddRange(baseErrors);

        if (_fundingAgreement is null)
            failures.Add("No Funding Agreement exists to regenerate.");
        else if (_fundingAgreement.IsLocked)
            failures.Add("Agreement is locked: a signed upload has been submitted.");

        errors = failures.Distinct(StringComparer.Ordinal).ToList().AsReadOnly();
        return errors.Count == 0;
    }

    /// <summary>
    /// Authorization: reviewer may approve/reject a signed upload. Admin OR the
    /// reviewer assigned to this application.
    /// </summary>
    public bool CanUserReviewSignedUpload(
        bool isAdministrator,
        bool isReviewerAssignedToThisApplication)
    {
        return isAdministrator || isReviewerAssignedToThisApplication;
    }

    /// <summary>
    /// Transitions the application from ResponseFinalized to AgreementExecuted.
    /// Called immediately after a reviewer-approved signed upload.
    /// </summary>
    public void ExecuteAgreement(string reviewerUserId)
    {
        if (string.IsNullOrWhiteSpace(reviewerUserId))
            throw new InvalidOperationException("Reviewer user id must be non-empty.");

        if (_fundingAgreement is null)
            throw new InvalidOperationException("Cannot execute agreement: no Funding Agreement exists.");

        if (State != ApplicationState.ResponseFinalized)
            throw new InvalidOperationException(
                $"Cannot execute agreement: application is in '{State}' state, expected 'ResponseFinalized'.");

        State = ApplicationState.AgreementExecuted;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Applicant facade: accept a new signed upload against the current agreement.
    /// </summary>
    public SignedUpload SubmitSignedUpload(
        string uploaderUserId,
        int generatedVersionAtUpload,
        string fileName,
        long size,
        string blobKey)
    {
        var agreement = _fundingAgreement
            ?? throw new InvalidOperationException("Cannot submit signed upload: no Funding Agreement exists.");

        if (State != ApplicationState.ResponseFinalized)
            throw new InvalidOperationException(
                $"Cannot submit signed upload: application is in '{State}' state, expected 'ResponseFinalized'.");

        var upload = agreement.AcceptSignedUpload(
            uploaderUserId, generatedVersionAtUpload, fileName, size, blobKey);
        UpdatedAt = DateTime.UtcNow;
        return upload;
    }

    /// <summary>
    /// Applicant facade: supersede a still-pending signed upload with a new one.
    /// </summary>
    public SignedUpload ReplaceSignedUpload(
        string uploaderUserId,
        int generatedVersionAtUpload,
        string fileName,
        long size,
        string blobKey)
    {
        var agreement = _fundingAgreement
            ?? throw new InvalidOperationException("Cannot replace signed upload: no Funding Agreement exists.");

        if (State != ApplicationState.ResponseFinalized)
            throw new InvalidOperationException(
                $"Cannot replace signed upload: application is in '{State}' state, expected 'ResponseFinalized'.");

        var upload = agreement.ReplacePendingUpload(
            uploaderUserId, generatedVersionAtUpload, fileName, size, blobKey);
        UpdatedAt = DateTime.UtcNow;
        return upload;
    }

    /// <summary>
    /// Applicant facade: withdraw the pending signed upload.
    /// </summary>
    public void WithdrawSignedUpload(string withdrawingUserId)
    {
        var agreement = _fundingAgreement
            ?? throw new InvalidOperationException("Cannot withdraw signed upload: no Funding Agreement exists.");

        if (State != ApplicationState.ResponseFinalized)
            throw new InvalidOperationException(
                $"Cannot withdraw signed upload: application is in '{State}' state, expected 'ResponseFinalized'.");

        agreement.WithdrawPendingUpload(withdrawingUserId);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Reviewer facade: approve the pending signed upload and execute the agreement.
    /// </summary>
    public SigningReviewDecision ApproveSignedUpload(string reviewerUserId, string? comment)
    {
        var agreement = _fundingAgreement
            ?? throw new InvalidOperationException("Cannot approve signed upload: no Funding Agreement exists.");

        var decision = agreement.ApprovePendingUpload(reviewerUserId, comment);
        ExecuteAgreement(reviewerUserId);
        return decision;
    }

    /// <summary>
    /// Reviewer facade: reject the pending signed upload with a required comment.
    /// Application state is unchanged.
    /// </summary>
    public SigningReviewDecision RejectSignedUpload(string reviewerUserId, string comment)
    {
        var agreement = _fundingAgreement
            ?? throw new InvalidOperationException("Cannot reject signed upload: no Funding Agreement exists.");

        var decision = agreement.RejectPendingUpload(reviewerUserId, comment);
        UpdatedAt = DateTime.UtcNow;
        return decision;
    }

    // --- Auditor workflow stage (spec 040) ---

    /// <summary>
    /// Spec 040 / D3 — the reviewer completes the reviewer checklist and hands the
    /// finalized application off to audit. Guard: <c>ResponseFinalized</c>, no open
    /// appeal, no agreement yet (the agreement is created later, during audit), and
    /// the reviewer checklist complete (completeness is evaluated by the Application
    /// service against live template items and passed in). Returns the
    /// <see cref="VersionHistory"/> entry it appends (the notification idempotency anchor).
    /// </summary>
    public VersionHistory SendToAudit(string reviewerUserId, bool reviewerChecklistComplete)
    {
        if (string.IsNullOrWhiteSpace(reviewerUserId))
            throw new InvalidOperationException("Reviewer user id must be non-empty.");
        if (State != ApplicationState.ResponseFinalized)
            throw new InvalidOperationException(
                $"No se puede enviar a auditoría: la solicitud está en estado '{State}'.");
        if (_appeals.Any(a => a.Status == AppealStatus.Open))
            throw new InvalidOperationException(
                "No se puede enviar a auditoría: hay una apelación abierta.");
        if (_fundingAgreement is not null)
            throw new InvalidOperationException(
                "No se puede enviar a auditoría: ya existe un convenio para esta solicitud.");
        if (!reviewerChecklistComplete)
            throw new InvalidOperationException(
                "No se puede enviar a auditoría: la lista de verificación del revisor está incompleta.");

        State = ApplicationState.PendingAudit;
        UpdatedAt = DateTime.UtcNow;
        var vh = new VersionHistory(reviewerUserId, "SentToAudit", "Enviado a auditoría");
        _versionHistory.Add(vh);
        return vh;
    }

    /// <summary>
    /// Spec 040 / D3 — the auditor finds the application non-compliant and returns it
    /// to the reviewer. Guard: <c>PendingAudit</c>. The per-item non-compliance reasons
    /// are recorded as checklist responses by the service; this transition only moves
    /// the state and anchors the reviewer notification.
    /// </summary>
    public VersionHistory ReturnFromAudit(string auditorUserId)
    {
        if (string.IsNullOrWhiteSpace(auditorUserId))
            throw new InvalidOperationException("Auditor user id must be non-empty.");
        if (State != ApplicationState.PendingAudit)
            throw new InvalidOperationException(
                $"No se puede devolver de auditoría: la solicitud está en estado '{State}'.");

        // Spec 040 / FR-010 — leaving the approved state invalidates any prior PDF-correctness
        // confirmation, so the next audit cycle requires a fresh confirm before release
        // (closes the stale-confirmation forward-leak across the PendingAudit⇄ReturnedFromAudit loop).
        _fundingAgreement?.ClearAuditorConfirmation();

        State = ApplicationState.ReturnedFromAudit;
        UpdatedAt = DateTime.UtcNow;
        var vh = new VersionHistory(auditorUserId, "ReturnedFromAudit", "Devuelto al revisor desde auditoría");
        _versionHistory.Add(vh);
        return vh;
    }

    /// <summary>
    /// Spec 040 / D3 — after rework, the reviewer re-completes the reviewer checklist
    /// and re-sends the application to audit (the loop). Guard: <c>ReturnedFromAudit</c>
    /// and the reviewer checklist complete.
    /// </summary>
    public VersionHistory ResendToAudit(string reviewerUserId, bool reviewerChecklistComplete)
    {
        if (string.IsNullOrWhiteSpace(reviewerUserId))
            throw new InvalidOperationException("Reviewer user id must be non-empty.");
        if (State != ApplicationState.ReturnedFromAudit)
            throw new InvalidOperationException(
                $"No se puede reenviar a auditoría: la solicitud está en estado '{State}'.");
        if (!reviewerChecklistComplete)
            throw new InvalidOperationException(
                "No se puede reenviar a auditoría: la lista de verificación del revisor está incompleta.");

        State = ApplicationState.PendingAudit;
        UpdatedAt = DateTime.UtcNow;
        var vh = new VersionHistory(reviewerUserId, "ResentToAudit", "Reenviado a auditoría");
        _versionHistory.Add(vh);
        return vh;
    }

    /// <summary>
    /// Spec 040 / D1 / D3 — the auditor releases the audited, confirmed agreement for
    /// signature, returning the application to <c>ResponseFinalized</c> so the existing
    /// signing ceremony runs unchanged. Guard: <c>PendingAudit</c>, an agreement exists,
    /// and the auditor has confirmed the PDF (<see cref="FundingAgreement.AuditorConfirmedAtUtc"/>).
    /// </summary>
    public VersionHistory ReleaseForSignature(string auditorUserId)
    {
        if (string.IsNullOrWhiteSpace(auditorUserId))
            throw new InvalidOperationException("Auditor user id must be non-empty.");
        if (State != ApplicationState.PendingAudit)
            throw new InvalidOperationException(
                $"No se puede liberar para firma: la solicitud está en estado '{State}'.");
        if (_fundingAgreement is null)
            throw new InvalidOperationException(
                "No se puede liberar para firma: no existe un convenio generado.");
        if (_fundingAgreement.AuditorConfirmedAtUtc is null)
            throw new InvalidOperationException(
                "No se puede liberar para firma: el auditor no ha confirmado el PDF.");

        State = ApplicationState.ResponseFinalized;
        UpdatedAt = DateTime.UtcNow;
        var vh = new VersionHistory(auditorUserId, "ReleasedForSignature", "Liberado para firma");
        _versionHistory.Add(vh);
        return vh;
    }

    /// <summary>
    /// Spec 040 / D11 — applicant facade: the auditor confirms the generated PDF is
    /// correct, unlocking <see cref="ReleaseForSignature"/>. Requires an existing
    /// agreement and the application to be in audit.
    /// </summary>
    public void ConfirmAgreementPdf(string auditorUserId)
    {
        if (_fundingAgreement is null)
            throw new InvalidOperationException(
                "No se puede confirmar el PDF: no existe un convenio generado.");
        if (State != ApplicationState.PendingAudit)
            throw new InvalidOperationException(
                $"No se puede confirmar el PDF: la solicitud está en estado '{State}'.");

        _fundingAgreement.ConfirmByAuditor(auditorUserId);
        UpdatedAt = DateTime.UtcNow;
    }
}
