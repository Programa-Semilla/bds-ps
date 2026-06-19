namespace FundingPlatform.Application.Notifications.Email;

/// <summary>
/// Spec 041 / Decision 2 — relative paths to the official brand assets already in
/// <c>wwwroot/lib/brand/</c> (served anonymously). Every email composes an absolute
/// URL by combining these with <c>Notifications:BaseUrl</c>. Single source of truth
/// so both render paths reference the same files; no new static path is introduced.
/// </summary>
public static class BrandAssets
{
    /// <summary>Header logo — Programa Semilla horizontal wordmark.</summary>
    public const string LogoPath = "/lib/brand/programa-semilla-horizontal.png";
    /// <summary>Footer strip — the 5-partner logo set (verified to match the seed).</summary>
    public const string PartnerStripPath = "/lib/brand/partners-footer.png";
}

/// <summary>
/// Spec 041 / FR-001 / Decision 1 — the contract the shared <c>_EmailLayout</c>
/// requires from <em>any</em> email model, whether it is the outbox
/// <c>EmailRenderModel</c> (Web) or a direct-send <see cref="DirectEmailModel"/>.
/// Both render paths flow through the one branded shell, so the layout depends on
/// this interface rather than a concrete model — the mechanism that makes
/// "one design system" real.
/// </summary>
public interface IBrandedEmailModel
{
    /// <summary>Subject line — drives the <c>&lt;title&gt;</c> / preheader.</summary>
    string Subject { get; }
    /// <summary>Absolute URL of the header logo (composed from <c>Notifications:BaseUrl</c>).</summary>
    string LogoUrl { get; }
    /// <summary>Absolute URL of the 5-partner footer strip.</summary>
    string PartnerStripUrl { get; }
}

/// <summary>
/// Spec 041 — one key/value row inside a "Detalle" <c>_StatusCard</c>
/// (reviewer/auditor detail; FR-001). Shared by outbox and direct-send views.
/// </summary>
public sealed record DetailRow(string Label, string Value);

/// <summary>
/// Spec 041 — input for the bulletproof <c>_CtaButton</c> partial. The button is
/// rendered ONLY when <see cref="Url"/> is non-empty (FR-005); no URL ⇒ neither
/// button nor fallback link is emitted and no placeholder URL is ever invented.
/// </summary>
public sealed record CtaButtonModel(string? Url, string Label);

/// <summary>
/// Spec 041 — input for the "Detalle" <c>_StatusCard</c> partial.
/// </summary>
public sealed record StatusCardModel(string Heading, IReadOnlyList<DetailRow> Rows);

/// <summary>
/// Spec 041 / Decision 1 / Decision 4 — model for the direct-send + notifier
/// emails (identity, stage, supplier/company) now rendered through the shared
/// branded <c>_EmailLayout</c> via <see cref="IEmailViewRenderer"/> instead of
/// plain-text token substitution. Each factory/notifier populates this with the
/// per-email copy (voseo) and the preserved dynamic variables; each named view
/// supplies its own hero title and composes the shared partials.
/// </summary>
/// <param name="Subject">Subject line (owned by the factory).</param>
/// <param name="DisplayName">Greeting target ("Hola, {DisplayName}:").</param>
/// <param name="Paragraphs">Body copy paragraphs, in order (already HTML-safe text).</param>
/// <param name="CtaUrl">Optional CTA link; when null/empty the view omits the button + fallback (FR-005).</param>
/// <param name="CtaLabel">CTA button label (used only when <paramref name="CtaUrl"/> is present).</param>
/// <param name="CardHeading">Optional "Detalle" card heading (reviewer/auditor detail).</param>
/// <param name="CardRows">Optional "Detalle" card rows.</param>
/// <param name="FooterNote">Optional small-print note under the body (e.g. link expiry, security advice).</param>
/// <param name="LogoUrl">Header logo absolute URL.</param>
/// <param name="PartnerStripUrl">Partner-strip absolute URL.</param>
public sealed record DirectEmailModel(
    string Subject,
    string DisplayName,
    IReadOnlyList<string> Paragraphs,
    string? CtaUrl,
    string? CtaLabel,
    string? CardHeading,
    IReadOnlyList<DetailRow>? CardRows,
    string? FooterNote,
    string LogoUrl,
    string PartnerStripUrl) : IBrandedEmailModel;
