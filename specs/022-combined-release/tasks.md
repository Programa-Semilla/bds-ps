# Tasks: 022 — Combined Release Merge

Phase-ordered checklist for merging `020-ai-quote-comparison` + `feature/notifications` into `combined/020-ai-compare-plus-021-notifications`. Each task references the plan section that motivates it.

Legend: `[ ]` pending · `[~]` in progress · `[x]` done · `[!]` blocked

---

## Phase 0 — Approval gate

- [ ] **T001** User reviews `plan.md` + this file and approves before any merge command runs.

## Phase 1 — Pre-flight

- [ ] **T002** `git fetch origin --prune` — capture latest refs.
- [ ] **T003** Record baseline SHAs: `origin/main`, `020-ai-quote-comparison`, `feature/notifications`. Append to plan.md "Conflict-resolution log".
- [ ] **T004** Confirm working tree clean on `main` worktree (`/mnt/D/repos/bds-ps`). Untracked snapshot PNGs + `publish-dacpac-azure.sh` either stashed, committed separately, or left untouched (they're not part of the merge).

## Phase 2 — Workspace

- [ ] **T005** Create new worktree `/mnt/D/repos/bds-ps-combined` checked out to a new branch `combined/020-ai-compare-plus-021-notifications` based on `origin/main`. Use `git worktree add`.
- [ ] **T006** Inside the new worktree, verify `git status` clean and `git log --oneline -1` matches `origin/main` HEAD.

## Phase 3 — Merge 020

- [ ] **T007** From the new worktree: `git merge --no-ff 020-ai-quote-comparison -m "Merge 020-ai-quote-comparison into combined release"`. Expected: clean merge — zero file overlap vs current main.
- [ ] **T008** Sanity build: `dotnet build FundingPlatform.slnx`. Halt on failure.

## Phase 4 — Merge feature/notifications

- [ ] **T009** `git merge --no-ff feature/notifications -m "Merge feature/notifications into combined release"`. Expected: conflicts on 7 files per plan conflict map.
- [ ] **T010** Inspect conflicts list. If any conflict appears OUTSIDE the 7 mapped files → pause, surface to user, do NOT auto-resolve.

## Phase 5 — Resolve trivial / additive overlaps

- [ ] **T011** `src/FundingPlatform.Infrastructure/Persistence/AppDbContext.cs` — union DbSet block (020 ComparisonArtifacts/Jobs + 021 NotificationOutbox/Deliveries) and add the 021 `using` line.
- [ ] **T012** `src/FundingPlatform.Infrastructure/FundingPlatform.Infrastructure.csproj` — union `<PackageReference>` entries (`Anthropic.SDK 5.10.0` + `MailKit 3.6.0` with comment).
- [ ] **T013** `CLAUDE.md` — union Active Technologies + Recent Changes entries; keep both 020 + 021 sections.

## Phase 6 — Resolve structural overlaps

- [ ] **T014** `src/FundingPlatform.AppHost/AppHost.cs` — assemble in this order:
  1. 020's AI knobs `var` block (Provider/ApiKey/ExtractModel/CompareModel/BaseUrl/Concurrency/PollInterval/SyncHardTimeout/RateLimit/TokenCap/OrphanReap/PromptVersion/SchemaVersion).
  2. 021's sentinel-email comment update (`admin@programa-semilla.test`).
  3. 021's Mailgun publish-mode fail-fast `if` block.
  4. `var webApp = builder.AddProject<...>` with: existing chain → 020's `AiComparison__*` env entries → terminator.
  5. 020's conditional `webApp.WithEnvironment("AiComparison__Anthropic__ApiKey"/`BaseUrl`)` guards.
  6. 021's `!IsPublishMode` smtp4dev sidecar block with `Notifications__Mailtrap__Host/Port` bindings.
- [ ] **T015** `src/FundingPlatform.Application/Services/ReviewService.cs` — combined constructor accepts ALL four deps (`IApplicationRepository`, `INotificationOutboxWriter`, `IWorkflowTransactionScope`, `ILogger<ReviewService>`). Retain 020's `GetApplicationIdForItemAsync` method intact. Retain 021's modifications to `SendBack` + `Finalize` (vhRow capture + outbox enqueue + outcome derivation in Finalize).

## Phase 7 — Resolve doc / numbering overlaps

- [ ] **T016** Renumber notif brainstorm file: `git mv brainstorm/18-email-notifications.md brainstorm/19-email-notifications.md`. Verify no other doc references the old `18-email-notifications.md` filename.
- [ ] **T017** Rebuild `brainstorm/00-overview.md`: sessions table with 18 = ai-quote-comparison, 19 = email-notifications; aggregate Open Threads from both; aggregate Parked Ideas (if any).
- [ ] **T018** `.specify/feature.json` — set `feature_directory` to `specs/022-combined-release`.
- [ ] **T019** Update `specs/020-ai-quote-comparison/quickstart.md` lines 39 + 43: replace `admin@FundingPlatform.com` and `reviewer@FundingPlatform.com` with `@programa-semilla.test` equivalents to match the 021 seed rename.

## Phase 8 — Verify resolution

- [ ] **T020** `git diff --check` — confirm no remaining conflict markers.
- [ ] **T021** Append a Conflict-Resolution Log section to `plan.md` recording each of T011–T019 with one-line rationale per file. (Audit trail for PR reviewers.)
- [ ] **T022** `git status` clean except staged merge resolutions. `git diff --staged --shortstat` for record.

## Phase 9 — Build + test (delivery bar)

- [ ] **T023** `dotnet build FundingPlatform.slnx` clean. Halt on any error.
- [ ] **T024** `dotnet test tests/FundingPlatform.Tests.Unit` green.
- [ ] **T025** `dotnet test tests/FundingPlatform.Tests.Integration` green.
- [ ] **T026** `dotnet test tests/FundingPlatform.Tests.E2E` green — full suite. Standing delivery bar. No exceptions.

## Phase 10 — Finalize merge commit + push

- [ ] **T027** Confirm merge commit messages reflect both spec numbers + STAMP/REVIEW-CODE references. Amend only if needed.
- [ ] **T028** `git push -u origin combined/020-ai-compare-plus-021-notifications`.

## Phase 11 — PR

- [ ] **T029** Open PR vs `main` titled `Combined release: 020 AI quote comparison + 021 email notifications`. Body lists both specs with paths, the conflict-resolution log file path, and the E2E green confirmation.
- [ ] **T030** Return PR URL to user. Mark this plan complete.

---

**Total: 30 tasks. Phase 0 is the approval gate — nothing past T001 runs without user sign-off.**
