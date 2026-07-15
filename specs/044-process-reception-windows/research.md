# Research: Fund Process Reception Windows + Applicant Timing UX (044)

Phase 0 decisions. Each resolves a design unknown surfaced by the spec, grounded in concrete codebase patterns (file:line references from the planning sweep).

## D1 — Timezone handling reduces to UTC instant comparison for gating

**Decision**: Store window `StartUtc`/`EndUtc` as absolute `DATETIMEOFFSET` (UTC). **Gating** (`start ≤ now < end`) is a pure UTC instant comparison — no timezone math. Costa Rica timezone is needed **only at the boundaries**: (a) converting admin CR-local input → UTC at save, and (b) formatting UTC → CR-local for display.

**Rationale**: Because both the windows and "now" are absolute instants, the open/closed/upcoming determination is timezone-free and cannot drift across the CR/UTC offset (resolves the spec's boundary edge case). This keeps the gating policy a pure function and the timezone surface tiny.

**Implementation**: A new `IBusinessTimeZone` abstraction (Application interface, Infrastructure impl) resolving `TimeZoneInfo` from config key `Process:BusinessTimeZone` (default `America/Costa_Rica`; CR has no DST so the offset is a constant −6). Used in the Web layer only — input parsing (`datetime-local` → `DateTimeOffset` via `tz.GetUtcOffset` → `.ToUniversalTime()`) and display formatting (`utc.ToOffset(crOffset)` + es-CR `dd/MM/yyyy HH:mm`). Defensive fallback to a fixed −6 `TimeSpan` if the named zone is absent on the host.

**Alternatives rejected**: Per-fund timezones (speculative, spec out-of-scope); converting to CR before comparing (needless and a drift risk).

## D2 — Time source reuses the existing `IStageExpiryClock`

**Decision**: Reuse `IStageExpiryClock.UtcNow` (`src/FundingPlatform.Domain/Interfaces/IStageExpiryClock.cs`, impl `SystemStageExpiryClock`, registered singleton at `Infrastructure/DependencyInjection.cs:123`) as the single "now" source for gating and notices. No new clock.

**Rationale**: It is already the project's injectable clock (used by `SubmitApplicationHandler`, `AutosaveFieldHandler`, `ReviewController`, `PasswordResetTokenStore`), so unit/integration tests already know how to fake it. The applicant notice ViewModel already carries a `Now` field for the same reason.

**E2E time control**: E2E does **not** freeze the clock. It seeds reception windows with dates **relative to real `UtcNow`** (e.g. now−1d…now+1d = open; all-in-past = closed; now+1d…now+2d = upcoming). The exact-boundary case (SC-002) is covered by unit/integration tests with a faked `IStageExpiryClock`, not E2E.

## D3 — Submission gate: pure domain evaluation, enforced in the handler

**Decision**: Add a pure domain evaluation `ReceptionWindowEvaluation.Evaluate(IReadOnlyList<ReceptionWindowSnapshot> windows, DateTimeOffset nowUtc) → ReceptionAvailability`. The **enforcement** (load windows, throw on closed) lives in `SubmitApplicationHandler` (Infrastructure), which already resolves cross-aggregate data (it resolved `stageClosesAt` from the Process the same way at `SubmitApplicationHandler.cs:184–197`).

**Rationale**: Window data is cross-aggregate (sibling `ProcessEvents`, reached via Application→Group→Process), so the Application entity cannot and should not load it — mirroring how the old Solicitud `stageClosesAt` was resolved in the handler and passed in. Keeping the *evaluation* pure (a static domain function over snapshots) preserves testability and the Rich-Domain-Model principle; the *enforcement* sits at the same layer it does today.

**Throw + surface**: New `ReceptionWindowClosedException : Exception` (`Domain/Exceptions/`) carrying the `ReceptionAvailability` status + the relevant boundary instant. `DomainExceptionFilter` (`Web/Filters/DomainExceptionFilter.cs`, which already maps `StageWindowClosedException → 422`) gains a case mapping it to **422** with the typed es-CR message via `IUserFacingErrorTranslator` (`UserFacingErrorCode.ReceptionWindowClosed`, Detail-verbatim, mirroring spec-043's `RegulatoryDataStale`).

## D4 — Remove the Solicitud duration gate from BOTH submit and autosave

**Decision (FR-008 + FR-015)**: Delete the Solicitud per-stage duration gate from:
1. `Application.Submit(...)` — drop the `currentStage/stageClosesAt/now` guard params; new signature `Submit(int minQuotations)`. The `StageWindowClosedException` throw at `Application.cs:435` is removed.
2. `SubmitApplicationHandler` — delete `ResolveStageClosesAtAsync`/`ResolvePlatformDefaultAsync` (`:184–207`); insert the reception-window evaluation instead.
3. **`AutosaveFieldHandler` (`:63`)** — delete the `StageWindowClosedException(Solicitud, stageClosesAt)` throw entirely. Draft editing becomes always-allowed (FR-015); autosave keeps only its concurrency/`AutosaveConflictException` path.

**Rationale**: The Solicitud duration window was the *only* submission-timing mechanism; reception windows replace it. Autosave was gated by the same window, which directly contradicts FR-015 (existing drafts always editable), so it must go too — this was the non-obvious consequence the planning sweep caught.

**Stays untouched**: `StageExpiryEvaluator` Revisión/Facturación arms, the reviewer/signing countdown banners, `StageEnteredAt`/`ResetStageState()` (still anchors the Revisión stage after submit).

## D5 — Drop `SolicitudWindowDays` via the established column-drop pattern

**Decision**: Remove `[SolicitudWindowDays]` from `dbo.Processes.sql`, its EF mapping (`ProcessConfiguration.cs:29`), the domain property + switch arms (`Process.cs:32,130,150`), the `IProcessQueryService` DTO field (`:45`), the `StageExpiryEvaluator` projection/arm (`:131,140`), `ProcessService.GetDetailAsync` projection (`:346`), and the admin "Solicitud" stage-override option/summary in `Details.cshtml`. Add an idempotent `PostDeployment` drop script mirroring `06_DropLegacySupplierComplianceColumns.sql` (check `COL_LENGTH`, drop default constraint if any, `DROP COLUMN`).

**Confirmation of safety**: The full grep (planning sweep §4) shows the only behavioral readers are the submission gate (removed in D4) and the StageExpiry Solicitud arm (removed). Remaining references are the column def, EF map, DTO, and tests — all updated here. `OverrideStageWindow(StageKind.Solicitud, …)` admin path is dropped (only Revisión/Facturación remain overridable).

**Alternatives rejected**: Leaving the column dormant (carries a dead admin control + DTO field + confusing "Solicitud window" UI that no longer gates anything).

## D6 — `ProcessEvent` schema shape (general, reception-only behavior)

**Decision**: One table `dbo.ProcessEvents` with `EventType TINYINT` (`ReceptionWindow=0`, `Informational=1`, `Deadline=2`, `Milestone=3` — only `ReceptionWindow` has behavior), `ControlsSubmissionAvailability BIT`, `Name`, `Description NULL`, `StartUtc DATETIMEOFFSET(0)`, `EndUtc DATETIMEOFFSET(0)`, `ApplicantFacingMessage NULL`, `IsActive BIT`, `DisplayOrder INT`, audit columns, `RowVersion`. FK→`Processes` `ON DELETE NO ACTION`. Index `IX_ProcessEvents_ProcessId`. EF mapping mirrors `FundConfiguration` (incl. `HasConversion<byte>()` on the TINYINT enum — the spec-040 InMemory-vs-SQL gotcha).

**Rationale**: Satisfies US5 (future informational/milestone events need no reshape) while keeping this slice's behavior to the reception type. `HasConversion<byte>()` is mandatory — prior specs (035/040) hit `Byte→Int32` materialization failures that InMemory hid and E2E caught.

**No overlap/unique constraint**: Overlapping windows are allowed (FR-003 union semantics), so no unique index on dates. `end > start` is enforced in the domain factory + service, surfaced as an es-CR validation message (EF/SQL won't enforce it).

## D7 — Admin CRUD mirrors the Process service/controller pattern

**Decision**: `IReceptionWindowService` (Application) + `ReceptionWindowService` (Infrastructure) with `Create/Update/SetActive/Delete` commands, two-SaveChanges audit discipline (mirrors `ProcessService.RenameAsync` at `:125–155`). Audit kinds use the **`process.` prefix** (`process.reception_window.created/updated/activated/deactivated/deleted`) so they route through the existing `process.` branch in `AdminAuditEventWriter` (→ `TargetTypeProcess`) with **no new target type**. Admin UI is a new "Ventanas de recepción" card on `Views/Admin/Processes/Details.cshtml`, rendered for Active **and** Closed processes (config is allowed anytime), following the spec-030 Rename / spec-029 ChangeFund inline-card pattern, re-rendered through `BuildDetailsViewModelAsync`.

**Rationale**: Direct reuse of three shipped patterns (FundService CRUD, ProcessService audit, Process Details inline cards). Routing audit under `process.` avoids touching `AdminAuditEventWriter`'s target-type switch.

## D8 — Applicant notice replaces the Solicitud countdown on the draft editor

**Decision**: New `_ReceptionWindowNotice.cshtml` partial + `ReceptionWindowNoticeViewModel` (states: `Open` with remaining-time, `Upcoming` with next-open instant + "puede preparar un borrador", `Closed`, `Unrestricted`→render nothing). Rendered at the top of `Views/Application/Create.cshtml` and `Views/Application/Edit.cshtml`. On `Edit.cshtml`, it **replaces** the Solicitud-stage `_StageCountdownBanner` (built at `ApplicationController.cs:759`) — that Solicitud banner becomes meaningless once the duration gate is gone. The Revisión/Facturación uses of `_StageCountdownBanner` (Review.cshtml, SigningInbox, reviewer queue) are untouched.

**Countdown rendering**: Server computes the boundary instant + remaining `TimeSpan` into the ViewModel (pure-render view, mirroring `StageCountdownBannerViewModel`). A small client tick (optional, plan-time) may update the remaining display; the authoritative data is server-rendered, and every server action re-evaluates (no stale-client trust — spec edge case).

## D9 — New-draft creation guard placement

**Decision (FR-014)**: In `ApplicationController.Create` POST, after Group/Company validation and before `CreateApplicationAsync` (`:147` area), evaluate the selected Group's Process reception windows. If `AllWindowsClosed` → `ModelState` error on `GroupId` with the es-CR "no upcoming windows" message and re-render. `Unrestricted`, `Open`, `Upcoming`, and `BetweenWindows` all permit creation (a future window still gives a submission chance). Existing-draft editing has no such guard (FR-015).

**Rationale**: Same controller-boundary guard pattern the create flow already uses for Group/Company eligibility; reuses the D3 evaluation over the windows loaded for the chosen Group's Process.

## Open follow-ups (to confirm during implementation)

- Confirm `OverrideStageWindowAsync`/`ProcessStageWindowOverridden` audit + the admin stage-override card cleanly lose only the Solicitud option (Revisión/Facturación stay) without breaking `OverrideStageWindowCommand` callers.
- Confirm the three `StageWindowClosedException` integration/unit tests (`SubmitGuardTests`, `AutosaveEndpointTests`, `ApplicationSubmitGuardTests`) are rewritten to the reception-window model rather than deleted wholesale (preserve the boundary-second assertion as SC-002).
