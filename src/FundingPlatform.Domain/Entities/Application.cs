using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Exceptions;
using FundingPlatform.Domain.ValueObjects;
using DomainImpact = FundingPlatform.Domain.ValueObjects.Impact;

namespace FundingPlatform.Domain.Entities;

public class Application
{
    private readonly List<Item> _items = [];
    private readonly List<VersionHistory> _versionHistory = [];
    private readonly List<ApplicantResponse> _applicantResponses = [];
    private readonly List<Appeal> _appeals = [];
    private readonly List<ImpactParameterValue> _impactParameterValues = [];
    private FundingAgreement? _fundingAgreement;

    public int Id { get; private set; }
    public int ApplicantId { get; private set; }
    /// <summary>
    /// Spec 018 / FR-015 / FR-016 — commercial entity name (`Empresa solicitante`)
    /// distinct from the applicant representative's legal name. Required (non-nullable),
    /// trimmed, ≤200 chars. Mutated via <see cref="SetCompanyName"/>.
    /// </summary>
    public string CompanyName { get; private set; } = string.Empty;

    /// <summary>
    /// Spec 021 / FR-008 — opaque, human-readable identifier surfaced on every
    /// applicant-facing surface (dashboard, reviewer queue, signing inbox,
    /// Funding Agreement PDF, notification emails). Immutable post-construction:
    /// once the Infrastructure generator stamps it via <see cref="AssignPublicCode"/>,
    /// the code stays for the life of the Application.
    /// </summary>
    public PublicCode? PublicCode { get; private set; }

    /// <summary>
    /// Spec 021 / FR-005 — the applicant's chosen ImpactTemplate (one per Application).
    /// Nullable while the Application is in Draft; the <see cref="Submit"/> guard chain
    /// requires it to be set before the state can advance to Submitted.
    /// </summary>
    public int? ImpactTemplateId { get; private set; }
    public ImpactTemplate? ImpactTemplate { get; private set; }

    /// <summary>
    /// Spec 021 / FR-005 — re-parented parameter-value rows (was <c>Items.ImpactId →
    /// Impacts → ImpactParameterValues</c>; now <c>Applications → ImpactParameterValues</c>).
    /// Populated by <see cref="SetImpact"/>.
    /// </summary>
    public IReadOnlyList<ImpactParameterValue> ImpactParameterValues => _impactParameterValues.AsReadOnly();

    /// <summary>
    /// Spec 021 / FR-005 — typed projection used by reads + the autosave path.
    /// Returns null until <see cref="SetImpact"/> has been called.
    /// </summary>
    public DomainImpact? Impact =>
        ImpactTemplate is null
            ? null
            : new DomainImpact(ImpactTemplate, ImpactParameterValues);

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

    public ApplicationState State { get; private set; } = ApplicationState.Draft;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? SubmittedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public Applicant Applicant { get; private set; } = null!;

    public IReadOnlyList<Item> Items => _items.AsReadOnly();
    public IReadOnlyList<VersionHistory> VersionHistory => _versionHistory.AsReadOnly();
    public IReadOnlyList<ApplicantResponse> ApplicantResponses => _applicantResponses.AsReadOnly();
    public IReadOnlyList<Appeal> Appeals => _appeals.AsReadOnly();
    public FundingAgreement? FundingAgreement => _fundingAgreement;

    private Application() { }

    public Application(int applicantId, string companyName)
    {
        ApplicantId = applicantId;
        State = ApplicationState.Draft;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        StageEnteredAt = DateTimeOffset.UtcNow;
        SetCompanyName(companyName);
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
    /// Spec 021 / FR-005 — replaces the applicant's Impact selection. Wipes any
    /// existing parameter values, attaches the new template + values, and bumps
    /// <see cref="UpdatedAt"/>. The Web/Application layer is responsible for
    /// validating <paramref name="template"/>.Id ∈ ProcessPlantilla.ImpactTemplateIds()
    /// before invoking (the snapshot lookup requires a repository so it stays out
    /// of Domain).
    /// </summary>
    public void SetImpact(ImpactTemplate template, IEnumerable<ImpactParameterValue> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        ImpactTemplate = template;
        ImpactTemplateId = template.Id;
        _impactParameterValues.Clear();
        _impactParameterValues.AddRange(values);
        UpdatedAt = DateTime.UtcNow;
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
    /// Spec 018 / FR-015 / FR-016 — sets the commercial entity name. Trims whitespace,
    /// rejects null/empty/whitespace-only input, and enforces a 200-character maximum
    /// after trim. Persists the trimmed value and bumps <see cref="UpdatedAt"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="companyName"/> is null/whitespace or exceeds 200 chars after trim.
    /// </exception>
    public void SetCompanyName(string companyName)
    {
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

    /// <summary>
    /// Removes an item from the application by its identifier.
    /// </summary>
    public void RemoveItem(int itemId)
    {
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
    /// Spec 021 / FR-006 / FR-017 — full Submit guard chain. Composes the legacy
    /// item/impact validators with the new stage-window check.
    ///
    /// Guards (data-model.md state transitions, FR-017):
    /// 1. <c>HasAtLeastOneItem</c>
    /// 2. <c>EachItemHasMinimumQuotations</c> (uses snapshot's
    ///    <c>MinimumQuotationsPerItem</c>)
    /// 3. <c>HasImpact</c> — <see cref="ImpactTemplateId"/> populated
    /// 4. <c>StageWindowOpen</c> — current instant &lt; <paramref name="stageClosesAt"/>;
    ///    otherwise throws <see cref="StageWindowClosedException"/> which the Web
    ///    layer maps to HTTP 422 (R-13).
    ///
    /// Required-field validation is enforced by the Application layer (it depends
    /// on <c>ProcessPlantilla.RequiredFieldFlags</c> + a field-key map living
    /// outside Domain). The legacy <c>HasCompleteImpact</c> per-Item check is
    /// dropped in favour of the per-Application Impact (R-6).
    /// </summary>
    public void Submit(
        int minQuotations,
        StageKind currentStage,
        DateTimeOffset stageClosesAt,
        DateTimeOffset now)
    {
        // Stage-window guard fires FIRST so an expired window short-circuits
        // before we enumerate the per-item validation list.
        if (now >= stageClosesAt)
        {
            throw new StageWindowClosedException(currentStage, stageClosesAt);
        }

        // Enumerate every submit-blocker in one pass so the applicant sees all
        // problems at once (FR-017 impact gate + per-item quotation counts).
        // The impact failure leads the list but does not short-circuit the
        // item/quotation enumeration.
        var errors = Validate(minQuotations);
        if (ImpactTemplateId is null)
        {
            errors.Insert(0, "Impact must be set before submission (FR-017).");
        }
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
    /// Validates the application and returns a list of validation error messages.
    /// Spec 021 / FR-005 — Impact is no longer per-Item; the per-Application
    /// <see cref="Impact"/> is enforced by the <c>Submit(int, StageKind, …)</c>
    /// overload. This method now only enumerates per-Item quotation-count failures.
    /// </summary>
    public List<string> Validate(int minQuotations)
    {
        var errors = new List<string>();

        if (_items.Count == 0)
        {
            errors.Add("Application must have at least one item.");
        }

        foreach (var item in _items)
        {
            if (!item.HasMinimumQuotations(minQuotations))
            {
                errors.Add(
                    $"Item '{item.ProductName}' must have at least {minQuotations} quotation(s).");
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

        if (State != ApplicationState.ResponseFinalized)
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
}
