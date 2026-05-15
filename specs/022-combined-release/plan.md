# Plan: 022 — Combined Release (020 AI Quote Comparison + 021 Email Notifications)

**Status:** Draft — awaiting user approval
**Date:** 2026-05-14
**Type:** Integration / release-merge (NOT a feature spec)
**Source of truth for behavior:** `specs/020-ai-quote-comparison/spec.md` and `specs/021-email-notifications/spec.md`. This document only governs the merge mechanics.

---

## Scope

Combine two completed feature branches into a single PR against `main`:

| Branch | Spec | Commits ahead of main | LOC vs main |
|---|---|---|---|
| `020-ai-quote-comparison` | `specs/020-ai-quote-comparison/` | 18 | +8914 / -6 |
| `feature/notifications` | `specs/021-email-notifications/` | 22 | +8685 / -69 |

Each branch already has its own STAMP / REVIEW-CODE / spec gate verdict. **This plan does NOT re-validate the features themselves.** It only validates that merging them together preserves the quality of each.

Final PR target: a new branch `combined/020-ai-compare-plus-021-notifications` cut from latest `origin/main`, both feature branches merged in, PR vs `main`.

## Out of scope

- Any behavioral change to either feature.
- Spec edits beyond doc-string reconciliation (sentinel email rename in 020 quickstart).
- Cleanup or refactor of either feature's code.
- Re-running per-feature spec/plan/code review gates (already passed upstream).

## Merge order

1. **020 first** (foundational schema additions, no shared-infra changes, +98 files all disjoint vs main).
2. **feature/notifications second** (introduces 7 file-level overlaps with the post-020 tree; conflicts are all additive or disjoint-region — see below).

Order chosen because 021 modifies `ReviewService` ctor while 020 only adds a method — putting 021 second keeps the merge resolution mostly mechanical on the 020 side and concentrates judgment on the 021-side additions.

## Conflict map

### Shared files (7 — modified by BOTH branches)

| File | Class | Resolution |
|---|---|---|
| `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs` | trivial / additive | Union DbSet block. 020 adds `ComparisonArtifacts` + `ComparisonJobs`. 021 adds `NotificationOutbox` + `NotificationDeliveries`. Plus 021 adds `using FundingPlatform.Infrastructure.Notifications.Persistence;`. |
| `src/FundingPlatform.Infrastructure/FundingPlatform.Infrastructure.csproj` | trivial / additive | Union `<PackageReference>` entries. 020 adds `Anthropic.SDK 5.10.0`. 021 adds `MailKit 3.6.0` with the 3.x-MIT pin comment. |
| `src/FundingPlatform.AppHost/AppHost.cs` | structural / disjoint regions | Combine: (a) 020's AI knobs var block; (b) 021's sentinel-email comment update (`admin@FundingPlatform.com` → `admin@programa-semilla.test`); (c) 021's Mailgun publish-mode fail-fast guard; (d) 020's `webApp.WithEnvironment(...)` chain extensions for `AiComparison__*`; (e) 020's conditional Anthropic key + base URL; (f) 021's smtp4dev sidecar with `Notifications__Mailtrap__Host/Port` binding. **No region overlaps another.** |
| `src/FundingPlatform.Application/Services/ReviewService.cs` | structural / ctor merge required | 020 adds method `GetApplicationIdForItemAsync` (purely additive). 021 modifies ctor signature (adds `INotificationOutboxWriter` + `IWorkflowTransactionScope`) and injects outbox enqueue into `SendBack` + `Finalize`. **Combined ctor must accept all four deps**: `IApplicationRepository`, `INotificationOutboxWriter`, `IWorkflowTransactionScope`, `ILogger<ReviewService>`. 020's added method stays as-is. |
| `CLAUDE.md` | docs / additive | Union "Active Technologies" entries (020 adds AI client + schemas; 021 adds MailKit + smtp4dev + outbox). Union "Recent Changes" entries. Keep both. |
| `brainstorm/00-overview.md` | docs / rebuild | Both branches added a session numbered `18`. Rebuild overview: keep 020 as `18-ai-quote-comparison.md`, renumber notif as `19-email-notifications.md` (chronologically 020 was specced first per its lower spec number). Rebuild sessions table + open threads. |
| `.specify/feature.json` | tracking file | 020 sets pointer to `specs/020-ai-quote-comparison`; 021 sets it to `specs/021-email-notifications`. On the combined branch, point it at `specs/022-combined-release/` for clarity. |

