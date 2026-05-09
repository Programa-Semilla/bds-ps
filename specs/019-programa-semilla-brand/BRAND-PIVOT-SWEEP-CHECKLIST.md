# Brand Pivot Sweep Checklist — Spec 019 Programa Semilla

Per FR-028 / SC-008. One row per swept surface. Each cell is checked once the partial sweep lands.

Verification axes:

- **VT** = Visual tokens (palette + typography + surfaces match the new tokens.css)
- **CV** = Component vocabulary (button / card / table / badge / input / alert / modal partials retuned)
- **VG** = Voice-guide compliance (copy passes BRAND-VOICE.md tone + person + stage-aware patterns; "Capital Semilla" / "Forge" absent)
- **SC** = Sponsor chrome (sidebar header brand mark + wordmark; footer sponsor strip)
- **MO** = Motion (spec 011 motion catalog respected; reduced-motion contract honored)
- **A11Y** = Accessibility (WCAG AA contrast on retuned palette; focus rings; 44 px touch targets)

| Surface | VT | CV | VG | SC | MO | A11Y |
|---|---|---|---|---|---|---|
| **Auth (4)** | | | | | | |
| Login (`Views/Account/Login.cshtml`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Register (`Views/Account/Register.cshtml`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Reset Password (`Views/Account/ChangePassword.cshtml` or scaffold) | [x] | [x] | [x] | [x] | [x] | [x] |
| Confirm Email (Identity scaffold or custom view) | [x] | [x] | [x] | [x] | [x] | [x] |
| **Applicant (5)** | | | | | | |
| Applicant home (`Views/Application/Index.cshtml`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Applicant dashboard (`Views/Application/Details.cshtml`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Applicant journey (timeline view) | [x] | [x] | [x] | [x] | [x] | [x] |
| Applicant appeal (appeal-flow view) | [x] | [x] | [x] | [x] | [x] | [x] |
| Applicant signing (signing ceremony entry view) | [x] | [x] | [x] | [x] | [x] | [x] |
| **Reviewer (4)** | | | | | | |
| Reviewer queue (`Views/Review/Index.cshtml` + `QueueDashboard.cshtml`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Reviewer detail (`Views/Review/Review.cshtml`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Reviewer signing inbox (`Views/Review/SigningInbox.cshtml`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Reviewer history (history view if present) | [x] | [x] | [x] | [x] | [x] | [x] |
| **Admin (10)** | | | | | | |
| Admin index (`Views/Admin/Index.cshtml`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Admin Users (`Views/Admin/Users/`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Admin Groups (`Views/Admin/Groups/`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Admin Suppliers (`Views/Admin/Suppliers/`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Admin Currencies (`Views/Admin/Currencies/`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Admin Exchange Rates (`Views/Admin/ExchangeRates/`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Admin Legacy Quotations (`Views/Admin/LegacyQuotations/`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Admin Reports (`Views/Admin/Reports/`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Admin Configuration (`Views/Admin/Configuration.cshtml`) | [x] | [x] | [x] | [x] | [x] | [x] |
| Admin Impact Templates (`Views/Admin/ImpactTemplates.cshtml` + Create/Edit) | [x] | [x] | [x] | [x] | [x] | [x] |
| **Shared chrome (1)** | | | | | | |
| `_Layout.cshtml` (sidebar header + sponsor strip + footer) | [x] | [x] | [x] | [x] | [x] | [x] |

## Pending designer pass (placeholder assets — to be replaced before SC-015 sign-off)

These assets ship as **clearly-marked placeholder SVGs** (geometric stand-ins on the correct teal stroke) so the structural sweep, audit gates, and asset budget can run end-to-end. Real vector art lands at SC-015 sign-off when the designer delivers the canonical sources.

- `wwwroot/lib/brand/mark.svg` — placeholder seedling silhouette (re-trace from PDF `header-seedling.png`).
- `wwwroot/lib/brand/wordmark.svg` — placeholder "Programa Semilla" wordmark (currently text-set; designer may want a custom letterform pass).
- `wwwroot/lib/brand/seal.svg` — placeholder teal seal.
- `wwwroot/favicon.ico` and `wwwroot/lib/brand/favicons/*` — placeholder favicons (square seedling silhouette at the matching pixel size).
- `wwwroot/lib/brand/sponsors/sbd.svg` — Banca para el Desarrollo SBD placeholder.
- `wwwroot/lib/brand/sponsors/crocus.svg` — CROCUS placeholder.
- `wwwroot/lib/brand/sponsors/nexo.svg` — nexo placeholder.
- `wwwroot/lib/brand/sponsors/programa-semilla.svg` — Programa Semilla wordmark variant.
- `wwwroot/lib/brand/sponsors/10-anos.svg` — 10 años badge placeholder.
- 9 empty-state illustrations under `wwwroot/lib/illustrations/*.svg` — re-stroked from forest-green to teal `var(--color-primary)`; geometry preserved from spec 011 originals (designer may revisit composition at SC-015).

Each placeholder file is committed with a top-of-file `<!-- PLACEHOLDER: pending designer pass -->` comment so future contributors and reviewers find them via grep.

## Email subsystem (deferred)

- The project at this spec's iteration does not register an `IEmailSender` and does not ship email templates. The brand-grep gate (T030) is the standing guard for future email-template contributions; a full sender-name + signature audit re-runs when an email subsystem ships in a later spec.
