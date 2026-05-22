// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Impact VO on Application)
// and research.md R-6.

using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Domain.ValueObjects;

/// <summary>
/// Spec 021 / FR-005 — lightweight projection carried by an
/// <see cref="Application"/> after the applicant picks an <see cref="ImpactTemplate"/>
/// and fills in its parameter values. Per R-6, the underlying schema re-parents
/// the existing <c>ImpactParameterValues</c> rows from the dropped
/// <c>Items.ImpactId</c> path onto <c>ApplicationId</c>; this record is the
/// in-memory pairing used by <c>Application.SetImpact</c> and the autosave path.
/// </summary>
public sealed record Impact(
    ImpactTemplate Template,
    IReadOnlyList<ImpactParameterValue> Values);
