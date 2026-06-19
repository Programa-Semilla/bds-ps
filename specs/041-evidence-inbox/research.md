# Research: Funds-Usage Evidence Inbox

**Feature**: 041-evidence-inbox | **Date**: 2026-06-19

All decisions below resolve the spec's design space against the existing codebase. No NEEDS CLARIFICATION remained after brainstorming.

## D1 — Where the inbox lives

**Decision**: A new `EvidenceInboxController` ( `[Authorize(Roles="Reviewer,Admin")]` ) with a single `GET /Evidence` → `Index` action rendering the list. Surfaced via a new `operativoEntries` sidebar entry in `_Layout.cshtml`.

**Rationale**: The user explicitly chose a **separate sidebar entry** over a filter on the existing review queue. A dedicated controller keeps the surface single-purpose and mirrors how `ReviewerDashboardController` (`/Reviewer/Dashboard`) and the signing inbox were added. Route `/Evidence` (no application id) is distinct from the per-application `/Applications/{id}/Evidence`.

**Alternatives considered**:
- New `ReviewerFilter` tab on `/Review` — reuses queue UI but conflates executed (post-deliverable) apps with the active worklist; rejected per user choice.
- New action on `ReviewerDashboardController` — that controller is a KPI-only surface; adding a list there muddles its purpose.

## D2 — Inbox query placement & shape

**Decision**: `IEvidenceInboxProjection` interface in `Application/EvidenceInbox/`; EF implementation `EvidenceInboxProjection` in `Infrastructure/Persistence/`, returning a capped list of `EvidenceInboxRowDto`. The query applies, in one EF statement: `State == AgreementExecuted`, `Group.Process.Status == Active`, the group-overlap predicate (admin short-circuit), `ExcludeDeleted`, and `ExcludeArchivedFund`.

**Rationale**: Mirrors the two established patterns — `ReviewerDashboardProjection` (Infrastructure-resident projection returning a scalar/DTO) and `SignedUploadRepository.GetPendingInboxAsync` (DTO-returning inbox query with the same group-overlap join via `UserGroupMemberships`). Returning DTOs (not aggregates) avoids loading full Application graphs for a list view. Enforcing the predicate in-query satisfies NFR-001 (no UI-only filtering).

**Alternatives considered**:
- Reuse `IApplicationRepository.GetByStateForReviewerAsync(AgreementExecuted, …)` then filter Process status in memory — loads aggregates and filters outside the query; violates NFR-001 spirit and is heavier.

## D3 — "Process active/closed" resolution

**Decision**: Resolve via `Application.GroupId → Group.ProcessId → Process.Status`. Evaluate **live** per request (no snapshot).

**Rationale**: `Application` already exposes `Group?.Process?` navigation; the existing `Application.IsFrozen` walks `Group?.Process?.Fund?.Status`, so `Group?.Process?.Status` is the identical idiom. Live evaluation makes FR-004 (reopen restores) automatic with zero extra machinery.

## D4 — Read-only enforcement mechanism

**Decision**: On `FundsUsageEvidenceController`:
- Add a private `IsProcessClosedAsync(applicationId, ct)` (EF query of `Group.Process.Status`).
- `Index` sets `FundsUsageEvidenceIndexViewModel.IsReadOnly = closed`.
- `Upload`, `EditNote`, `Delete`: after the existing `IsAccessibleAsync` gate, if the process is closed, **do not mutate** — set an es-CR error toast and `RedirectToAction(Index)`.

**Rationale**: A server-side check on every mutation action is authoritative and covers crafted direct POSTs (FR-007, SC-003), not just hidden UI. Redirect-to-Index with a toast matches the controller's existing TempData error idiom and keeps the page reachable (no 404, FR-006). The UI controls are additionally hidden by `IsReadOnly` so honest users never see a blocked button.

**Alternatives considered**:
- Return `403/Forbid` on blocked mutation — valid but less consistent with the page's existing redirect+toast UX; redirect chosen for coherence.
- Enforce only in the view (hide controls) — rejected; fails FR-007/SC-003 (crafted POST would mutate).

## D5 — Admin behavior under a closed process

**Decision**: The read-only freeze applies to **everyone**, admins included. Admins keep their broader *visibility* (group bypass governs which applications appear and which pages they may open), but cannot upload/edit/delete once the process is closed.

**Rationale**: Matches the brainstorm decision ("read-only for pages if open" applies uniformly). Flagged in `review_brief.md` as the one decision worth stakeholder confirmation; the spec records it as an assumption.

## D6 — Process close does not block on executed apps (feature reachability)

**Decision/Finding**: `ProcessService.ListBlockingActiveApplicationPublicCodesAsync` defines blocking "active" states as `Draft, Submitted, UnderReview, AppealOpen`. `ResponseFinalized` and `AgreementExecuted` are explicitly non-blocking ("the cycle has produced its deliverable"). Therefore a process **can** be closed while it has executed applications — the read-only scenario is reachable.

**Rationale**: Confirms the feature is coherent and gives the E2E its setup path: seed an executed application, then `POST /Admin/Processes/{id}/Close` as admin, then assert de-listed + read-only.

**Observation (out of scope)**: That blocking-state list predates spec 040 and omits `PendingAudit`/`ReturnedFromAudit`, so a process could currently be closed mid-audit. Not addressed by spec 041; noted for a future spec.

## D7 — Download in read-only mode

**Decision**: `Download` stays available regardless of process status (only role + group + executed-state + evidence-ownership gates apply, unchanged from spec 036).

**Rationale**: FR-006 — read-only preserves retrieval of the captured record; "frozen", not "sealed".

## D8 — Inbox row identity & ordering

**Decision**: Each row shows the application number (`APP-{id:D5}`), applicant display name, and fund + process names; rows ordered most-recently-executed first.

**Rationale**: FR-003 needs enough to choose a row; this mirrors the signing-inbox row content. Most-recent-first matches reviewer expectation (the just-executed app is top). Ordering uses the execution/last-updated timestamp already on the aggregate.

## Reused seams (no new abstractions)

- `IReviewerScopeProvider` / `IReviewerScope` (spec 016) — group-overlap scope.
- `SignedUploadRepository.GetPendingInboxAsync` group-overlap join (`UserGroupMemberships`) — query template.
- `ReviewerDashboardProjection` — Infrastructure-resident projection placement/style.
- `FundingAgreementSeeder.SeedExecutedAgreementAsync` (spec 036 E2E) — executed-state seed.
- `POST /Admin/Processes/{id}/Close` (`AdminProcessesController` → `IProcessService.CloseAsync`) — close trigger for E2E.
- `IApplicationQueryFilter.ExcludeDeleted` / `ExcludeArchivedFund` — consistency with other reviewer reads.
