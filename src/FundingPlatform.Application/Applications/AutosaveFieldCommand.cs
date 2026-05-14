// Spec 021 — see specs/021-feedback-session-may13/research.md R-5
// and contracts/applicant-routes.md (POST /api/applications/{publicCode}/autosave).

namespace FundingPlatform.Application.Applications;

/// <summary>
/// Spec 021 / T090 / R-5 / FR-016 — per-field autosave command. The Web layer
/// translates the HTTP POST body <c>{ fieldKey, value, etag }</c> into this
/// record and dispatches it through <see cref="IAutosaveFieldHandler"/>.
///
/// <para>The ETag is the Application's <c>RowVersion</c> rendered as a
/// base64 string (the existing optimistic-concurrency convention).</para>
/// </summary>
public sealed record AutosaveFieldCommand(
    string PublicCode,
    string FieldKey,
    string? Value,
    string? Etag);

/// <summary>
/// Spec 021 / R-5 — autosave response payload returned on a successful 200.
/// </summary>
public sealed record AutosaveFieldResult(string Etag, DateTimeOffset SavedAt);

/// <summary>
/// Spec 021 / R-5 — thrown when the supplied ETag does not match the current
/// row's <c>RowVersion</c>. The Web layer translates to HTTP 409.
/// </summary>
public sealed class AutosaveConflictException : Exception
{
    public AutosaveConflictException()
        : base("ETag mismatch: the application has changed since you opened the editor.") { }
}

/// <summary>
/// Spec 021 / R-5 / FR-016 — autosave handler seam. The Infrastructure layer
/// supplies the EF-backed implementation; the Web layer takes only this
/// interface so the controller stays decoupled from <c>AppDbContext</c>.
/// </summary>
public interface IAutosaveFieldHandler
{
    Task<AutosaveFieldResult> HandleAsync(
        AutosaveFieldCommand cmd, int currentApplicantId, CancellationToken ct = default);
}
