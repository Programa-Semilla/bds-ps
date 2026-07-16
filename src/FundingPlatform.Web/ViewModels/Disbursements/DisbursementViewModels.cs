using FundingPlatform.Application.Disbursements;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Web.ViewModels.Disbursements;

/// <summary>Spec 045 — the per-application disbursement surface: five-dimension balance,
/// the record form, and the disbursement list. <see cref="CanWrite"/> is false for the
/// Auditor read-only view (controls hidden — FR-025).</summary>
public sealed class DisbursementIndexViewModel
{
    public required int ApplicationId { get; init; }
    public required ParticipantBalance Balance { get; init; }
    public required IReadOnlyList<DisbursementListItem> Items { get; init; }
    public required bool CanWrite { get; init; }
}

/// <summary>Spec 045 — the disbursement detail: amounts, typed evidence, the live
/// discrepancy list, and the lifecycle actions. Write controls gated by <see cref="CanWrite"/>.</summary>
public sealed class DisbursementDetailViewModel
{
    public required int ApplicationId { get; init; }
    public required DisbursementDetail Detail { get; init; }
    public required bool CanWrite { get; init; }
    public required string AcceptExtensions { get; init; }
}

/// <summary>Spec 045 — the group-scoped disbursement inbox (executed applications in
/// active processes). Reuses the spec-041 evidence-inbox projection.</summary>
public sealed class DisbursementInboxViewModel
{
    public required IReadOnlyList<DisbursementInboxRowViewModel> Rows { get; init; }
}

public sealed class DisbursementInboxRowViewModel
{
    public required int ApplicationId { get; init; }
    public required string ApplicationNumber { get; init; }
    public required string ApplicantName { get; init; }
    public required string FundName { get; init; }
    public required string ProcessName { get; init; }
    public required DateTimeOffset ExecutedAtUtc { get; init; }
}
