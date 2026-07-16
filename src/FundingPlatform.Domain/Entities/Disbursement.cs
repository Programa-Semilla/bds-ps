using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 045 — a recorded money movement against one executed funding agreement
/// (one participant). A standalone aggregate keyed by <c>ApplicationId</c> (research R2),
/// like <see cref="FundsUsageEvidence"/> — no navigation collection on the large
/// <see cref="Application"/> aggregate. The executed-agreement gate, the amount-&gt;0
/// invariant, the state machine, and the lock-after-validated boundary live here
/// (Constitution II — Rich Domain Model).
///
/// State machine (FR-026): <c>Recorded ⇄ Inconsistent</c> (reconciliation flips) →
/// <c>Validated</c> (terminal, via <see cref="Validate"/>); <c>{Recorded,Inconsistent}</c>
/// → <c>Cancelled</c> (terminal). No transition out of Validated/Cancelled.
/// </summary>
public sealed class Disbursement
{
    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
    public DateOnly PaymentDate { get; private set; }
    public decimal Amount { get; private set; }
    public string BankTransactionReference { get; private set; } = string.Empty;
    public string? BankAccountReference { get; private set; }
    public DisbursementState State { get; private set; }
    public string CreatedByUserId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public string? ValidatedByUserId { get; private set; }
    public DateTimeOffset? ValidatedAtUtc { get; private set; }
    public string? CancelledByUserId { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private Disbursement() { } // EF

    /// <summary>
    /// FR-001/FR-003 — recording is only valid for an application in
    /// <see cref="Enums.ApplicationState.AgreementExecuted"/> with a positive amount.
    /// The application is passed in (a cheap tracked scalar load) so the gate stays in
    /// the domain (mirrors <see cref="FundsUsageEvidence.CreateForExecutedApplication"/>).
    /// </summary>
    public static Disbursement Record(
        Application application,
        string operatorUserId,
        DateOnly paymentDate,
        decimal amount,
        string bankTransactionReference,
        string? bankAccountReference)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application.State != ApplicationState.AgreementExecuted)
        {
            throw new InvalidOperationException(
                $"A disbursement can only be recorded against an application in {nameof(ApplicationState.AgreementExecuted)}; "
                + $"application {application.Id} is {application.State}.");
        }
        if (string.IsNullOrWhiteSpace(operatorUserId))
        {
            throw new ArgumentException("OperatorUserId is required.", nameof(operatorUserId));
        }
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Disbursement amount must be greater than zero.");
        }
        if (string.IsNullOrWhiteSpace(bankTransactionReference))
        {
            throw new ArgumentException("BankTransactionReference is required.", nameof(bankTransactionReference));
        }

        return new Disbursement
        {
            ApplicationId = application.Id,
            PaymentDate = paymentDate,
            Amount = amount,
            BankTransactionReference = bankTransactionReference.Trim(),
            BankAccountReference = Normalize(bankAccountReference),
            State = DisbursementState.Recorded,
            CreatedByUserId = operatorUserId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>FR-028 — edit pre-validation details. Guarded <c>State ∈ {Recorded,Inconsistent}</c>
    /// (locked once Validated/Cancelled). Reconciliation is re-run by the service afterwards (FR-016).</summary>
    public void EditDetails(DateOnly paymentDate, decimal amount, string bankTransactionReference, string? bankAccountReference)
    {
        EnsureMutable();
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Disbursement amount must be greater than zero.");
        }
        if (string.IsNullOrWhiteSpace(bankTransactionReference))
        {
            throw new ArgumentException("BankTransactionReference is required.", nameof(bankTransactionReference));
        }

        PaymentDate = paymentDate;
        Amount = amount;
        BankTransactionReference = bankTransactionReference.Trim();
        BankAccountReference = Normalize(bankAccountReference);
    }

    /// <summary>FR-015/FR-016 — recompute the derived state from the current discrepancy list.
    /// No-op on terminal states (Validated/Cancelled).</summary>
    public void ApplyReconciliation(IReadOnlyList<ReconciliationDiscrepancy> discrepancies)
    {
        ArgumentNullException.ThrowIfNull(discrepancies);
        if (State is DisbursementState.Validated or DisbursementState.Cancelled)
        {
            return;
        }
        State = discrepancies.Count > 0 ? DisbursementState.Inconsistent : DisbursementState.Recorded;
    }

    /// <summary>FR-009/FR-026 — a disbursement is validatable only when it is pre-validation,
    /// both evidence documents are present, AND there are zero blocking discrepancies.</summary>
    public bool IsValidatable(bool bothEvidencePresent, bool zeroDiscrepancies)
        => State is not (DisbursementState.Validated or DisbursementState.Cancelled)
           && bothEvidencePresent
           && zeroDiscrepancies;

    /// <summary>FR-026/FR-027 — the explicit Validar action. Guarded on
    /// <see cref="IsValidatable"/>; the service supplies evidence-presence + discrepancy
    /// flags (they span other aggregates). Flips State=Validated and stamps the actor;
    /// the immutable ledger entry is posted by the service in the same SaveChanges.</summary>
    public void Validate(string operatorUserId, bool bothEvidencePresent, bool zeroDiscrepancies)
    {
        if (string.IsNullOrWhiteSpace(operatorUserId))
        {
            throw new ArgumentException("OperatorUserId is required.", nameof(operatorUserId));
        }
        if (!IsValidatable(bothEvidencePresent, zeroDiscrepancies))
        {
            throw new InvalidOperationException(
                $"Disbursement {Id} is not validatable (state={State}, bothEvidencePresent={bothEvidencePresent}, zeroDiscrepancies={zeroDiscrepancies}).");
        }
        State = DisbursementState.Validated;
        ValidatedByUserId = operatorUserId;
        ValidatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>FR-028 — cancel a pre-validation disbursement. Guarded
    /// <c>State ∈ {Recorded,Inconsistent}</c>. It leaves no ledger entry (it never posted).</summary>
    public void Cancel(string operatorUserId)
    {
        if (string.IsNullOrWhiteSpace(operatorUserId))
        {
            throw new ArgumentException("OperatorUserId is required.", nameof(operatorUserId));
        }
        EnsureMutable();
        State = DisbursementState.Cancelled;
        CancelledByUserId = operatorUserId;
        CancelledAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>True while the disbursement is pre-validation (Recorded/Inconsistent) — the
    /// window in which edit, evidence replace, and cancel are allowed (FR-028).</summary>
    public bool IsPreValidation => State is DisbursementState.Recorded or DisbursementState.Inconsistent;

    private void EnsureMutable()
    {
        if (!IsPreValidation)
        {
            throw new InvalidOperationException(
                $"Disbursement {Id} is {State} and can no longer be edited, replaced, or cancelled (FR-028).");
        }
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