### Sentinel-email rename cleanup

021 renamed all seed emails from `@FundingPlatform.com` to `@programa-semilla.test`. Grep across files added in 020 found exactly **one residual reference**:

- `specs/020-ai-quote-comparison/quickstart.md` lines 39 and 43 — doc only, references `admin@FundingPlatform.com` and `reviewer@FundingPlatform.com`.

No 020 source file or E2E test references the old email. Update the two doc lines as part of the merge — keeps the spec accurate for anyone reading 020 quickstart after merge.

### Files modified by only ONE branch — no action needed

- 020 touched `ApplicationRepository`, `ApplicationService`-adjacent code, and added all `AiComparison/*`, `ComparisonArtifact/Job` entities, schema, controllers, views, CSS/JS, prompts, tests. ZERO overlap with notif's added files.
- notif touched `AspireFixture`, `AuthenticatedTestBase`, `MailCaptureClient`, `Identity/UserAdministrationService`, `appsettings.*`, multiple existing E2E tests (sentinel rename cascade), `ApplicationService`. ZERO overlap with 020's added files.

## Risks

1. **Sentinel-rename cascade hits 020 E2E.** Verified mitigation: grep shows only docs hit. Risk: residual ref hides in a non-grepped form (e.g., constant in a fixture). Mitigation: full E2E suite run on combined branch is the safety net.
2. **ReviewService DI registration.** Combined ctor adds two new deps. Both are registered by 021 in `NotificationsServiceCollectionExtensions`. Need to confirm registration order is intact post-merge (021 adds it; 020 didn't touch DI for ReviewService).
3. **E2E runtime balloons.** Both branches added ~10 new E2E test files each. Combined suite is bigger. Acceptable per delivery bar (full green E2E required regardless of time). No mitigation needed unless the run times out.
4. **Cross-feature interaction**: spec 021 wires email enqueue into `ReviewService.Finalize` (Approved/Rejected) and `SendBack`. Spec 020's reviewer flow ends at item-level comparison, NOT at Finalize. No accidental email firing from 020 actions. Low risk; verify by reviewing 021's `ApplicationApproved/Rejected` event triggers.
5. **`feature.json` pointer**: this file drives speckit CLI behavior on the branch. Pointing it to `022-combined-release` is informational; if downstream tooling expects an actual feature dir for invoked commands, override at invocation. Acceptable since we're not running speckit commands on this branch.
6. **Aspire smtp4dev sidecar**: gated `!IsPublishMode`. 020 added Aspire env vars and `WithReference(sqlServer)`. 021 added `WithReference(smtpEndpoint)`. Both attach to the same `webApp` builder var — order matters only for env var resolution, not for Aspire reference graph. Safe.

## Acceptance

1. `dotnet build FundingPlatform.slnx` clean.
2. Unit + integration tests green.
3. **Full E2E suite green, personally executed on combined branch.** (Standing delivery bar — no exceptions.)
4. No new compiler warnings vs. either source branch.
5. PR opened with summary linking both specs.

## Rollback

If E2E reveals an irreconcilable interaction we missed:
- Combined branch is throwaway. Discard the branch, fall back to merging each feature individually as separate PRs.
- No upstream branches are modified during this plan; only `combined/...` is created and pushed.

## Workspace

Use a fresh worktree `/mnt/D/repos/bds-ps-combined/` so the 020 + notifications worktrees remain untouched and reviewable side-by-side if questions arise during conflict resolution.

---

**Approval gate.** This plan must be approved before any merge action runs. Conflict-resolution log section will be appended below as we execute.

---

## Execution log

### Baseline SHAs (captured pre-merge, 2026-05-14)
- `origin/main`: `a2e1a85a8815dfc5f09a9530b149f7f22caf3f02`
- `020-ai-quote-comparison`: `73f62ddafd5d65ddfdf644ad6e554b79c18aa5b9`
- `feature/notifications`: `1323efde1b2700aa5793570977cf25112c3a1a4f`

Main worktree status at start: clean except untracked snapshot PNGs (spec 019 carryover), `scripts/publish-dacpac-azure.sh`, and this `specs/022-combined-release/` folder. None part of merge scope.

### Merge results

**Merge 1: 020-ai-quote-comparison.** Clean merge. No conflicts. Merge commit: `2c316af`.

**Merge 2: feature/notifications.** Git reported 7 overlapped files:
- 4 marked CONFLICT (content): `.specify/feature.json`, `CLAUDE.md`, `brainstorm/00-overview.md`, `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs`.
- 3 Auto-merging (no markers, semantically verified post-hoc): `src/FundingPlatform.AppHost/AppHost.cs`, `src/FundingPlatform.Application/Services/ReviewService.cs`, `src/FundingPlatform.Infrastructure/FundingPlatform.Infrastructure.csproj`.

### Conflict-resolution log

| File | Decision | Rationale |
|---|---|---|
| `.specify/feature.json` | Manual: `{"feature_directory":"specs/022-combined-release"}` | This combined branch's "active feature" pointer is the integration spec dir 022, not either of the source specs. |
| `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs` | Manual union of both DbSet blocks | Both branches added new DbSets after `AdminAuditEvents`. 020's ComparisonArtifacts/Jobs first, then 021's NotificationOutbox/Deliveries. 021's `using FundingPlatform.Infrastructure.Notifications.Persistence;` already in by auto-merge of imports. |
| `CLAUDE.md` Active Technologies + Recent Changes | Manual union | Kept both 020 (AI client + Anthropic.SDK) and 021 (MailKit + smtp4dev) entries. Added a 022 Recent-Changes lead entry pointing to this plan. |
| `CLAUDE.md` SPECKIT START plan pointer | Replace with `specs/022-combined-release/plan.md` | Single pointer key, picked the active integration plan over either source. |
| `brainstorm/00-overview.md` Sessions table | Add row for #18 (020) and #19 (021) | Both source branches wrote a row 18. 020 keeps 18 (lower spec number, specced first); notif renumbered to 19. |
| `brainstorm/00-overview.md` Open Threads | Manual union | Kept all 020 threads as `(from #18)`. Rewrote all 021 threads' source tags from `(from #18)` to `(from #19)`. |
| `brainstorm/00-overview.md` Closed Threads | Updated 3 entries from `Closed by #18` / `Partially closed by #18` → `#19` | The "email-channel closes the notifications subset" closure originates in 021's brainstorm, which is #19 post-renumber. |
| `brainstorm/00-overview.md` `Last updated` | `2026-05-11` → `2026-05-14` | Merge date. |
| `brainstorm/18-email-notifications.md` | `git mv` → `brainstorm/19-email-notifications.md` | Resolve the numbering collision per the overview decision. |
| `src/FundingPlatform.AppHost/AppHost.cs` | Auto-merge verified | Git correctly assembled (1) 020 AI-knobs var block, (2) 021 sentinel-email comment rename, (3) 021 Mailgun publish-mode fail-fast, (4) `var webApp = …` chain with 020 `AiComparison__*` env entries, (5) 020 conditional Anthropic `ApiKey` / `BaseUrl` guards, (6) 021 `!IsPublishMode` smtp4dev sidecar + `Notifications__Mailtrap__Host/Port` bindings. Regions were textually disjoint. |
| `src/FundingPlatform.Application/Services/ReviewService.cs` | Auto-merge verified | Combined constructor accepts all four deps (`IApplicationRepository`, `INotificationOutboxWriter`, `IWorkflowTransactionScope`, `ILogger<ReviewService>`). 020's `GetApplicationIdForItemAsync` method retained. 021's vhRow capture + outbox enqueue in `SendBack` + `Finalize` retained. |
| `src/FundingPlatform.Infrastructure/FundingPlatform.Infrastructure.csproj` | Auto-merge verified | Both `Anthropic.SDK 5.10.0` + `MailKit 3.6.0` (with v3-MIT-pin comment) present. |
| `specs/020-ai-quote-comparison/quickstart.md` L39 + L43 | Doc sweep: rename sentinel emails to `@programa-semilla.test` | 021 renamed all seed emails to the new domain; this doc was the only residual ref in 020-added files. |

