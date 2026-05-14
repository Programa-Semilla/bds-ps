# Quickstart — Feature 021 (Feedback Session May-13)

**Branch**: `021-feedback-session-may13` | **Stack**: .NET 10 / Aspire / SQL Server / Playwright

---

## Run the feature locally

```bash
# From repo root
dotnet build FundingPlatform.slnx
dotnet run --project src/FundingPlatform.AppHost
```

Aspire dashboard opens; SQL container starts; dacpac auto-deploys; new tables (`Processes`, `Plantillas`, `ProcessPlantillas`, `Provinces`, `Cantons`, `PasswordResetTokens`) and post-deployment seeds (7 provinces, ~82 cantones, *"Migración inicial"* Process, `SupplierAdmin` AspNetRole) apply on first start.

## Smoke test (manual)

### As anonymous

1. Open `http://localhost:{AppHostPort}/`.
2. Confirm hero CTA *"¿Listo para acelerar tu negocio?"* + button *"Iniciar acompañamiento"*.
3. Confirm three slot regions; Reglamento + Ejemplo render *"Próximamente"* placeholders (no files uploaded yet).
4. Click *"¿Olvidó su contraseña?"* on login page; submit any email; observe generic confirmation page (no enumeration).

### As Admin (`admin@FundingPlatform.com`)

1. Sign in. Greeting reads *"Hola, Admin"*.
2. `/Admin` shows the 4 action KPIs plus *Personas activas* + *Fondos entregados*. No *Cotizaciones pendientes* tile.
3. `/Admin/Processes` — empty (only *"Migración inicial"* seeded). Create *Crocus 2025*.
4. `/Admin/Plantillas` — create *PlantillaMVP-v1*, attach ≥ 1 ImpactTemplate.
5. Back on Process, assign *PlantillaMVP-v1* → snapshot row created.
6. `/Admin/Users/{anyReviewer}/Edit` — Group filter cascades Process → Group; assign reviewer to *Crocus 2025 / Norte*.
7. `/Admin/Suppliers` — default sort is `LastUsedAt DESC`; Process filter visible; autocomplete on Name + CédulaJurídica.

### As SupplierAdmin (provision a user with role `SupplierAdmin` only)

1. Sidebar shows only *Empresas proveedoras* + profile.
2. CRUD a Supplier + SupplierBranch (Province → Cantón cascade renders).
3. Direct-URL `/Admin/Users` → 403 (Tabler styled); `/Admin/AuditEvents` shows a `SupplierAdminDeniedAccess` row.

### As Applicant

1. Sign in; greeting *"Hola, Vivi"*.
2. New Application → Impact step first (FR-005).
3. Add items inline; supplier search autocompletes; on no-match, "Register new branch" flow opens inline with Province → Cantón cascade + ContactPersonName.
4. Fill required fields; observe *"✓ Guardado HH:MM"* after each blur.
5. Submit button is disabled until ≥ 1 Item, Impact set, every required field filled — tooltip enumerates failures by name.
6. Click submit → `/review` page renders Items / Suppliers / Totals (CRC with FX disclaimer) / Impact + *"Confirmar y enviar"*.
7. After confirm → success banner shows PublicCode (e.g. *A7K2-9XF*).
8. Soft-delete the Application via admin → return to dashboard: counter decrements; no *"borrador listo"* row remains (FR-021).

### Stage expiry

1. As Admin: `/Admin/Processes/{Crocus 2025}/StageOverride` set *facturación* = 1 day.
2. Adjust an Application's `StageEnteredAt` directly in DB to >24h ago.
3. Within the next hour, observe T-72h (if seeded older) + T-24h reminder emails captured in the Aspire-dashboard log; banner switches to *"Vencido..."* once `StageEnteredAt + window < now`.
4. Attempt POST on the expired Application → 422 with *"La etapa cerró el {{fecha}}. Contacte al administrador."*

### Forgot password

1. Sign out. `/Account/ForgotPassword` → submit known email.
2. Aspire log shows token email dispatched.
3. Open the reset link; strength legend ticks live; submit.
4. Re-open the same link → *"Enlace inválido o expirado. Solicite uno nuevo."*

---

## Run tests

```bash
dotnet test tests/FundingPlatform.Tests.Unit
dotnet test tests/FundingPlatform.Tests.Integration
dotnet test tests/FundingPlatform.Tests.E2E
```

The E2E suite is the delivery gate (NFR-004). All 8 user-story tests must be green.

---

## Configuration knobs introduced

| Key | Default | Notes |
|-----|---------|-------|
| `Stage.Solicitud.WindowDays` | `14` | Platform default (FR-006). Override per-Process via admin UI. |
| `Stage.Revision.WindowDays` | `10` | |
| `Stage.Facturacion.WindowDays` | `30` | |
| `StageExpiry.Reminders.Cadence` | `T-72h, T-24h, Expiry` | Fixed; not admin-configurable in 021 (OQ-6). |
| `Storage:Categories:public-landing-files:MaxSizeBytes` | `10485760` (10 MiB) | Slot uploads via existing `IObjectStorage`. |
| `Storage:Categories:public-landing-files:UrlExpirySeconds` | `300` | |
| `Identity:PasswordReset:TokenLifespanMinutes` | `60` | Wired via `DataProtectionTokenProviderOptions`. |

---

## Surfaces affected (cheat-sheet)

- **New views**: public landing (`/`), `/Admin/Processes/*`, `/Admin/Plantillas/*`, `/Account/ForgotPassword`, `/Account/ResetPassword`, `/Profile`, `/Applications/{publicCode}/Review`, sidebar SupplierAdmin variant.
- **Restyled views**: admin dashboard (FR-032), reviewer dashboard (FR-033 receives pending-quotation tile), admin user list (FR-034 cascading filter), supplier list (FR-009/11 sort + autocomplete), Application draft (FR-005/16/17 Impact-first + autosave + submit gating).
- **PDF**: Funding Agreement template — *"Solicitud N.º N"* → *"Solicitud {{PublicCode}}"* (OQ-4 swap).
- **Email**: forgot-password + three stage-expiry reminders (existing SMTP).
- **Background**: `StageExpiryReminderService` hosted in Web (hourly).

---

## Out of scope (do not implement)

- BCCR auto-fetch of exchange rates (research only).
- Tropic AI quotation extraction (research only).
- OTP for sensitive profile edits.
- User-initiated email-change request workflow (admin-only path stays).
- Foreign supplier addresses outside Costa Rica.
- Multi-Process Applicant membership.
- Visual-regression tooling.
- Public marketing site beyond the FR-031 landing scaffold.
