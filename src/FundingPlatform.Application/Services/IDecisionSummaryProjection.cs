using FundingPlatform.Application.DTOs;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 027 / US4 — projects an already-loaded <see cref="Domain.Entities.Application"/>
/// aggregate (Items→Category, Items→Quotations→Supplier, ApplicantResponses→
/// ItemResponses) into the shared per-line decision summary. Pure in-memory
/// mapping: no repository access, no new query.
/// </summary>
public interface IDecisionSummaryProjection
{
    IReadOnlyList<DecisionSummaryLineDto> Project(Domain.Entities.Application application);
}
