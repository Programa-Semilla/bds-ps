# Research & Decisions: 037-applicant-companies

Phase 0 output. Resolves the spec's deferred HOW items and pins the patterns each change mirrors. All file references verified against the codebase on 2026-06-17.

---

## D1 — Company aggregate placement & shape

**Decision**: New `Company` domain aggregate in `src/FundingPlatform.Domain/Entities/Company.cs`, owned by an `Applicant` (FK `ApplicantId`). Single business attribute `Name`; lifecycle via nullable `ArchivedAt` (active ⇔ `ArchivedAt is null`). `CreatedAt`/`UpdatedAt` timestamps + `RowVersion`.

**Invariants (entity-level)**: constructor + `Rename(name)` trim, reject null/empty/whitespace, enforce ≤200 chars (matches the `Applications.CompanyName` snapshot column width). `Archive()` sets `ArchivedAt = UtcNow`; `Unarchive()` clears it; `IsActive => ArchivedAt is null`.

**Rationale**: Mirrors the `Fund` (spec 029) and `FundsUsageEvidence` (spec 036) aggregate style. Name length 200 (not 100) so the per-application snapshot never truncates.

**Alternatives rejected**: a value object (companies have identity + lifecycle); attaching name attributes now (YAGNI — spec is name-only).

---

## D2 — Historical preservation: snapshot + reference (reuse `CompanyName`)

**Decision**: Keep `Applications.CompanyName` (NVARCHAR(200), spec 018) as a **frozen name snapshot**; add nullable `Applications.CompanyId` (FK → Companies, NO ACTION). At creation the service copies `Company.Name` into the snapshot and sets `CompanyId`. Draft re-select re-copies the snapshot; submission freezes it (existing `EnsureNotFrozen` gate).

**Rationale**: Satisfies FR-016/FR-017 with zero versioning, reuses an existing column, and keeps every read surface (Details/Review/Index/FundingAgreement PDF) working unchanged because they already render `CompanyName`. Renaming a `Company` never rewrites prior applications' snapshots.

**Alternatives rejected**: pure FK with live name resolution (fails FR-016 — renames would retroactively rewrite history); company versioning (overkill).

---

## D3 — Per-applicant active-name uniqueness (FR-003)

**Decision**: Enforce uniqueness among an applicant's **active** companies two ways: (1) an **app-level normalized pre-check** in `CompanyAdministrationService` (and in `UserAdministrationService` create/batch attach) using the spec-031 normalization (NFD decompose + combining-mark strip + `toLocaleLowerCase('es')` equivalent in C#) for the friendly es-CR duplicate message; (2) a **filtered unique index backstop** `UX_Companies_ApplicantId_Name ON [dbo].[Companies]([ApplicantId],[Name]) WHERE [ArchivedAt] IS NULL` catching exact/case-dup races (`DbUpdateException` → es-CR), mirroring spec-029/030 (`UX_Funds_Name`/`UX_Processes_Name`).

