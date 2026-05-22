# Phase 0 — Research

**Feature**: Feedback Session May-13 (021) | **Date**: 2026-05-14

This research resolves every `NEEDS CLARIFICATION` flagged in Technical Context and every Open Question in `spec.md`. Each item lists the **decision**, **rationale**, and **alternatives considered**, then names the artefact(s) the decision binds in.

---

## OQ-1 — Plantilla assignment cardinality per Process

- **Decision**: **One-to-one.** Each `Process` holds exactly one `ProcessPlantilla` snapshot. Multiple-snapshot stacking (different Application kinds inside one Process) is deferred.
- **Rationale**: Stakeholder note (*"copy-on-assign"*) and spec body (FR-004) read as singular ("a snapshot row whose payload is independent"). One-to-one keeps assignment UX trivial (one Plantilla dropdown), keeps stage-expiry overrides unambiguous, and removes the need to disambiguate which snapshot the Application form should consult. Multi-snapshot can ship later without breaking the column shape (`ProcessId` UNIQUE in `ProcessPlantillas` can be relaxed to a composite key on `(ProcessId, Kind)` if/when needed).
- **Alternatives considered**:
  - *Many-to-one (multiple Plantillas per Process)* — adds Plantilla-pick step inside Application creation; rejected because the spec does not name a use case requiring it inside scope 021.
- **Binds in**: `dbo.ProcessPlantillas.sql` (UNIQUE constraint on `ProcessId`), `data-model.md`, `tasks.md`.

## OQ-2 — Process closure semantics on FundingAgreement aftermath

- **Decision**: **Closing a Process freezes its signed `FundingAgreement`s.** No further mutation after `Process.Status = Closed`: no new Applications, no resubmissions, no agreement re-signing, no agreement amount edits. Disbursement tracking (`AmountDisbursed`) remains read-only-from-admin-only paths for accounting reconciliation.
- **Rationale**: Matches the *"single shot"* annual-cycle mental model from the meeting. Prevents accidental edits to a year that ops considers historically settled.
- **Alternatives considered**:
  - *Read-only on draft Applications only* — leaves FundingAgreements mutable, which contradicts the "settled cycle" intent.
- **Binds in**: `Process.Close()` domain method, `Application.Submit()` guard, `FundingAgreement` write paths, `data-model.md`.

## OQ-3 — Stage-expiry override granularity

- **Decision**: **Per-`Process` only.** `SystemConfigurations` holds platform defaults; `Processes` holds optional per-stage overrides as nullable columns (`SolicitudWindowDays`, `RevisionWindowDays`, `FacturacionWindowDays`). Per-Plantilla overrides are deferred.
- **Rationale**: Spec FR-006 names `SystemConfiguration` + per-`Process` only. Per-Plantilla overrides would require composing three sources (platform default → Process override → Plantilla override) at runtime; not justified by any in-scope user story.
- **Alternatives considered**:
  - *Per-Plantilla overrides too* — adds chain-of-resolution complexity; rejected per Constitution VI.
- **Binds in**: `Process` entity, `IStageExpiryEvaluator`, `SystemConfigurations` rows.

## OQ-4 — PublicCode on legacy Funding Agreement PDF (spec 018)

- **Decision**: **Template field swap.** The PDF template's *"Solicitud N.º {{Number}}"* token becomes *"Solicitud {{PublicCode}}"*; numeric reference removed from PDF output. Legacy archived PDFs are unaffected (already-generated files retain the old text). The legal term *"financiamiento"* stays in body copy per FR-029 carve-out.
- **Rationale**: Matches FR-008 ("everywhere the Application identity appears, including the Funding Agreement PDF"). A footnote dual-display would invite reader confusion ("which is *the* identifier?") and contradict SC-005 ("100 % of Application identity displays use `PublicCode`").
- **Alternatives considered**:
  - *Footnote dual-display* — rejected, contradicts SC-005.
  - *Leave PDF unchanged* — rejected, FR-008 explicitly enumerates Funding Agreement PDF.
- **Binds in**: `FundingAgreementHtmlTemplate` (spec 018 artefact), `FundingAgreementPdfGenerator`, US2 E2E coverage on the generated PDF.

## OQ-5 — Reglamento + ejemplo file content ownership

