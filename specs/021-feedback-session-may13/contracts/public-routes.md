# Public + Identity Routes — Contracts

**Feature**: 021-feedback-session-may13 | **Surface**: ASP.NET MVC anonymous

## Public landing

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/` | `HomeController.Index` | Anonymous | FR-031 scaffold: hero CTA *"¿Listo para acelerar tu negocio?"* with button *"Iniciar acompañamiento"* (FR-029); three slot regions (Reglamento, Ejemplo de cotización, Sponsor strip — reuses spec 019 brand kit). Slots without uploaded files render *"Próximamente"*. If authenticated, redirect to role-appropriate dashboard. |
| GET | `/files/public-landing/{slot}` | `PublicLandingFilesController.Download` | Anonymous | `slot ∈ { reglamento, ejemplo }`. Streams via `IObjectStorage` SAS / pass-through depending on configured serving mode. 404 if slot empty. |

## Identity — forgot/reset password

| Verb | Route | Action | Auth | Notes |
|------|-------|--------|------|-------|
| GET | `/Account/ForgotPassword` | `ForgotPassword` | Anonymous | Form: Email + submit. Eye toggle not applicable. |
| POST | `/Account/ForgotPassword` | `ForgotPassword` | Anonymous | Always returns 200 with neutral confirmation (no enumeration per FR-028). |
| GET | `/Account/ResetPassword?token={t}` | `ResetPassword` | Anonymous | Form: NewPassword + Confirm; strength legend (FR-027); eye toggle (FR-026). Invalid/expired token renders *"Enlace inválido o expirado. Solicite uno nuevo."* |
| POST | `/Account/ResetPassword` | `ResetPassword` | Anonymous | Validates token via `IPasswordResetTokenStore.Consume`. 200 on success → redirect `/Account/Login` with success toast; 422 with structured errors on validation failure. |
| GET | `/Account/Login` | (existing) | Anonymous | Adds *"¿Olvidó su contraseña?"* link. Eye toggle on password field. |

## Notification email contracts (informational)

| Trigger | Template | Subject |
|---------|----------|---------|
| Forgot-password request (known email) | `Identity/ForgotPasswordEmail.cshtml` | *Restablezca su contraseña* |
| Forgot-password request (unknown email) | (no email sent — but timing matches known path) | — |
| Stage-expiry T-72h | `Stages/T72ReminderEmail.cshtml` | *Su solicitud {{PublicCode}} cierra en 72 horas* |
| Stage-expiry T-24h | `Stages/T24ReminderEmail.cshtml` | *Su solicitud {{PublicCode}} cierra en 24 horas* |
| Stage-expiry hit | `Stages/ExpiredEmail.cshtml` | *La etapa de {{PublicCode}} cerró el {{fecha}}* |

All emails route through existing SMTP wiring (NFR-005 — no new provider).

## Forbidden-strings invariant (enforced by `ForbiddenStringsCrawler` POM)

Across every applicant-facing surface (anonymous landing, login, profile, dashboard, application draft, /review, reviewer queue, signing inbox, all email previews):

- Zero matches for `/financiamiento/i` (FR-029, SC-012)
- Zero matches for `/Bienvenido\/?a/i` (FR-030, SC-015)
- Zero matches for `/Solicitud N\.º \d+/` (FR-008, SC-005)

Funding Agreement PDF retains *"financiamiento"* (legal term carve-out) — not crawled by this assertion.
