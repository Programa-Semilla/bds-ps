# Quickstart: Funds-Usage Evidence Inbox

**Feature**: 041-evidence-inbox | **Date**: 2026-06-19

## Manual verification (dev)

Run the app: `dotnet run --project src/FundingPlatform.AppHost`.

1. **Inbox reachable (US1)**: Log in as a reviewer (`reviewer@programa-semilla.test` / `Demo123!`) assigned to a group. In the sidebar, click **"Evidencia de uso de fondos"**. You land on `/Evidence` listing executed applications in active processes within your group.
2. **Open + upload (US1)**: Click a row → the application's evidence page (`/Applications/{id}/Evidence`). Upload a PDF/photo, add a note, download it back — full behavior.
3. **Empty state**: A reviewer with no qualifying applications sees the friendly es-CR empty message, not an error.
4. **Close → de-list + read-only (US2)**: As admin (`admin@programa-semilla.test` / `Sentinel123!` in E2E; configured admin in dev), close the application's Process (`/Admin/Processes/{id}` → Cerrar). Re-open `/Evidence` as the reviewer: the application is gone from the list. Open its evidence page by URL: existing files still listed and downloadable, a read-only notice is shown, and there is no upload/edit/delete control.
5. **Crafted mutation rejected (US2/SC-003)**: With the process closed, a direct POST to `…/Evidence/Upload` (or `/Note`, `/Delete`) does not mutate; you are redirected back with an es-CR "proceso cerrado" toast.
6. **Reopen restores**: If the process is reopened to Active, the application returns to the inbox and the page is read-write again.
7. **Access control (US3)**: An out-of-group reviewer and the owning applicant both get the standard 404/refusal on the evidence page and its routes, in both active and closed states; applicants never see the sidebar entry.

## E2E setup notes (`EvidenceInboxTests`)

- **Executed application**: reuse `FundingAgreementSeeder.SeedExecutedAgreementAsync` (spec 036) to fast-forward an application to `AgreementExecuted` in a group the test reviewer belongs to.
- **Reviewer/admin onboarding**: `RegisterUserAsync` (auto-assigns all groups) + `AssignRoleAsync(..., "Reviewer"|"Admin")`, per session conventions.
- **Closing the process**: drive `POST /Admin/Processes/{id}/Close` as admin (the real UI action). `AgreementExecuted` does **not** block close (verified in `ProcessService.ListBlockingActiveApplicationPublicCodesAsync`), so the seeded executed app does not prevent closure. Resolve the application's `ProcessId` via its group (seed returns enough to navigate, or query in setup).
- **Group scoping for the closed-process case**: the admin closes; assertions about de-listing/read-only run as the in-scope reviewer and as admin (D5 — admin is frozen too).

### Suggested E2E coverage

| Test | Story | Asserts |
|------|-------|---------|
| `Inbox_ListsExecutedActiveProcessApp_AndLinksToEvidence` | US1 | row visible with `data-application-number`; click → evidence page; upload succeeds |
| `Inbox_EmptyForReviewerWithNoQualifyingApps` | US1 | `evidence-inbox-empty` shown |
| `ClosedProcess_AppDeListed_AndEvidenceReadOnly` | US2 | after close: row absent; page loads; notice shown; no upload/edit/delete controls; download works |
| `ClosedProcess_DirectMutationRejected` | US2 | crafted POST upload/note/delete → no change + es-CR toast |
| `OutOfGroupReviewer_AndApplicant_Refused` | US3 | 404/refusal on page + routes (active and closed); applicant has no sidebar entry |
| `ReopenedProcess_ReappearsAndEditable` | US2 edge (optional) | reopen → row returns, page read-write |

## Layered test notes

- **Integration** (`EvidenceInboxQueryTests`, real DB): matrix over `State × Process.Status × group-overlap` — only `AgreementExecuted ∧ Active ∧ in-scope` rows return; archived-fund and soft-deleted excluded.
- **Unit** (optional, `EvidenceInboxProjectionTests`, InMemory): admin short-circuit and empty-group → empty. (Note: InMemory does not enforce filtered indexes; keep DB-specific assertions in Integration — mirrors prior specs.)

## Delivery gate

Per project convention, delivery = the **filtered E2E classes** for this change are personally executed and green (`EvidenceInbox` + any evidence/process regression touched). Full suite only if explicitly requested.