- **Decision**: **Deferred to operations.** FR-031 captures the slots; the spec explicitly allows *"Próximamente"* placeholders. The plan ships the upload surface (admin-only path through existing `IObjectStorage`) and the public-render code path; the actual binary files are loaded by ops post-merge.
- **Rationale**: Authoring ownership is a content / legal call; nothing in the code depends on the file contents.
- **Binds in**: `AdminPublicLandingFilesController` (upload + replace + delete), public `/` view (renders *"Próximamente"* on null), `IObjectStorage` category config.

## OQ-6 — Email-reminder cadence

- **Decision**: **Fixed** at T-72h, T-24h, expiry. Admin-configurable cadence deferred.
- **Rationale**: FR-025 names the cadence inline; SC-008 measures only the three-point cadence at ±1 hour. Admin config would add a SystemConfiguration surface + validation + tests that the meeting did not ask for.
- **Alternatives considered**:
  - *Admin-configurable cadence* — rejected, out of scope for 021.
- **Binds in**: `StageExpiryReminderService` constants, integration test parameter matrix.

## OQ-7 — SupplierAdmin scope

- **Decision**: **Full CRUD** on `Supplier` + `SupplierBranch`, plus `IsCompliant` toggle. *Validate-only-existing* is too narrow — the meeting explicitly described delegating supplier-catalog **administration**, not just compliance flagging.
- **Rationale**: FR-007 names CRUD + `IsCompliant` toggle in one breath; SupplierAdmin replaces the "I have to bug the admin to add this supplier" friction described in the session.
- **Alternatives considered**:
  - *Validate-only-existing* — rejected, leaves the original friction in place.
- **Binds in**: `SupplierAdminOnlyAttribute` route filter, `AdminSuppliersController` action authorization matrix.

## OQ-8 — Hint copy authorship for FR-020 fields

- **Decision**: **Deferred** to designer / copywriter. FR-020 captures the *slots* (`Item.ProductName`, `Item.Categoria`, *Cantidad de cotizaciones*, *Cédula jurídica*, *Razón social*). Initial implementation ships the `Hint` attribute infrastructure (model annotation → view rendering → es-CR catalog key) with empty initial strings; copy lands in a follow-up PR without code change.
- **Rationale**: Hint surface is structural; content is editorial. Decoupling avoids blocking a structural delivery on a copy review.
- **Binds in**: `HintAttribute` (model annotation), `_HintTooltip.cshtml`, es-CR `.resx` (empty slots reserved).

## OQ-9 — Process audit-event coverage

- **Decision**: **Extends `AdminAuditEvent`** (spec 016 pattern). New event kinds: `ProcessCreated`, `ProcessClosed`, `PlantillaAssignedToProcess`, `StageWindowOverridden`, `PlantillaForceDetached`, `SupplierAdminDeniedAccess` (already implicit per FR-007 / US3).
- **Rationale**: Established pattern; no new entity needed. Keeps the admin event surface single-source.
- **Binds in**: `AdminAuditEvent` enum + reader, US3 AC-3 E2E assertion.

## OQ-10 — Provincia *"Otro/Extranjero"* handling

- **Decision**: **Block in UI.** `Provinces` catalog holds 7 CR provinces only. SupplierBranch save with no Province match returns *"Solo proveedores con dirección en Costa Rica"* (per spec Edge Cases). No catalog row for *"Otro"*.
- **Rationale**: Out-of-scope foreign suppliers per spec; adding a catalog row would invite data-entry into a code path 021 does not service.
- **Binds in**: `Provinces` seed, `SupplierBranch` validation.

---

## Tech research items

### R-1 — `PublicCode` generator + uniqueness strategy

- **Decision**: Crypto-RNG 5-byte source → encode to 8 chars across base32 alphabet `[A-HJ-NP-Z2-9]` (excludes 0/O/1/I/L per FR-008). Insert with `INSERT … OUTPUT INSERTED.PublicCode` under a UNIQUE constraint; on `SqlException` 2627 (duplicate), retry up to 3 times; throw on 4th attempt (logged + alerted, never user-surfaced per FR-008 collision-handling note).
- **Rationale**: 32^8 ≈ 1.1 × 10^12 namespace. At 10^5 Applications, birthday-collision probability is negligible (~5 × 10^-3). Three-retry budget is conservative.
- **Alternatives considered**: UUID-v4 (too long, dictation-hostile); CRC32 of `Id` (predictable, not opaque); base58 (less common alphabet).
- **Binds in**: `IPublicCodeGenerator` / `PublicCodeGenerator`, `dbo.Applications.sql` (`PublicCode CHAR(9) NOT NULL UNIQUE`), unit tests.

