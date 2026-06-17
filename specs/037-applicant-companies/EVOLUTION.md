# Evolution notes: 037-applicant-companies

Deviations and decisions that emerged during implementation (vs. plan/research/tasks).

## D-1 — `Application` ctor `companyId` is `int?` (not `int`)
data-model.md/T005 specify the ctor as `Application(int applicantId, int groupId, int companyId, string companyNameSnapshot)`.
Implemented as `int? companyId`. Rationale: `Applications.CompanyId` is a **nullable** FK (greenfield, D9), and ~100 test
builders + pre-037 rows legitimately construct applications with no company. A non-nullable `companyId` would force a bogus
FK value (→ FK violations on persist in integration tests). The production applicant-create path always passes a real,
ownership-validated id, so the nullable parameter is purely a test/legacy ergonomic. The ctor parameter is named `companyName`
(not `companyNameSnapshot`) to preserve the many `companyName:` named-arg test call sites.

## D-2 — `ICompanyRepository` lives in `Domain/Interfaces/` (not `Domain/Repositories/`)
tasks.md T003 names `src/FundingPlatform.Domain/Repositories/ICompanyRepository.cs`. The codebase keeps **all** repository
interfaces under `Domain/Interfaces/` (namespace `FundingPlatform.Domain.Interfaces`); the interface was placed there to
match the existing convention.

## D-3 — `SetCompanyName` made private; `SetCompany` is the public re-select path
The applicant free-text `SetCompanyName` is now a private snapshot helper. The unit tests that targeted it
(`ApplicationCompanyNameTests`) were rewritten to drive the snapshot rules through the public `SetCompany(int, string)`
(draft re-select), which routes through the same validation.

## D-4 — autosave binds `change` for `<select>` fields
`autosave.js` previously bound only `blur`. The company re-select is a `<select>` (field-key `CompanyId`), and a dropdown
choice does not reliably emit a `blur` (notably under Playwright `selectOption`). Added a `change` listener for
`tagName === 'SELECT'` so the company re-select autosaves. Harmless for the text fields (none remain on the draft form).

## D-5 — pre-existing spec-036 query-hygiene exemption added
The unit `DashboardQueriesHonorSoftDeleteTests` was already red on the branch base: spec 036 (PR #65) added two files
(`FundsUsageEvidenceService.cs`, `FundsUsageEvidenceController.cs`) that read `.Applications` by-Id without being added to the
exemption table. 036's delivery bar was filtered E2E, so the full unit hygiene sweep never ran. Added both as documented
exemptions (legitimate by-Id write/state reads) so the unit suite is green. Unrelated to spec 037; no new `.Applications`
reads were introduced by this feature.

## D-6 — E2E bootstrap (`/Account/SeedUser`) seeds two companies
The dev `SeedUser` seam creates a bare applicant; with the controlled selector, such applicants could not create
applications. It now seeds **two** active companies so (a) the ~50 legacy `CreateApplicationAsync` flows keep working,
(b) the create selector is a real multi-option `<select>` (matching the demo seed), and (c) the draft re-select autosave
path has a second company to switch to. `AdminUserCreatePage.FillAsync` auto-fills one company input for the Applicant role
(mirroring its UserCode/group auto-fill) so legacy admin-create-applicant E2E callers stay green.

## D-7 — spec-018 free-text company E2E replaced
`CompanyNameApplicationFlowTests` (free-text company name, spec 018) was deleted and replaced by
`ApplicantCompanySelectionTests` (spec 037 controlled selection). The integration `CompanyNameRequiredTests` was reworked
into company-selection validation coverage (it keeps its file name).

## Test counts (delivery)
- Unit: 624/0 (+`CompanyTests`, rewritten `ApplicationCompanyNameTests`).
- Integration: 391/0 (+`CompanyAdministrationTests`, reworked `CompanyNameRequiredTests`, +batch company tests in
  `BatchUserCreationTests`, +FR-020 archived-submit in `SubmitGuardTests`, rewired `AutosaveEndpointTests`).
- Filtered E2E (delivery bar): `ApplicantCompanySelection` 5/5, `AdminCompanyManagement` 2/2,
  `CompanyHistoryPreservation` 2/2, `BatchUserCreate` 7/7, plus regression of the affected create/admin/full-flow classes
  (US2_ApplicantE2E, SupplierLocationCascade, ApplicationFundAnchor, CascadeSearch, US4_StageExpiry, AdminUserCode,
  AdminUserLifecycle, AdminUserGroupAssignment, ApplicationSubmission, ReviewApplication, ItemManagement, SendBack,
  DraftPersistence) — all green.
