# Deep Review Findings

**Date:** 2026-06-17
**Branch:** 037-applicant-companies
**Rounds:** 2
**Gate Outcome:** PASS
**Invocation:** quality-gate (after /speckit-implement)

## Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 8 | 8 | 0 |
| Minor | 8 | 1 | 7 (accepted/deferred) |
| **Total** | **16** | **9** | **7** |

**Agents completed:** 5/5 (Correctness, Architecture, Security, Production Readiness, Test Quality) + 1 round-2 verification agent.
**External tools:** CodeRabbit / Copilot not installed — skipped.

## Important findings (all fixed in round 1)

### FINDING-1 — FR-015 not enforced: company mutable after submission
- **Severity:** Important · **Category:** correctness + test-quality · **Source:** correctness-agent + test-quality-agent · **Resolution:** fixed (round 1)

**What was wrong:** `Application.SetCompany` only guarded `EnsureNotFrozen()` (archived-Fund), not submission state. Because `ResetStageState()` refreshes `StageEnteredAt` at submit, the autosave stage-window is freshly open on a Submitted application, so a forged `POST …/autosave {fieldKey:"CompanyId"}` could re-point the company + snapshot — violating FR-015/FR-016. The unit test `SetCompany_AfterSubmit_Throws` asserted the *opposite* of its name (`Throws.Nothing`).

**How it was resolved:** Added a `State != Draft` guard in `Application.SetCompany` (throws `InvalidOperationException`); the autosave controller action maps it to 400. Rewrote the unit test to force `Submitted` and assert the throw + unchanged snapshot. Added integration negatives (`AutosaveEndpointTests`): cross-applicant / archived / nonexistent / non-Draft `CompanyId` all rejected.

### FINDING-2 — Concurrent duplicate company name → unhandled 500
- **Severity:** Important · **Category:** production-readiness · **Resolution:** fixed (round 1)

**What was wrong:** The add/rename/unarchive verbs pre-checked the name then `SaveChanges`; a racing duplicate trips the filtered unique index `UX_Companies_ApplicantId_Name`, and no `DbUpdateException` was caught (unlike spec-030/032), so the admin saw a 500.

**How it was resolved:** New `TrySaveAsync` catches the index collision → callers return `CompanyNameDuplicate` (add/rename) / `CompanyUnarchiveNameCollision` (unarchive), the established es-CR path.

### FINDING-3 — At-creation attach: no compensation → zero-company Solicitante
- **Severity:** Important · **Category:** production-readiness · **Resolution:** fixed (rounds 1–2)

**What was wrong:** The company attach commits in a 2nd `SaveChanges` after the Applicant is persisted; a failure left a Solicitante with zero companies (FR-004), with no rollback. Round-2 caught that a naive `DeleteAsync(user)` is FK-rejected (NO ACTION on `FK_Applicants_AspNetUsers`) while the orphan Applicant exists.

**How it was resolved:** On attach failure: `ChangeTracker.Clear()` (discard the failed company/audit adds) → remove the committed Applicant → `SaveChanges` → delete the user → rethrow. The orphan can no longer persist.

### FINDING-4 — Archive last-active floor TOCTOU
- **Severity:** Important · **Category:** production-readiness · **Resolution:** fixed-by-documentation (round 1)

**What was wrong:** The floor is a read-then-write count; two concurrent archives of an applicant's last two active companies could both pass → zero active.

**How it was resolved:** A guarded `ExecuteUpdate` was attempted but is unsupported by the InMemory provider the integration tests use. Reverted to count-then-archive and documented the TOCTOU as an accepted low-probability limitation — identical to the codebase's existing last-active-admin floor (`CountActiveNonSentinelAdminsAsync`), which carries the same theoretical race by design.

### FINDING-5 — Divergent error-message mapping (two sources of truth)
- **Severity:** Important · **Category:** architecture · **Resolution:** fixed (round 1)

**What was wrong:** `AdminCompaniesResources.ForError` and `UserFacingErrorTranslator` both mapped `CompanyNameTooLong`/`CompanyInvalid` to **different** strings — guaranteed drift.

**How it was resolved:** Consolidated to `IUserFacingErrorTranslator` as the single source of truth (added the three admin company codes with canonical es-CR strings); injected it into `AdminUsersController`; deleted `ForError` + the duplicated error constants (kept only the two ModelState strings the controller uses directly).

### FINDING-6 — `ICompanyRepository`: 6/7 members dead
- **Severity:** Important · **Category:** architecture · **Resolution:** fixed (round 1)

**What was wrong:** Only `GetActiveByIdForApplicantAsync` was called in production; the other six members were unused, with near-duplicate query bodies folded into services/controllers elsewhere.

**How it was resolved:** Trimmed the interface + impl to the single used seam, documented that other company reads are intentionally folded into services (spec-036 `FundService` style).

### FINDING-7 — Test-coverage gaps
- **Severity:** Important (aggregate) · **Category:** test-quality · **Resolution:** fixed (round 1)

Added: autosave forgery/non-Draft negatives (FR-018/019/015); FR-020 *unblock* (re-select active clears the gate); archive-floor *company-stays-active* + audit-actor assertions; at-creation case/accent dedupe test (FR-004/D4, asserts 2 companies + 2 audits).

### FINDING-8 — Dead enum `CompanyAtLeastOneRequired`
- **Severity:** Minor→folded · **Resolution:** fixed (round 1). Removed (the FR-004 rule uses a direct ModelState string).

## Remaining findings (Minor — accepted/deferred)

| # | File | Issue | Disposition |
|---|------|-------|-------------|
| A-4 | `Company.cs` | `ArchivedAt` is `DateTimeOffset?` while `CreatedAt`/`UpdatedAt` are `DateTime`; "mirrors Fund" comment imprecise | Deferred — cosmetic; changing the type touches the dacpac column. |
| A-5 | `CompanyAdministrationService.cs` | `new Company(0, name)` throwaway to reuse private name validation | Deferred — could expose a static validator; low value. |
| C-2 | `UserAdministrationService.cs` | Batch in-file dedup claims identity before the company-cell check → a company-rejected row can yield a misleading "dup-in-file" reason for a later row | Deferred — mirrors the pre-existing chain-mismatch ordering. |
| S-1 | `AdminUsersController.AddCompany` | Resolves applicant by `Applicants` row, not an explicit Applicant-role assert | Deferred — defense-in-depth; `Applicants` rows exist only for the Applicant role today. |
| S-2 | `CompanyAdministrationService` verbs | Ownership enforced in the controller (`CompanyBelongsToUserAsync`), not at the service boundary | Deferred — controller is the sole caller; defense-in-depth. |
| T-5 | `ApplicantCompanySelectionTests` | Forged-id E2E asserts the error but not "no application persisted" | Deferred — the integration `CompanyNameRequiredTests` asserts no-persist for the create path. |
| R2-1 | autosave action | Not-found PublicCode now returns 400 (was 500); 404 would be more precise | Accepted — strictly better than the pre-fix 500; generic message, no leak. |