### R-2 — Stage-expiry hosted background service

- **Decision**: Single `IHostedService` (`StageExpiryReminderService`) registered in `Web` host (no separate worker). Cadence: hourly timer. Per cycle: query all active Applications with `(StageEnteredAt + window)` falling in the next 72h or already expired; compute reminder bucket (T-72h, T-24h, expired) using a per-Application `RemindersSentMask` bitfield to ensure each reminder fires at most once. Email send failure → retry with exponential backoff (NFR-002 max 5 attempts).
- **Rationale**: Hourly cadence meets SC-008 ±1h granularity. Single hosted service avoids the operational cost of a second process. Bitfield mask prevents duplicate sends without a separate ledger table.
- **Alternatives considered**: Hangfire / Quartz (new managed dep, rejected per NFR-005); per-Application timer entries (state-management overhead).
- **Binds in**: `StageExpiryReminderService`, `Applications` schema (`RemindersSentMask TINYINT NOT NULL DEFAULT 0`), integration tests with MailKit capture.

### R-3 — `PasswordResetToken` + token generation

- **Decision**: Use ASP.NET Identity's built-in `IUserTwoFactorTokenProvider<TUser>` infrastructure with the default `DataProtectorTokenProvider` configured for 60-min TTL via `DataProtectionTokenProviderOptions.TokenLifespan`. Persist a single-use marker via a new `PasswordResetTokens` table storing `TokenHash`, `UserId`, `IssuedAt`, `ConsumedAt`. On reset attempt: verify Identity-issued token, then check `ConsumedAt IS NULL`; on success, atomically set `ConsumedAt = SYSUTCDATETIME()` and update the password.
- **Rationale**: Identity's provider gives cryptographic correctness for free; the new table adds single-use enforcement (Identity tokens are theoretically replayable within TTL unless server-tracked). No new dependency (Identity is already in stack).
- **Alternatives considered**: Roll-our-own SecureRandom token (Identity already solves this); reuse `AspNetUserTokens` (mixes flows, harder to audit).
- **Binds in**: `dbo.PasswordResetTokens.sql`, `PasswordResetTokenStore`, `AccountController.ForgotPassword/ResetPassword`, integration tests (consumed-token reuse rejected).

### R-4 — Province + Cantón catalog seed

- **Decision**: Catalog seeded from MOPT/TSE 2020 official cantón listing. 7 provinces, 82 cantones (post-2018 *Río Cuarto* split included; *Monteverde* split included). Seed via PostDeployment script `01_SeedProvincesCantons.sql` (idempotent `MERGE` on `Code`). Foreign keys `Provinces.Code` (CHAR(2)) ↔ `Cantons.ProvinceId` ↔ `SupplierBranches.CantonId` (nullable until migration). Cascading select fed by JSON endpoint `GET /api/cantons?provinceId={id}` returning `{ id, name }[]`.
- **Rationale**: Static, low-cardinality, mutate-rarely. Idempotent MERGE keeps re-deploy safe. JSON endpoint is small (~12 rows max per province), cacheable client-side.
- **Binds in**: PostDeployment script, `CantonsApiController`, `province-canton-cascade.js`.

### R-5 — Autosave UX contract

- **Decision**: Per-field `POST /api/applications/{id}/autosave` taking `{ fieldKey, value, etag }`. ETag is the row's `RowVersion` (existing optimistic-concurrency pattern). Response `200 { etag, savedAt }` or `409 Conflict` (stale ETag → client refresh prompt). Client JS module (`autosave.js`) hooks every input's `blur` event, debounces 300 ms, sends, swaps the `_AutosaveIndicator` partial state (idle → saving → ✓ Guardado HH:MM | ⚠ No guardado).
- **Rationale**: Matches existing optimistic-concurrency conventions (Constitution-aligned). Per-field grain matches the spec's "on-blur" specificity (US2 AC-2 requires *"within 1 s"* feedback).
- **Alternatives considered**: Whole-form debounced autosave (too coarse, loses per-field feedback); WebSocket push (over-engineered, new dep).
- **Binds in**: `ApplicationController.Autosave`, `_AutosaveIndicator.cshtml`, `autosave.js`, integration tests.

### R-6 — `Impact` value object on Application

