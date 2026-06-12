# Implementation Plan: Admin-only user provisioning + unique applicant User Code

**Branch**: `032-admin-user-code` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/032-admin-user-code/spec.md`

## Summary

Close public self-registration (delete the `/Account/Register` actions/view/VM → native 404; repoint the home hero CTA to Login and drop the login-page register link). Add a new, nullable, ≤50-char, **unique** `UserCode` to the `Applicant` aggregate — required for the Solicitante role at the controller boundary, enforced unique by a filtered unique index plus a service pre-check, shown/edited only for Solicitante on the admin create/edit forms, and read-only on the applicant profile. Widen the existing single search box on five read surfaces (admin users list, reviewer queue + row-refresh, and the Applications/Applicants/Aging reports + applicants CSV) to also match the applicant's `LegalId` and `UserCode` (and email on the reviewer queue), and surface a "Código de usuario" column on the admin users list and applicants report/CSV. es-CR throughout; no new managed dependencies.

## Technical Context

**Language/Version**: C# / .NET 10.0, ASP.NET MVC, EF Core 10
**Primary Dependencies**: ASP.NET Identity, .NET Aspire, Syncfusion (unaffected), Tabler.io (vendored). **No new NuGet/CDN deps.**
**Storage**: SQL Server via the `FundingPlatform.Database` dacpac (schema source of truth); EF Core for data access only.
**Testing**: Playwright E2E (NUnit + AspireFixture), xUnit/NUnit Unit, Integration against a real SQL Server.
**Target Platform**: Linux container (Aspire-orchestrated).
**Project Type**: Server-rendered web app (Clean Architecture: Domain / Application / Infrastructure / Web).
**Performance Goals**: No regression; search predicates stay single-query (correlated `EXISTS` on the admin list; `LIKE` ORs elsewhere). N/A new perf targets.
**Constraints**: es-CR copy only; vendored assets only; schema via dacpac (no EF migrations); migration-safe column add (nullable, no backfill).
**Scale/Scope**: ~1 column + 1 index; ~3 DTOs, ~3 view models; 5 search blocks; 2 admin views + JS toggle; 1 profile view; 2 resource classes; register removal across 1 controller + 3 views + 1 VM delete.

## Constitution Check

*GATE: must pass before Phase 0 and again after Phase 1 design.*

| Principle | Assessment | Status |
|-----------|------------|--------|
| **I. Clean Architecture** | `UserCode` on Domain `Applicant`; DTOs in Application; persistence/config + service in Infrastructure; VM/views/controller in Web. Dependencies point inward. | PASS |
| **II. Rich Domain Model** | `UserCode` set/cleared through `Applicant` ctor + `UpdateProfile()` with trim/length guards in the entity. Cross-row uniqueness lives in the service + DB index — identical to the existing `LegalId` treatment (not a new anemic leak). | PASS |
| **III. E2E (NON-NEGOTIABLE)** | Each user story gets Playwright coverage: registration-404 + no links; admin create/edit required/unique/role-toggle; widened search per surface; profile read-only. Filtered-suite gate per CLAUDE.md. | PASS |
| **IV. Schema-First** | Column + filtered unique index added to `dbo.Applicants.sql`; EF config mirrors it; no EF migration, no `EnsureCreated`. Nullable add = migration-safe, no post-deploy script. | PASS |
| **V. Spec-Driven** | spec → plan (this) → tasks → implement; stories independently testable/deliverable (registration close, code assignment, search widening). | PASS |
| **VI. Simplicity / YAGNI** | Reuses existing patterns (filtered index, service uniqueness guard, role-driven field toggle, `LIKE`-OR search). No format validation, backfill, or bulk import (deferred in spec). | PASS |

**Initial Constitution Check: PASS** (no violations → Complexity Tracking empty).
**Post-Design Re-check: PASS** (design introduces no new projects, dependencies, or abstractions; see Complexity Tracking).

## Project Structure

### Documentation (this feature)

```text
specs/032-admin-user-code/
├── spec.md              # /speckit-specify
├── plan.md              # this file
├── research.md          # Phase 0 — D1..D8 decisions
├── data-model.md        # Phase 1 — Applicant.UserCode, dacpac, DTOs/VMs
├── contracts/
│   └── contracts.md     # Phase 1 — routes, form, search, profile contracts
├── quickstart.md        # Phase 1 — run + smoke + test gates
├── review_brief.md      # reviewer guide
├── REVIEW-SPEC.md       # spec review (SOUND)
├── checklists/requirements.md
└── tasks.md             # Phase 2 — /speckit-tasks (NOT created here)
```

### Source Code (touch points)

```text
src/FundingPlatform.Domain/
└── Entities/Applicant.cs                      # + UserCode prop, ctor param, UpdateProfile param, guard