**Nuance**: The DB index gives case-insensitivity via the column collation; **accent-insensitivity** is provided by the app-level pre-check (the index alone is accent-sensitive under CI_AS). This matches the codebase precedent where the duplicate-race path is **E2E-only** (EF InMemory doesn't enforce the filtered index).

**Edge**: Unarchiving a company whose name now collides with an existing active company is blocked with the same duplicate message (checked in `UnarchiveAsync`).

**Alternatives rejected**: persisted computed `NameNormalized` column + unique index (more robust accent handling, but heavier; not warranted vs. the app pre-check given the established precedent).

---

## D4 — Admin management write paths: two seams

**Decision**:
- **At user creation** (and batch): `UserAdministrationService.CreateUserAsync` attaches `Company` rows in the **same `SaveChangesAsync`** as the `Applicant` row. `CreateUserRequest` gains `IReadOnlyList<string> CompanyNames`. This respects the retrying-execution-strategy constraint (no raw `BeginTransactionAsync`; single SaveChanges) — the spec-036 gotcha.
- **Post-creation** (add / rename / archive / unarchive): a dedicated `ICompanyAdministrationService` (`CompanyAdministrationService` in Infrastructure, folding DB access in, mirroring `FundService`). Each method: validate → mutate → write `AdminAuditEvent` → `SaveChangesAsync`.

**Rationale**: Create-time attach must co-commit with the Applicant; post-creation actions are independent admin mutations that fit the Fund-style service. Keeps `UserAdministrationService` from growing four more verbs.

---

## D5 — Last-active-company floor (FR-008) lives in the service

**Decision**: `CompanyAdministrationService.ArchiveAsync` counts the applicant's **other** active companies; if zero, it refuses with an es-CR message ("No puede archivar la única empresa activa del solicitante."). This is a cross-aggregate rule the `Company` entity cannot see in isolation, so it is a justified service-level invariant (documented in plan Complexity Tracking note).

---

## D6 — Applicant selection rendering: reuse the spec-029 `0/1/many` pattern

**Decision**: `CreateApplicationViewModel` gains company-selection fields mirroring the existing `GroupId` anchor exactly:
- `int? CompanyId` (`[Required]`, es-CR message), `IReadOnlyList<SelectListItem> Companies`, `bool HasNoCompanies`, `bool IsSingleCompany`.
- `Create.cshtml`: when `IsSingleCompany` → hidden `CompanyId` + disabled read-only text box (auto-select, FR-012); else → `<select asp-for="CompanyId" asp-items="Model.Companies" data-searchable>` with placeholder `— Seleccione una empresa —` (FR-013); when `HasNoCompanies` → block with an es-CR message directing to an admin (FR-014) and no submit.
- `ApplicationController.Create` GET/POST resolve the applicant's **active** companies (new `ResolveActiveCompaniesAsync(userId)` helper, mirroring `ResolveEligibleGroupsAsync`), validate the posted `CompanyId` ∈ that set server-side (FR-018/019), and re-populate on redisplay.

**Rationale**: One proven, already-tested interaction pattern for both the Group anchor and the company; same `data-searchable` enhancer; same tamper-defense shape.

---

## D7 — Command/service threading

**Decision**: `CreateApplicationCommand` changes from `(ApplicantId, CompanyName, GroupId)` to `(ApplicantId, CompanyId, GroupId)`. `ApplicationService.CreateApplicationAsync` resolves the `Company` via a new `ICompanyRepository.GetActiveByIdForApplicantAsync(companyId, applicantId)`; if null → `UserFacingError` (`CompanyRequired`/new `CompanyInvalid` code) without disclosure. On success it constructs the application with both id + name snapshot.

**Domain ctor change**: `Application(int applicantId, int groupId, int companyId, string companyNameSnapshot)` (was `(applicantId, groupId, companyName)`). `SetCompany(int companyId, string nameSnapshot)` replaces the applicant-facing `SetCompanyName` path. **Ripple**: unit/integration tests and any other `new Application(...)` / `SetCompanyName` call sites must update — captured as a foundational task. (`SetCompanyName` may remain as an internal snapshot setter used by `SetCompany`, or be inlined.)

---

## D8 — Draft re-select via autosave (FR-015/FR-016)

**Decision**: Replace the `Edit.cshtml` free-text company input with a `<select data-searchable>` of the applicant's active companies, autosaved under a **new field-key `"CompanyId"`** (the `"CompanyName"` field-key is removed — free text is no longer allowed). `AutosaveFieldHandler.ApplyFieldMutation` gains a `"CompanyId"` case that: parses the id, validates it belongs to the current applicant and is active (query `Companies` by `(id, applicantId, ArchivedAt IS NULL)`), then calls `application.SetCompany(id, company.Name)` (re-copies snapshot). Ownership/etag/stage-window guards already wrap the mutation. The handler needs the company lookup — inject `ICompanyRepository` (or query `_db.Companies` directly, consistent with its current direct-`_db` style).

**FR-020 (archived-while-draft)**: at submit, `Application.Submit()`/the submit path validates the linked `CompanyId` is still active; if archived, block with an es-CR message requiring re-selection. The dropdown already excludes archived companies, so the applicant simply re-picks.

---

## D9 — Migration safety: nullable FK, no anchor script needed

**Decision**: `dbo.Companies` is a brand-new table (no migration concern). `Applications.CompanyId` is **nullable**, so the column + `FK_Applications_Companies` (NO ACTION) + `IX_Applications_CompanyId` can be added **inline** in `dbo.Applications.sql` — adding a nullable FK to a populated table publishes cleanly. **No `06_*Anchors.sql` post-deploy backfill is required** (unlike the spec-029 NOT NULL `GroupId` anchor). Greenfield: pre-existing rows keep `CompanyId = NULL` + their snapshot.

---

## D10 — Audit events (`company.*`)

**Decision**: Add to `AdminAuditEvent`: `company.create`, `company.rename`, `company.archive`, `company.unarchive`, and `TargetTypeCompany = "company"`. Add the `company.` prefix branch to `AdminAuditEventWriter.DeriveTarget`. Payload JSON: create `{companyId, applicantId, name}`; rename `{companyId, oldName, newName}`; archive/unarchive `{companyId, name}`. No-op rename (equal after trim) writes no audit (mirrors `process.renamed`).

---

## D11 — Batch CSV column (FR-009)

**Decision**: Append `"Nombre de la empresa"` to `BatchUserCsvColumns.Ordered` (Count 10→11) as the **last** column; add `NombreEmpresa` to `BatchUserImportRow`; parse `Cell(cells,10)` in the controller; add the value to the template example row; per-row validation = required cell + trim + ≤200 (es-CR reasons `CompanyNameBlank`/`CompanyNameTooLong` in `BatchUserRowReasons`). Each created row attaches exactly one company via `CreateUserRequest.CompanyNames = [name]`. `HeaderMatches`/normalization already handle the new column generically.

**Note**: in-row dedupe is N/A (one company per row; each row is a distinct new applicant).

---

## D12 — Admin UI placement (spec Open Question resolved)

**Decision**: No new top-level surface. **At creation**: repeatable company name inputs on `Create.cshtml`, shown only for the Solicitante role via the existing role-toggle JS (`companiesField` block, ≥1 required). **Post-creation management**: an "Empresas" card on the user **Edit** page listing the applicant's companies with rename / archive / unarchive actions + an "Agregar empresa" form, posting to sub-routes under `/Admin/Users/{id}/Companies/...`.

**Rationale**: Keeps everything on the existing admin user surfaces (simplest; matches LegalId/UserCode role-gated fields); avoids a separate navigation entry.

---

## D13 — es-CR copy & resources

**Decision**: New `AdminCompaniesResources` (Web.Resources) for admin-side labels/messages; applicant-side strings follow the existing inline es-CR pattern on `Create.cshtml`/`CreateApplicationViewModel` (e.g. `Debe seleccionar una empresa.`, placeholder `— Seleccione una empresa —`). Batch reasons live in `Application` (`BatchUserRowReasons`) per the spec-034 Clean-Architecture deviation. No English-only copy.

---

## Open items deferred to implementation (non-blocking)

- Exact `UserFacingErrorCode` name for an invalid/forbidden company selection (`CompanyInvalid` vs reuse `CompanyNameRequired`) — pick during implementation; both map to es-CR.
- Whether `Application.SetCompanyName` is kept as a private snapshot helper or fully inlined into `SetCompany` — implementation detail.
- Whether the Edit-page company card and the create-page repeatable inputs share a partial — DRY opportunity, decide while building.