- **Decision**: `Application` gains nullable `ImpactTemplateId` FK to `ImpactTemplates` and a one-to-many child collection `ApplicationImpactParameterValues` reusing the existing `ImpactParameterValues` table (re-pointed from `Impacts` to `Applications`). Drop `Items.ImpactId` outright (NFR-001 — no production data). Drop legacy `Impacts` table once nothing else references it (spec 016/017 audit confirms no other references → safe drop).
- **Rationale**: Reuses existing parameter-value model; only the parent key changes. Avoids JSON column anti-pattern.
- **Alternatives considered**: JSON blob on `Applications` (loses queryability, contradicts spec 015 reporting paths); keep `Impacts` table and FK from `Applications` (1:1 with empty hop — pointless indirection).
- **Binds in**: `dbo.Applications.sql`, `dbo.Items.sql`, `dbo.ImpactParameterValues.sql` (re-target FK), domain validation `Application.SetImpact()`.

### R-7 — Internationalisation + new copy strings

- **Decision**: All new strings registered in `Localization/es-CR.resx` (single canonical file). The English fallback `.resx` ships empty keys to satisfy NFR-003. New keys grouped by surface (e.g. `Public.Hero.Cta`, `Application.Review.Confirm`, `Account.ForgotPassword.Title`, `Application.Disclaimer.Fx`, `Banner.StageExpiry.Closed`). The forbidden strings `Bienvenido/a` and `financiamiento` are removed from `.resx` entirely (only the Funding Agreement PDF template-literal — non-resource — keeps *"financiamiento"*).
- **Rationale**: Constitution-aligned single source. Removing the strings from the catalog rather than just the views guarantees no view can re-introduce them via key lookup.
- **Binds in**: `Localization/es-CR.resx`, grep test in CI (`SC-005`, `SC-012` enforcement).

### R-8 — Forbidden-string CI assertion

- **Decision**: Add a Playwright assertion in `US7_AcompanamientoCopyAndLanding.cs` that crawls every applicant-facing surface (anonymous landing + applicant dashboard + draft form + /review + signing flow) and asserts zero matches for `/financiamiento/i`, `/Bienvenido\/?a/i`, `/Solicitud N\.º \d+/`. Implemented via a `ForbiddenStringsCrawler` Page Object that opens each route, dumps `page.content()`, and asserts. Reuses the same crawler in `US2_ApplicantE2E.cs` for the PublicCode-everywhere assertion (SC-005).
- **Rationale**: A single crawler covers SC-005, SC-012, SC-015 — these three success criteria are structurally identical (rendered-HTML grep, no matches).
- **Binds in**: Shared `ForbiddenStringsCrawler` POM helper, US2 + US7 tests.

### R-9 — SupplierAdmin authorization

- **Decision**: New `[Authorize(Roles = SupplierAdmin)]` class-level filter on `AdminSuppliersController`. New `[SupplierAdminDenied]` attribute on every other admin controller's class declaration: emits 403 + writes `AdminAuditEvent` row of kind `SupplierAdminDeniedAccess` when the user holds only the SupplierAdmin role and reaches the route. Sidebar navigation hides non-Supplier admin links for SupplierAdmin via a `_AdminSidebar.cshtml` role-aware switch.
- **Rationale**: Filter composition gives single-place enforcement; auditing the *attempt* (not just the success) per FR-007 needs the 403 path to write a row (matches `AdminAuditEvent` 016-pattern).
- **Binds in**: `SupplierAdminOnlyAttribute`, `SupplierAdminDeniedAttribute`, `_AdminSidebar.cshtml`, US3 integration test.

### R-10 — Soft-delete dashboard-filter audit

- **Decision**: Centralise the soft-delete predicate in a single `IApplicationQueryFilter.ExcludeDeleted(IQueryable<Application>)` extension method used by every projection (applicant dashboard, admin dashboard, reviewer queue, signing inbox, "borrador listo para enviar" prompt, counters). Audit existing call sites and route all of them through this helper. Add an analyzer-style unit test (`DashboardQueriesHonorSoftDeleteTests`) that uses Roslyn / reflection to confirm no `_dbContext.Applications.AsQueryable()` call escapes the helper inside listed namespaces.
- **Rationale**: FR-021 + SC-011 demand a regression that holds across all dashboard surfaces; a single helper plus a structural test prevents the bug from re-emerging on a *new* dashboard surface added later.
- **Binds in**: `IApplicationQueryFilter`, `DashboardQueriesHonorSoftDeleteTests`, US8 E2E regression.

