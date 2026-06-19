# Contract: Email Design System (shell + partials)

**Feature**: 041-email-brand-lift

The single source of truth for brand chrome. Every in-scope email (outbox + identity + stage + supplier) composes these. All CSS inline; table-based; ≤600px; no flexbox/grid; no external CSS (NFR-001).

## Shell: `_EmailLayout.cshtml`

Structure (top→bottom), each a table row inside a centered 600px container on a light-neutral page background:
1. `_BrandHeader` — Programa Semilla horizontal logo (hosted absolute URL, alt "Programa Semilla").
2. `@RenderBody()` — the per-email body (composes partials below).
3. Sign-off block — "Saludos cordiales," / **"Equipo Programa Semilla"** (replaces the old "Sistema de Banca para el Desarrollo" signature; ALIA is referenced in body copy, not the signature).
4. `_PartnerFooter` — partner-logo strip + support line (email **and** `+506 4600-1234`) + "Este es un mensaje automático — no respondás a este correo." (FR-006).

Inputs available to the layout: `Model.Subject` (→ `<title>` / preheader), `Model.LogoUrl`, `Model.PartnerStripUrl`. Brand palette tokens (inline): primary teal `#008a9e`, secondary `#42afa8`, orange `#f9a61c`, yellow `#ffc729`, light neutral bg.

## Partials (in `Views/Emails/Shared/`)

| Partial | Purpose | Key inputs | Rules |
|---|---|---|---|
| `_BrandHeader` | logo header | `LogoUrl` | `<img>` with Spanish alt; legible if blocked (NFR-004). |
| `_PartnerFooter` | partner strip + support + legal note | `PartnerStripUrl` | strip on **every** email (FR-006); alt text lists partners. |
| `_Hero` | title block | title text, optional subtitle | brand-teal `<h1>` (semantic heading, NFR-003). |
| `_CtaButton` | call to action | `Url`, `Label` | **render only when `Url` is non-empty** (FR-005); bulletproof (VML for Outlook); ALWAYS followed by a plain-text fallback link. |
| `_StatusCard` | the "Detalle" card | heading + rows | bordered/tinted card; wraps long values, no overflow (edge case). |
| `_DetailList` | key/value list | ordered `(label, value)` pairs | used inside `_StatusCard` for reviewer/auditor detail. |

## Invariants (testable)

- **CTA rule (FR-005)**: no `Url` ⇒ neither button nor fallback link is emitted; no literal/placeholder URL is ever invented.
- **Images (FR-002/NFR-004)**: every `<img>` has non-empty Spanish `alt`; no text content lives only inside an image.
- **Palette (FR-003)**: CTA background is brand teal `#008a9e`; near-black `#1d1d1f` button is removed.
- **Footer (FR-006)**: partner strip + support email + `+506 4600-1234` + automatic-message note present on every rendered email.
- **No external assets**: all `<img src>` are absolute URLs under `Notifications:BaseUrl`/`lib/brand`; no `<link>`/external `<style>`.
- **Width/layout (NFR-001/002)**: outermost content table ≤600px; single column; no flex/grid.
- **es-CR (NFR-007)**: no English copy in any rendered email.
