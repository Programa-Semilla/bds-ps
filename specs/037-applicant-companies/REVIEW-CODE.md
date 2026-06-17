# Code Review: 037-applicant-companies

**Spec:** [spec.md](spec.md) · **Date:** 2026-06-17 · **Reviewer:** Claude (spex-gates.review-code)

Compliance: 20/20 FRs implemented. One documented minor deviation ([FR-012](spec.md#fr-012) — single-company renders a disabled read-only field + hidden id rather than a literal visible `<select>`, per [research D6](research.md); same user-facing outcome). See [EVOLUTION.md](EVOLUTION.md) for the implementation deviations.

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation, focusing on high-level questions that need human judgment.

**Changed files:** ~119 files. Core: 1 new domain aggregate (`Company`) + 1 new admin service + 1 repository; the `Application` aggregate change + its ~100-call-site ctor ripple; 4 new/changed Web surfaces (applicant create, applicant draft, admin create, admin edit); 1 new dacpac table + 1 column; 4 new E2E classes + page objects. Tests: unit 624/0, integration 391/0, filtered E2E green.

### Understanding the changes (8 min)

- Start with [`Company.cs`](../../src/FundingPlatform.Domain/Entities/Company.cs) and [`data-model.md`](data-model.md): the new aggregate is name-only + a soft-archive lifecycle. This is the smallest part but anchors everything.
- Then [`Application.cs`](../../src/FundingPlatform.Domain/Entities/Application.cs) — the `CompanyId` (nullable) + the `CompanyName` snapshot semantics, the new ctor, and `SetCompany`. The snapshot-vs-reference split is the heart of the history-preservation behavior ([FR-016](spec.md#fr-016)).
- Question: the `Company` reference and the `CompanyName` snapshot are deliberately decoupled — is that split clearly expressed in the code and the read surfaces, or could a future maintainer accidentally "fix" the snapshot to resolve live?

### Key decisions that need your eyes (12 min)

**`Application` ctor takes `int? companyId` (not `int`)** (`Application.cs:106`, relates to [FR-002/FR-017](spec.md#fr-017))
data-model named it `int companyId`. Implemented nullable because the FK is nullable (greenfield) and ~100 test builders + pre-037 rows construct apps with no company. The production create path always passes a real, validated id.
- Question: is a nullable domain reference the right call, or should the test builders have been forced to supply a real company (heavier, but no "null company" state in the domain)?

**Two write seams for company creation** (`UserAdministrationService.AttachCompaniesAtCreationAsync` vs `CompanyAdministrationService`, [research D4](research.md))
At-creation attach co-commits with the Applicant (second `SaveChanges`, no transaction — the spec-036 retrying-execution-strategy gotcha); post-creation mutations go through the Fund-style service.
- Question: the at-creation attach silently skips invalid/duplicate names (the controller boundary guards them first). Is "controller validates, service is defensive" the right division, or should the service reject?

**Last-active floor + uniqueness live in the service, not the entity** (`CompanyAdministrationService.cs`, [research D3/D5](research.md))
These are cross-aggregate rules the `Company` entity cannot see in isolation. Accent-insensitivity is an app-level pre-check; the DB filtered index is the case/race backstop (E2E-only, like spec-029/030).
- Question: is the app-level normalized pre-check (`CompanyNameNormalizer`, NFD+strip+lower-es) acceptable given the index is accent-sensitive under CI_AS, or is a persisted normalized column warranted?

**FR-020 archived-at-submit gate placement** (`SubmitApplicationHandler.cs:65`)
The check runs before item validation, so an archived-company draft is blocked regardless of other submit-blockers. Surfaced via the controller's `InvalidOperationException` → validation-errors path.
- Question: should the archived-company message be one of *several* aggregated submit errors, or is short-circuiting (first thing the applicant must fix) the better UX here?

### Areas where I'm less certain (5 min)

- `Application.cs:248` (`SetCompanyName` made private): the spec-018 free-text path is gone, but the method survives as a snapshot helper called by the ctor + `SetCompany`. Worth confirming no other intended caller was lost.
- [FR-012](spec.md#fr-012) single-company rendering (`Views/Application/Create.cshtml`): a disabled read-only text box + hidden id, not a literal `<select>`. The outcome matches; the wording doesn't. Reviewer call on whether the literal "dropdown visible" matters.
- `autosave.js` (`change` listener for `<select>`): added so the company re-select autosaves. Confirm this doesn't double-fire saves for any other select that might later gain `data-autosave-field`.
- E2E bootstrap (`AccountController.SeedUser` seeds 2 companies; `AdminUserCreatePage.FillAsync` auto-fills one): this keeps ~50 legacy create flows green but means the test fixtures now always have companies. Confirm that doesn't mask a real zero-company regression elsewhere.

### Deviations and risks (5 min)

No deviations from [plan.md](plan.md)'s architecture were identified; the implementation-level refinements are logged in [EVOLUTION.md](EVOLUTION.md) (nullable ctor param, interface folder, private `SetCompanyName`, autosave `change`, the spec-036 hygiene-exemption fix, the E2E bootstrap company seed, and the spec-018 E2E replacement).

- `DashboardQueriesHonorSoftDeleteTests` (`tests/.../QueryHygiene`): added two **spec-036** files to the exemption table — they were a pre-existing red on the branch base, unrelated to 037. Question: is folding that fix into this PR acceptable, or should it be split out?
- The `Application` ctor ripple touched ~100 test call sites + 5 `ApplicationService` constructions via a scripted sweep. Risk: a mechanical miss. Mitigation: the compiler is the safety net (it built clean) and the full unit+integration suites are green. Question: is the scripted sweep auditable enough, or would you want the diff spot-checked?

---

## Deep Review Report

> Automated multi-perspective code review (5 internal agents + a round-2 verification pass).

**Date:** 2026-06-17 | **Rounds:** 2/3 | **Gate:** PASS

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 2 | completed |
| Architecture & Idioms | 5 | completed |
| Security | 2 | completed |
| Production Readiness | 4 | completed |
| Test Quality | 6 | completed |
| CodeRabbit (external) | — | skipped (not installed) |
| Copilot (external) | — | skipped (not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 8 | 8 | 0 |
| Minor | 8 | 1 | 7 |

### What was fixed automatically

- **FR-015 immutability** — `Application.SetCompany` now rejects re-select on a non-Draft application (the autosave window stayed open post-submit); the inverted unit test was corrected and forgery/non-Draft autosave negatives added.
- **Concurrency UX** — the unique-index race on a duplicate company name now returns the es-CR duplicate/collision message instead of a 500 (`TrySaveAsync`).
- **Create integrity** — at-creation company attach now compensates correctly (clears the failed adds, removes the orphan Applicant, then deletes the user) so FR-004 can't be left violated on a transient failure.
- **Single source of truth** — company error strings consolidated into `IUserFacingErrorTranslator`, removing the divergent `AdminCompaniesResources.ForError` table.
- **Dead code** — trimmed `ICompanyRepository` to its one used seam; removed the unused `CompanyAtLeastOneRequired` code.
- **Tests** — added FR-018/019 autosave-forgery, FR-020 unblock, archive-floor stays-active + audit-actor, and at-creation dedupe coverage.

### What still needs human attention

All Critical and Important findings were resolved. 7 Minor findings remain (see [review-findings.md](review-findings.md)) and are documented as accepted/deferred — most are defense-in-depth or cosmetic. Reviewer judgement is welcome on two:

- The archive last-active floor keeps a documented TOCTOU window (matching the existing last-active-admin floor). Is matching that precedent acceptable, or should this invariant get a serialized guard despite the InMemory-test constraint?
- Folding the spec-036 query-hygiene exemption fix into this PR (a pre-existing red, see [EVOLUTION.md](EVOLUTION.md) D-5) — acceptable, or split out?

### Recommendation

All findings addressed. Code is ready for human review with no known blockers. Verified green: unit 625/0, integration 396/0, filtered E2E 17/17.
