namespace FundingPlatform.Application.Applications.Commands;

/// <summary>
/// Spec 023 — outcome envelope for <c>ApplicationService.EditQuotationAsync</c>.
/// Field-level errors are surfaced via <see cref="FieldErrors"/> (FR-005 / R0.5
/// aggregation); state / legacy / missing-rate failures use the top-level
/// <see cref="GlobalError"/> message that the controller surfaces in
/// <c>ModelOnly</c>.
/// </summary>
public sealed record EditQuotationResult(
    EditQuotationOutcome Outcome,
    IReadOnlyDictionary<string, string>? FieldErrors = null,
    string? GlobalError = null);

/// <summary>
/// Spec 023 — outcome discriminator. Maps to the HTTP status table in
/// <c>contracts/quotation-edit-endpoint.md</c>.
/// </summary>
public enum EditQuotationOutcome
{
    /// <summary>303 See Other → Application/Edit/{id}.</summary>
    Success,

    /// <summary>404 — quotation, item, or application missing.</summary>
    NotFound,

    /// <summary>403 — current applicant is not the application owner (FR-007).</summary>
    Forbidden,

    /// <summary>422 — application state is not <c>Draft</c> (FR-008).</summary>
    StateChanged,

    /// <summary>422 — quotation is flagged <c>LegacyNeedsReview</c> (FR-011).</summary>
    LegacyFlagged,

    /// <summary>400 — field validation failed; <c>FieldErrors</c> is populated (FR-005).</summary>
    ValidationFailed,

    /// <summary>422 — no published exchange rate for the requested currency (US3 edge).</summary>
    MissingRate,
}