src/FundingPlatform.Application/
└── Admin/Users/DTOs/
    ├── CreateUserRequest.cs                    # + UserCode
    ├── UpdateUserRequest.cs                    # + UserCode
    └── UserDetailDto.cs                        # + UserCode
    # + UserCode field on the Applicants report row DTO (report projection)

src/FundingPlatform.Infrastructure/
├── Persistence/Configurations/ApplicantConfiguration.cs   # + property + filtered unique index
├── Identity/UserAdministrationService.cs                  # thread UserCode into Create/Update; uniqueness pre-check; widen ListUsersAsync search; map UserCode in UserDetailDto
├── Persistence/Repositories/ApplicationRepository.cs      # reviewer-queue search: + UserCode + Email
└── Persistence/Reports/ReportQueryService.cs              # 3 search blocks: + UserCode; Applicants projection: + UserCode column

src/FundingPlatform.Database/
└── Tables/dbo.Applicants.sql                  # + [UserCode] NVARCHAR(50) NULL + UX_Applicants_UserCode filtered index

src/FundingPlatform.Web/
├── Controllers/AccountController.cs           # DELETE Register GET+POST; profile VM gets UserCode
├── Controllers/Admin/AdminUsersController.cs  # required-for-Solicitante ModelState; map VM↔request; DbUpdateException→es-CR
├── ViewModels/Admin/AdminUserCreateViewModel.cs   # + UserCode [StringLength(50)]
├── ViewModels/Admin/AdminUserEditViewModel.cs     # + UserCode [StringLength(50)]
├── ViewModels/ProfileViewModel.cs             # + UserCode
├── ViewModels/RegisterViewModel.cs            # DELETE (dead)
├── Resources/AdminUsersResources.cs           # + UserCode label/required/in-use; search placeholder
├── Resources/ReviewerQueueResources.cs        # search placeholder
├── Views/Account/Register.cshtml              # DELETE
├── Views/Account/Login.cshtml                 # remove register link
├── Views/Account/Profile.cshtml               # + read-only Código de usuario field
├── Views/Home/Index.cshtml                    # hero CTA → Login
├── Views/Admin/Users/Create.cshtml            # + UserCode field + JS show/hide on role
├── Views/Admin/Users/Edit.cshtml              # + UserCode field + JS show/hide on role
├── Views/Admin/Users/Index.cshtml             # + Código de usuario column
└── Views/Admin/Reports/Applicants.cshtml      # + Código de usuario column (+ CSV writer header/row)

tests/
├── FundingPlatform.Tests.Unit/                # Applicant.UserCode guard
├── FundingPlatform.Tests.Integration/         # service uniqueness pre-check; each widened search predicate (real DB)
└── FundingPlatform.Tests.E2E/                 # registration-404; admin create/edit; per-surface search; profile
```

**Structure Decision**: Existing four-layer Clean Architecture solution (`FundingPlatform.slnx`). The change is additive within current files plus targeted deletions for register removal; no new projects or folders beyond the spec doc set.

## Complexity Tracking

> No Constitution violations — table intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |

## Notes for `/speckit-tasks`

- Order by user story: **US1 register-removal** (independent, ship-first), **US2 UserCode assignment** (domain→dacpac→DTO→service→VM→views→resources), **US3 search widening** (5 blocks + 2 column surfacings).
- Schema task (dacpac column+index) and the EF-config mirror must precede service/search tasks that reference `UserCode`.
- E2E tasks per story; integration tasks for the service uniqueness pre-check and each search predicate; unit task for the entity guard.
- Honor the SC-001 sweep: a grep task for residual `Register` references (`asp-action="Register"`, `Url.Action("Register"`, `/Account/Register`).
- Ephemeral seed: `applicant@programa-semilla.test` will have `UserCode = NULL`; US2/US3 E2E should seed/assign a known code (and clean up) so uniqueness and search assertions are deterministic.
