---
description: "Task list for Regulatory Freshness Gating + Hacienda API Sync (feedback-3 slice D)"
---

# Tasks: Regulatory Freshness Gating + Hacienda API Sync

**Input**: Design documents from `specs/043-regulatory-freshness-hacienda-sync/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/interfaces.md, quickstart.md

**Tests**: INCLUDED. Constitution Principle III (E2E) is NON-NEGOTIABLE; the spec defines an Independent Test + acceptance scenarios per story. Each story carries E2E tests; unit/integration complement them. Integration tests hit a real DB (no mocks).

**Organization**: by user story (US1–US4) for independent implementation/testing. The live Hacienda API is NEVER called in tests — a config-gated `FakeHaciendaApiClient` is used (mirrors `StubAiClient`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no incomplete-task dependency)
- **[Story]**: US1–US4; Setup/Foundational/Polish carry no story label

## Path Conventions

Clean Architecture under `src/`: `FundingPlatform.Domain`, `.Application`, `.Infrastructure`, `.Web`, `.Database`; tests under `tests/FundingPlatform.Tests.{Unit,Integration,E2E}`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: configuration seams + es-CR copy scaffolding shared by all stories.

- [ ] T001 Add `RegulatoryFreshnessOptions` (`FreshnessWindowDays=30`) and `HaciendaSyncOptions` (`Provider`/`Enabled`/`RunAtLocalTime`/`BatchSize`/`PerCallDelayMs`/`BaseUrl`) in `src/FundingPlatform.Application/Regulatory/`; bind both via `services.Configure<>` in `src/FundingPlatform.Infrastructure/DependencyInjection.cs`; add default keys to `src/FundingPlatform.Web/appsettings.json` and pin `Regulatory:HaciendaSync:Provider=Fake` for dev/ephemeral in `src/FundingPlatform.AppHost/AppHost.cs`.
- [ ] T002 [P] Add `RegulatoryFreshnessResources` (es-CR) under `src/FundingPlatform.Web/Resources/` with keys for the block message, the non-blocking warning, the sync-failure label ("verificación fallida") + reason, the `HaciendaSyncOutcome` labels, and the digest subject/body strings.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the freshness predicate + the shared freshness query used by the US1 hard gate AND the US4 warning/digest. **No story can complete without this.**

**⚠️ CRITICAL**: complete before US1/US4.

- [ ] T003 [P] Add freshness predicate to `src/FundingPlatform.Domain/Entities/Supplier.cs`: `bool IsRegulatoryStale(int windowDays, DateTime nowUtc)` and `IReadOnlyList<RegulatoryField> StaleRequiredFields(int windowDays, DateTime nowUtc)` — a required field (Hacienda/Ccss/Sicop) is stale when `{Field}LastReviewedAt` is null OR `< nowUtc.AddDays(-windowDays)` (FR-001, FR-005).
- [ ] T004 [P] Unit tests for the freshness predicate in `tests/FundingPlatform.Tests.Unit/` — null timestamp (stale), exactly-at-window boundary, just-inside/just-outside window, per-field independence, all-fresh empty result.
- [ ] T005 Add `StaleRegulatoryFinding` record (`SupplierId, SupplierName, RegulatoryField, DateTime? LastReviewedAt`) and `IRegulatoryFreshnessService.GetStaleFindingsForApplicationAsync(int applicationId, CancellationToken)` in `src/FundingPlatform.Application/Regulatory/`.
- [ ] T006 Implement `RegulatoryFreshnessService` in `src/FundingPlatform.Infrastructure/Services/` — loads `Application → Items → SelectedSupplier`, dedups suppliers via `Item.SelectedSupplierId` (FR-006 / research D2), flattens `Supplier.StaleRequiredFields(window, nowUtc)` to findings using `RegulatoryFreshnessOptions`; register in `DependencyInjection.cs`.
- [ ] T007 Integration test `RegulatoryFreshnessQueryTests` in `tests/FundingPlatform.Tests.Integration/` (real DB) — selected-supplier scoping (rejected/unselected quotes excluded), window math, never-reviewed field, multi-supplier flatten, all-fresh empty.

**Checkpoint**: freshness findings query available to gate + warning.

---

## Phase 3: User Story 1 — Staleness block (Priority: P1) 🎯 MVP

**Goal**: an application cannot advance through the audit stage while any selected supplier has a stale required regulatory field; the message names provider+field+last-reviewed; re-authorize clears it.

**Independent Test**: seed an audit-stage app whose selected supplier has a stale CCSS timestamp; auditor advance is blocked with the naming message; slice-A "Reviewed — No Change" then unblocks. (Uses slice-A-maintained timestamps — no Hacienda sync needed, so US1 is a standalone MVP.)

- [ ] T008 [P] [US1] E2E `RegulatoryFreshnessBlockTests` in `tests/FundingPlatform.Tests.E2E/` (write first, expect fail): stale required field blocks generate/confirm/release at the audit stage; message names provider+field+last-reviewed (FR-007); never-reviewed field blocks; multiple stale providers/fields all enumerated; re-authorize clears the block (FR-008); all-fresh advances.
- [ ] T009 [US1] Insert the server-side freshness gate in `src/FundingPlatform.Web/Controllers/FundingAgreementController.cs` `Generate` (auditor path) — call `IRegulatoryFreshnessService`; non-empty findings → refuse + redirect back with the es-CR block message (mirror the existing `IsAuditChecklistCompleteAsync` check shape).
- [ ] T010 [US1] Insert the gate in `src/FundingPlatform.Infrastructure/Services/AuditWorkflowService.cs` `ReleaseForSignatureAsync` and the `ConfirmAgreementPdf` path (defense in depth so a crafted POST cannot bypass, FR-009).
- [ ] T011 [US1] Add the es-CR block-message builder enumerating provider + field + last-reviewed (FR-007) using `RegulatoryFreshnessResources`; surface via the existing toast/inline-error mechanism.
- [ ] T012 [US1] Run filtered E2E `RegulatoryFreshnessBlock` green; run the slice-C `FundingAgreement`/`AuditorWorkflow`/`AuditReturn` regression classes green (gate is additive at the advance actions).

**Checkpoint**: US1 fully functional and independently testable (MVP).

---

## Phase 4: User Story 2 — Daily Hacienda sync (Priority: P1)

**Goal**: a daily job keeps each provider's Hacienda status current and audited via the real API, behind a test-fakeable seam.

**Independent Test**: with `FakeHaciendaApiClient`, trigger one cycle via the dev endpoint; verify changed/unchanged statuses, refreshed freshness (source `Api`), and audit entries.

- [ ] T013 [P] [US2] Add `HaciendaSyncOutcome` enum (`Success=1, Failure=2`) in `src/FundingPlatform.Domain/Enums/`.
- [ ] T014 [P] [US2] Add columns `HaciendaSyncAttemptAt DATETIME2 NULL`, `HaciendaSyncOutcome TINYINT NULL`, `HaciendaSyncError NVARCHAR(500) NULL` to `src/FundingPlatform.Database/Tables/dbo.Suppliers.sql`; map in `src/FundingPlatform.Infrastructure/Persistence/Configurations/SupplierConfiguration.cs` with `HaciendaSyncOutcome` `HasConversion<byte?>()` and `HaciendaSyncError` `HasMaxLength(500)`.
- [ ] T015 [P] [US2] Add `Supplier.ApplyHaciendaSyncResult(HaciendaStatus mapped, DateTime nowUtc)` (returns `RegulatoryChange` Changed/ReviewedNoChange; stamps Hacienda last-reviewed `Api`/`"system"`; sets sync outcome `Success`) and `Supplier.RecordHaciendaSyncFailure(DateTime nowUtc, string reason)` (sets sync metadata only; touches no status/last-reviewed — FR-018) in `Supplier.cs`; unit tests for both.
- [ ] T016 [P] [US2] Add `AdminAuditEvent.SupplierHaciendaSyncFailed = "supplier.hacienda_sync_failed"` constant in `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs` (success reuses existing `SupplierRegulatoryChanged`/`SupplierRegulatoryReviewed`).
- [ ] T017 [P] [US2] Define `IHaciendaApiClient` + `HaciendaLookupResult` (`Found`/`NotRegistered`/`Failed`) + `HaciendaSituacion(Estado, Moroso, Omiso)` in `src/FundingPlatform.Application/Abstractions/Hacienda/`.
- [ ] T018 [P] [US2] Implement pure `HaciendaStatusMapper.Map(HaciendaLookupResult)` in `src/FundingPlatform.Infrastructure/Hacienda/` per research D1; exhaustive unit tests (al día / moroso / Inscrito+omiso → CobroAdministrativo / Desinscrito+moroso=NO→DesinscritoAlDia / Desinscrito+moroso=SI→DesinscritoMoroso / 200 "No inscrito"→SinInscripcion / 404→SinInformacion / unrecognized estado→Failed).
- [ ] T019 [US2] Implement `LiveHaciendaApiClient` in `src/FundingPlatform.Infrastructure/Hacienda/` — typed `HttpClient` (`GET {BaseUrl}/fe/ae?identificacion=`), parse 200 body, 404→NotRegistered, else/timeout/exception→Failed; register via `AddHttpClient` (BaseAddress + timeout). No new NuGet.
- [ ] T020 [P] [US2] Implement `FakeHaciendaApiClient` in `src/FundingPlatform.Infrastructure/Hacienda/` — canned results keyed by identification, stageable outcomes, static `LookupCallCount` + `Reset()` (mirror `StubAiClient`).
- [ ] T021 [US2] Wire the config gate in `DependencyInjection.cs`: `Regulatory:HaciendaSync:Provider` `Live`→`LiveHaciendaApiClient`, else→`FakeHaciendaApiClient`.
- [ ] T022 [US2] Implement `HaciendaSyncService : BackgroundService` in `src/FundingPlatform.Infrastructure/BackgroundServices/` — daily next-run scheduling to `RunAtLocalTime` (America/Costa_Rica) via a shared `NextDailyRun` helper, startup-resilient loop, public `RunOnceAsync` test seam; per-supplier: validate id (empty/malformed→`RecordHaciendaSyncFailure`) → `LookupAsync` → `Map` → `ApplyHaciendaSyncResult`/`RecordHaciendaSyncFailure` → write audit (D6 verbs) → `SaveChangesAsync` under `RowVersion` (concurrency conflict→skip+log, FR-025); batch per `BatchSize`; one provider's exception never aborts the run (FR-024); register `AddHostedService`.
- [ ] T023 [US2] Add Development-only `GET /Dev/RunHaciendaSync` endpoint (mirrors `GET /Account/SeedUser`; 404 outside Development) invoking `HaciendaSyncService.RunOnceAsync`, returning the `{checked,changed,unchanged,failed}` summary.
- [ ] T024 [US2] Ensure the supplier detail freshness display renders the `Api` source in es-CR ("actualizado … por sistema") in `src/FundingPlatform.Web/Controllers/Admin/AdminSuppliersController.cs` detail view (slice-A display extended for the automated source).
- [ ] T025 [P] [US2] Integration tests `HaciendaSyncTests` (real DB, `FakeHaciendaApiClient`) in `tests/FundingPlatform.Tests.Integration/`: changed value → status updated + `supplier.regulatory_changed` (source `Api`); unchanged → `supplier.regulatory_reviewed` + timestamp refreshed; 404 → `SinInformacion`; 200 "No inscrito" → `SinInscripcion`.
- [ ] T026 [P] [US2] E2E `HaciendaSyncTests` in `tests/FundingPlatform.Tests.E2E/`: stage a Fake result → `GET /Dev/RunHaciendaSync` → supplier detail shows updated Hacienda status + "por sistema" freshness; audit present.
- [ ] T027 [US2] Run filtered integration + E2E (`HaciendaSync`) green.

**Checkpoint**: US2 functional and independently testable.

---

## Phase 5: User Story 3 — Sync failures visible, never silent (Priority: P2)

**Goal**: providers the daily job could not verify are discoverable (per-provider outcome + admin-list filter/badge + audit), with no data corruption on failure.

**Independent Test**: stage failures via the Fake, run sync, verify failure surfaces and data is unchanged.

**Depends on**: US2 (the sync produces the failure metadata).

- [ ] T028 [P] [US3] Show the last-sync outcome (attempt time + outcome + error reason) on the supplier detail screen in `src/FundingPlatform.Web/Controllers/Admin/AdminSuppliersController.cs` + its view.
- [ ] T029 [US3] Add a "verificación fallida" filter + row badge to the admin supplier list (`AdminSuppliersController.Index` + view) keyed on `HaciendaSyncOutcome == Failure` (FR-020).
- [ ] T030 [P] [US3] Add es-CR `HaciendaSyncOutcome`/failure labels to `RegulatoryFreshnessResources`.
- [ ] T031 [P] [US3] Integration test (real DB, Fake) in `tests/FundingPlatform.Tests.Integration/`: API-failure + malformed-id → no value/last-reviewed change (FR-018), outcome `Failure` + reason set, `supplier.hacienda_sync_failed` audit written, batch continues past the failed provider (FR-024); a `RowVersion` conflict is skipped (FR-025).
- [ ] T032 [P] [US3] E2E in `tests/FundingPlatform.Tests.E2E/`: stage a failure via Fake → run sync → supplier detail shows "verificación fallida" + reason; admin list filter finds it; the supplier's regulatory data is unchanged.
- [ ] T033 [US3] Run filtered integration + E2E green.

**Checkpoint**: US3 functional; failures never silent.

---

## Phase 6: User Story 4 — Early warning + stale-value digest (Priority: P3)

**Goal**: a non-blocking warning surfaces stale providers/fields on the reviewer send-to-audit and auditor screens before the hard block; a daily digest emails group-scoped auditors about stale providers.

**Independent Test**: stale provider → warning visible on both screens; trigger digest → captured in smtp4dev for the scoped auditor.

**Depends on**: Foundational (`IRegulatoryFreshnessService`).

- [ ] T034 [P] [US4] Add a non-blocking warning partial driven by `IRegulatoryFreshnessService` and render it on `src/FundingPlatform.Web/Views/Review/Review.cshtml` (send-to-audit) and `src/FundingPlatform.Web/Views/Audit/*.cshtml` — names stale providers/fields, does not block (FR-010).
- [ ] T035 [P] [US4] Add `RegulatoryDigestEmailFactory` in `src/FundingPlatform.Infrastructure/Email/` + `src/FundingPlatform.Web/Views/Emails/Regulatory/` html + `.text` twin, composed through the spec-041 `_EmailLayout` brand shell (es-CR), mirroring `StageReminderEmailFactory`.
- [ ] T036 [US4] Implement `RegulatoryFreshnessDigestService : BackgroundService` in `src/FundingPlatform.Infrastructure/BackgroundServices/` — daily next-run (shared `NextDailyRun` helper) + public `RunOnceAsync`; gather audit-pipeline apps (`State ∈ {PendingAudit, ReturnedFromAudit}`) whose selected suppliers have stale required fields → group by `Group` → resolve `Auditor`-role members per group → one aggregated `IEmailSender` send per auditor with in-cycle backoff (allowlist applies); register `AddHostedService`. No outbox, no new `NotificationEvent` (research D3).
- [ ] T037 [US4] Add Development-only `GET /Dev/RunFreshnessDigest` endpoint (404 outside Development) invoking `RegulatoryFreshnessDigestService.RunOnceAsync`, returning the count sent.
- [ ] T038 [P] [US4] Integration test (real DB, smtp capture) in `tests/FundingPlatform.Tests.Integration/`: stale-supplier audit-pipeline app → one digest email to the group's auditor; no stale → no email; an app outside the auditor's group → not included.
- [ ] T039 [P] [US4] E2E in `tests/FundingPlatform.Tests.E2E/`: warning visible on reviewer send-to-audit + auditor screens; `GET /Dev/RunFreshnessDigest` → smtp4dev captures a brand-shell digest for the scoped auditor.
- [ ] T040 [US4] Run filtered E2E green.

**Checkpoint**: all four user stories independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T041 [P] Extract/confirm the shared `NextDailyRun(localTime, tz)` scheduling helper used by both `HaciendaSyncService` and `RegulatoryFreshnessDigestService` (DRY; unit-test the next-run calc across day boundaries).
- [ ] T042 Run quickstart.md validation end-to-end (build, dev run, both dev endpoints, three live `curl` contract checks optional).
- [ ] T043 [P] Confirm the `Inscrito`+`omiso=SI`→`CobroAdministrativo` mapping row with a stakeholder (research D1 residual); adjust `HaciendaStatusMapper` + its unit test if corrected.
- [ ] T044 Run the affected-class regression sweep (slice-C `FundingAgreement`/`AuditorWorkflow`/`AuditReturn`/`SigningWayfinding`) green; update CLAUDE.md Recent Changes + decomposition row D → shipped at merge time.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)**: no dependencies.
- **Foundational (P2)**: after Setup. **Blocks US1 and US4** (both use `IRegulatoryFreshnessService`).
- **US1 (P3)**: after Foundational. Standalone MVP (uses slice-A timestamps; no sync).
- **US2 (P4)**: after Setup; independent of US1/Foundational-freshness (its own sync infra). Can run in parallel with US1.
- **US3 (P5)**: after **US2** (consumes sync failure metadata).
- **US4 (P6)**: after Foundational (warning) ; digest is richer with US2 data but works on any stale timestamps.
- **Polish (P7)**: after the stories it touches.

### Story independence

- US1 and US2 are both P1 and **independent** of each other (gate vs sync) — can be built in parallel after their respective prerequisites.
- US3 builds on US2. US4 builds on Foundational.

### Within a story

- Tests written first and failing before implementation (constitution); Domain → Application → Infrastructure → Web; run filtered E2E last.

### Parallel opportunities

- T002 ∥ T001 (after T001 options exist for resource refs is not required — independent files).
- Foundational: T003 ∥ T004; then T005 → T006 → T007.
- US2: T013 ∥ T014 ∥ T015 ∥ T016 ∥ T017 ∥ T018 ∥ T020 (distinct files); then T019/T021 (DI) → T022 → T023; tests T025 ∥ T026.
- US3: T028 ∥ T030; T031 ∥ T032.
- US4: T034 ∥ T035; T038 ∥ T039.

---

## Parallel Example: User Story 2 foundation

```bash
# After Setup, launch US2's independent building blocks together:
Task: "T013 HaciendaSyncOutcome enum"
Task: "T014 dbo.Suppliers columns + EF config"
Task: "T015 Supplier sync domain methods + unit tests"
Task: "T016 AdminAuditEvent constant"
Task: "T017 IHaciendaApiClient + DTOs"
Task: "T018 HaciendaStatusMapper + exhaustive unit tests"
Task: "T020 FakeHaciendaApiClient"
```

---

## Implementation Strategy

### MVP first (US1 only)

1. Phase 1 Setup → Phase 2 Foundational → Phase 3 US1.
2. **STOP & VALIDATE**: stale field blocks the auditor advance with the naming message; re-authorize clears it (filtered E2E green + slice-C regression).
3. US1 is shippable on its own (auditors keep CCSS/SICOP fresh manually).

### Incremental delivery

US1 (block) → US2 (sync keeps Hacienda fresh automatically) → US3 (failure visibility) → US4 (warning + digest). Each is an independently testable increment.

### Delivery gate (per CLAUDE.md)

A story is delivered only when its **filtered E2E tests have been personally executed and are green**. Run the per-story filtered E2E (T012/T027/T033/T040) plus the slice-C regression sweep (T044). The full ~30-min E2E suite runs only on explicit request.

---

## Notes

- The live Hacienda API is **never** called in tests — `Regulatory:HaciendaSync:Provider=Fake` in dev/ephemeral; `FakeHaciendaApiClient` supplies outcomes.
- No new managed dependency; schema change is dacpac-only; no new `ApplicationState` or `NotificationEvent`.
- Commit after each task or logical group; commit + push at each speckit checkpoint per project convention.

## Deviations (implementation)

1. **Shared es-CR copy in Application, not Web.Resources.** The gate/warning/digest formatters live in `Application/Regulatory/RegulatoryFreshnessCopy.cs` (field labels, finding line, block/warning headings, digest copy) because Infrastructure (`AuditWorkflowService`, the digest factory) *and* Web both produce them — the same Clean-Architecture exception spec 034 made for `BatchUserRowReasons`. The Web `RegulatoryFreshnessResources` (T002/T030) keeps view-only strings (outcome labels, filter label) and **delegates** field labels to the Application copy.
2. **`UserFacingErrorCode.RegulatoryDataStale`** (new) — its translator passes the `Detail` (the data-driven es-CR enumerated block message) through verbatim, mirroring the shipped `SupplierCcssSinInscripcion` precedent (data in `Detail`, not domain English).
3. **`LiveHaciendaApiClient` uses a manually-constructed long-lived `HttpClient`** (singleton registration) instead of `IHttpClientFactory`/`AddHttpClient` (T019/T021 wording): `Microsoft.Extensions.Http` is not referenced in the Infrastructure project, so this honors "no new managed dependency". A single client for a once-daily worker is safe.
4. **Integration tests use EF InMemory** (matches the project's prevailing service-test pattern + the spec-041 precedent). Real-SQL behavior — TINYINT `HasConversion<byte?>` materialization and the **`RowVersion` concurrency-skip (FR-025)** — is exercised only by the E2E suite (real SQL), not unit/integration.
5. **Gate scope = non-admin (auditor) path.** The freshness block sits inside the existing `if (!isAdministrator)` branch in `FundingAgreementController.Generate` (and unconditionally in `AuditWorkflowService.Confirm/Release` for defense-in-depth); admins retain the slice-C advance bypass, consistent with the `IsAuditChecklistCompleteAsync` gate.
6. **Digest reuses `HaciendaSyncOptions.RunAtLocalTime`** for its daily schedule (no separate digest-time config knob).
7. **Extra dev endpoint `/Dev/StageHaciendaOutcome`** (beyond the contract's two `/Dev/Run*`) lets E2E stage `FakeHaciendaApiClient` outcomes deterministically over HTTP (the fake's staging is otherwise in-process static).
8. **Malformed-id pre-validation** (T031/contract): the sync validates the local id (≥9 digits) and records a failure *without* an API call for malformed ids (e.g. passport-only suppliers).
9. **T024** extended `ReviewFreshness.Describe` so Api/System sources render "por el sistema (Hacienda)/(sistema)" (slice-A `RegulatoryDisplayTests` updated accordingly).
10. **T043** (Inscrito+omiso=SI → `CobroAdministrativo`) shipped best-effort per research D1; flagged for stakeholder confirmation, non-blocking. `DesinscritoDeOficio` is never auto-set (no `fe/ae` signal).
11. **System-sentinel actor (real-SQL fix, E2E-surfaced).** The sync attributes the audit `ActorUserId` and `Supplier.HaciendaLastReviewedBy` to the **system-sentinel user's real id** (resolved via `Users.IgnoreQueryFilters().Where(IsSystemSentinel)`), not the literal `"system"` — the latter violated `FK_AdminAuditEvents_AspNetUsers` + `FK_Suppliers_HaciendaReviewedBy_AspNetUsers` on real SQL (InMemory didn't enforce FKs; the spec-040 "InMemory hid it" lesson). `ApplyHaciendaSyncResult` gained a `systemActorUserId` parameter; the run aborts (logged) if no sentinel exists.
12. **Offline-default provider (real-SQL fix, E2E-surfaced).** The DI gate defaults to the **Fake** client and appsettings no longer pins `Provider` — so dev/E2E never touch the live API even if Aspire's `WithEnvironment` forwarding doesn't override appsettings (it didn't, in the fixture). This mirrors `AiComparison`'s Stub default; real envs opt into `Live` via azd-env / container config (deviation from the data-model's "appsettings Provider=Live default").
