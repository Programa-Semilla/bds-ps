# Code Review: Admin-only user provisioning + unique applicant User Code

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Date:** 2026-06-11
**Reviewer:** Claude (speckit-spex-gates-review-code)

## Compliance Summary

**Overall: ~97%** — 17/18 functional requirements fully compliant; **1 minor deviation** (FR-002 POST→405, see [tasks.md D-5](tasks.md#deviations-discovered-during-implementation)). All six success criteria met; filtered E2E 30/0, Unit 527/0, Integration 358/0.

The single deviation is functional-parity (registration handler deleted → no account creatable) but a literal miss of "POST … MUST return 404." Candidate for `speckit-spex-evolve` (reword FR-002, or normalize POST→404 with a catch-all).

---

## Code Review Guide (30 minutes)

**Changed files:** ~30 source files across Domain → Application → Infrastructure → Web + dacpac, plus 6 E2E classes / 2 page objects / 2 new test classes. Three story slices (registration removal, UserCode, search widening) + one cross-cutting test-infra change.

### Understanding the changes (8 min)

Read in this order:
- `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` — the spine: the Applicant create/update branches now thread + uniqueness-check `UserCode`, and `ListUsersAsync` gains the correlated search sub-query. If this file reads cleanly, most of US2+US3 follows.
- `src/FundingPlatform.Web/Controllers/AccountController.cs` — the registration deletion + the **dev-only `SeedUser` seam** (the load-bearing test-infra decision).
- Question: the three concerns (registration / code / search) are interleaved across the same files but were committed as separate story checkpoints — does the *commit history* (b991b6a → b5e124b → 294ca90) tell a clearer story than the final diff?

### Key decisions that need your eyes (12 min)

**Dev-only `SeedUser` replaces the E2E registration bootstrap** (`AccountController.cs` `SeedUser`, [tasks.md D-2](tasks.md#deviations-discovered-during-implementation))
Removing public registration broke `RegisterUserAsync` (~103 call sites). I added a Development-gated, no-UI `GET /Account/SeedUser` mirroring the four existing dev seams, rather than routing every test through admin-create.
- Question: is a test-only user-creation seam acceptable given [FR-004](spec.md#functional-requirements) ("admin create is the sole means")? It's unreachable outside Development and has no UI — but it *is* a second creation path that exists only for tests. Reasonable, or should the bootstrap go through the real admin flow?

**`UserCode` lives on `Applicant`, required at the controller, nullable in storage** (`Applicant.cs`, `AdminUsersController.cs`, [research D1/D3](research.md#d1-where-does-usercode-live--applicant-entity-vs-aspnetusers))
- Question: required-for-role-but-nullable-in-column mirrors the existing LegalId treatment. Is that the right model, or should "required for Solicitante" be a domain invariant rather than a controller check?

**Uniqueness has two enforcement points** (`UserAdministrationService.cs` pre-check + `dbo.Applicants.sql` filtered index; `AdminUsersController.cs` `DbUpdateException` catch)
- Question: the service `AnyAsync` pre-check is the friendly path; the filtered index is the race backstop. On a true race the create-path catch leaves the just-created Identity user in place (orphan) before the index trips — acceptable for an admin-driven, vanishingly-rare race, or worth a compensating delete?

**Admin-list search is a correlated `EXISTS`** (`UserAdministrationService.cs` `ListUsersAsync`)
- Question: `_dbContext.Applicants.Any(a => a.UserId == u.Id && …)` inside a `_userManager.Users` predicate — fine at current user volumes, but is the per-row EXISTS a concern if the user table grows large? (The cascade fund/process/group filter below it is already in-memory per the existing code.)

### Areas where I'm less certain (5 min)

- `dbo.Applicants.sql` / `ApplicantConfiguration.cs` ([FR-015](spec.md#functional-requirements)): "accent-insensitive" is delivered as *whatever the DB's current collation does* (the existing `Contains`/`LIKE` behavior), not an explicit NFD fold like spec 031's JS. If the team expects deterministic accent folding, that's a larger change.
- `AdminReportsService.cs` CSV header (`"User Code"`): the existing applicants-CSV headers are English, so I matched them; the on-screen column is es-CR ("Código de usuario"). [FR-017](spec.md#functional-requirements) wants es-CR — is the English CSV header an acceptable match-the-neighbors choice or a gap?
- `AdminUserCreatePage.FillAsync` auto-filling a UserCode for Applicant ([tasks.md D-3](tasks.md#deviations-discovered-during-implementation)): this keeps ~legacy admin-create-applicant tests green, but it also means those tests no longer *prove* a human must supply a code — only the dedicated `AdminUserCodeTests` does. Is that separation acceptable?

### Deviations and risks (5 min)

- **FR-002 POST→405** (`RegistrationRemovedTests.cs`, [tasks.md D-5](tasks.md#deviations-discovered-during-implementation)): GET cleanly 404s; POST surfaces as 405 under the http→https E2E redirect because the handler is gone. Functionally registration is closed. Question: reconcile the spec wording, or force a literal 404 for POST?
- **US3 reviewer-queue / Applications-Aging coverage** ([tasks.md D-4](tasks.md#deviations-discovered-during-implementation)): match-only surfaces are E2E-covered transitively (identical `LIKE` predicate as the applicants report, which *is* E2E-tested) + the admin-list predicate is integration-tested. Question: is the transitive argument sufficient, or should a full reviewer-queue submission flow assert the code match directly?
- No deviations from [plan.md](plan.md)'s data model or layering were identified — the column/index/DTO/VM/search shape matches the plan; the deviations above are all test-infra or the FR-002 routing nuance discovered during execution.