### R-11 — Background service test strategy

- **Decision**: Integration tests spin up the host via `WebApplicationFactory`-equivalent against the Aspire-orchestrated SQL container, replace `IEmailSender` with a `CapturingEmailSender` (in-memory queue), inject an `IStageExpiryClock` fake (advances by `TimeSpan`), then assert the reminder queue contains exactly the expected envelopes for synthetic Applications at boundaries (T-72h, T-24h, expired).
- **Rationale**: Constitution III + project rule — integration tests hit real DB, never mocks. Email and clock are out-of-process / external — injectable seams are appropriate.
- **Binds in**: `StageExpiryReminderServiceTests`, `IStageExpiryClock`.

### R-12 — Admin dashboard KPI projection

- **Decision**: Extend `IAdminDashboardProjection` (spec 017) with two new methods: `Task<int> CountPersonasActivas()` (distinct `Applicants` with at least one non-soft-deleted Application in last 12 months) and `Task<decimal> SumFondosEntregados()` (sum of `FundingAgreement.AmountDisbursed` where `Status = Executed`). Move the pending-quotation tile out of admin and into `IReviewerDashboardProjection.CountPendingQuotations()`. Existing 4 action-KPI tiles preserved (spec 017 FR-027 alignment).
- **Rationale**: Reuses the projection seam established in 017. No new schema; pure read-side.
- **Binds in**: `IAdminDashboardProjection`, `IReviewerDashboardProjection`, `_AdminDashboard.cshtml`, reviewer dashboard view.

### R-13 — Stage-expiry HTTP 422 mapping

- **Decision**: `StageWindowClosedException` thrown in domain → mapped in a global `IExceptionFilter` to `UnprocessableEntity(new ProblemDetails { Title, Detail = "La etapa cerró el {{fecha}}. Contacte al administrador.", Status = 422 })`. Banner copy lives in `_StageCountdownBanner.cshtml`.
- **Rationale**: Single mapping seam; controllers stay thin; FR-006 / FR-024 satisfied.
- **Binds in**: `StageWindowClosedException`, `DomainExceptionFilter`, banner partial.

### R-14 — Province + Cantón cascade — no managed dep

- **Decision**: Vanilla JS `province-canton-cascade.js` fetches `/api/cantons?provinceId={id}` on `change`, repopulates the cantón `<select>` options. No new library; uses `fetch` + `URLSearchParams`. ETag caching set on the API response (`Cache-Control: public, max-age=3600`) — catalog is effectively static.
- **Rationale**: NFR-005 (no new managed deps). Plain JS is sufficient; Tabler's `Select` styling is CSS-only.
- **Binds in**: `province-canton-cascade.js`, `CantonsApiController`.

### R-15 — Public landing without auth

- **Decision**: `HomeController.Index` is `[AllowAnonymous]`; the existing route fallback (post-login redirect) is preserved. Anonymous `/` renders the FR-031 view. Three slot files live under `IObjectStorage` category `public-landing-files` (new entry in `Storage:Categories` config). Admin upload surface: `AdminPublicLandingFilesController` (3 fixed slots: `reglamento`, `ejemplo-cotizacion`, `sponsor-strip` — but sponsor strip reuses spec 019 assets, so it's not a slot upload but a rendered partial).
- **Rationale**: FR-031 + spec 014 (IObjectStorage) + spec 019 (sponsor strip reuse).
- **Binds in**: `HomeController`, `AdminPublicLandingFilesController`, `Storage:Categories:public-landing-files`, public view.

---

## Summary of resolutions

| Open question | Resolution |
|---------------|-----------|
| OQ-1 | One Plantilla per Process (one-to-one) |
| OQ-2 | Close-Process freezes FundingAgreements |
| OQ-3 | Stage-expiry override per-Process only |
| OQ-4 | PublicCode = template field swap on PDF |
| OQ-5 | Reglamento/ejemplo files ops-uploaded later |
| OQ-6 | Reminder cadence fixed (T-72h / T-24h / expiry) |
| OQ-7 | SupplierAdmin = full CRUD |
| OQ-8 | Hint copy slots only, strings deferred |
| OQ-9 | Process events extend `AdminAuditEvent` |
| OQ-10 | Foreign provinces blocked in UI |

All NEEDS CLARIFICATION items resolved. No outstanding research debt. Plan proceeds to Phase 1 (data-model, contracts, quickstart).
