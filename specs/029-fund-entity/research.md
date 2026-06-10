# Research: Fund (Fondo) Entity

**Feature**: 029-fund-entity | **Date**: 2026-06-10

All `NEEDS CLARIFICATION` items resolved. Decisions below are grounded in a read of the existing codebase (citations inline) and two product-owner calls made during planning.

---

## D1 — Fund as a new Domain aggregate (not a lookup table)

- **Decision**: `Fund` is a rich Domain entity with a factory and behavior methods (`Create`, `Rename`, `EditDescription`, `Archive`, `Reactivate`, `AttachRegulation`, `ReplaceRegulation`, `RemoveRegulation`), mirroring `Process`.
- **Rationale**: Constitution II (Rich Domain Model) and the Active/Archived lifecycle require entity behavior, not an anemic row. `Process.cs` is the template (`src/FundingPlatform.Domain/Entities/Process.cs` — `Create()` factory, `Close()` guard, `RowVersion`).
- **Alternatives**: Lookup table (rejected — can't host lifecycle/regulation behavior); DB blob for the PDF (rejected — violates spec-014 "no DB blobs").

## D2 — Status enum `FundStatus { Active = 0, Archived = 1 }`

- **Decision**: New enum mirroring `ProcessStatus` (`src/FundingPlatform.Domain/Enums/ProcessStatus.cs`), persisted as `TINYINT` with `HasConversion<byte>()`.
- **Rationale**: Consistency with the existing Process lifecycle column style.

## D3 — Regulation PDF storage: spec-014 `IObjectStorage`, new `FileCategory.FundRegulation`

- **Decision**: Add `FundRegulation` to the `FileCategory` enum with `[Description("fund-regulations")]` (`src/FundingPlatform.Application/Abstractions/Storage/FileCategory.cs`). Add a `FundRegulation` property to `StorageCategoriesOptions` + wire it in the `For()` switch (`StorageOptions.cs`), default `MaxSizeBytes = 20 MiB`, `UrlExpirySeconds = 300`, `RetentionPolicy = "none"`. Decorate the upload action with `[UploadSizeGuard(FileCategory.FundRegulation)]` (`src/FundingPlatform.Web/Filters/UploadSizeGuardAttribute.cs`).
- **Serving**: Applicant download uses `IObjectStorage.ResolveServingHandleAsync(..., ServingMode.BackendStream, ...)` and `File(stream, contentType, name)` — the same pattern as `PublicLandingFilesController` / `FundingAgreementController.Download`. The application boundary is the single auth point.
- **PDF validation**: magic-byte `%PDF-` check (the strong pattern in `SignedUploadService.ValidateIntake`, `src/FundingPlatform.Application/Services/SignedUploadService.cs:592`) plus content-type `application/pdf`.
- **Reference storage**: store the blob key + metadata as columns on the `Fund` row (single optional regulation), like `FundingAgreement.BlobKey` (`dbo.FundingAgreements.sql`). Columns: `RegulationBlobKey NVARCHAR(1024) NULL`, `RegulationFileName NVARCHAR(260) NULL`, `RegulationContentType NVARCHAR(100) NULL`, `RegulationSizeBytes BIGINT NULL`, `RegulationUploadedAtUtc DATETIME2(3) NULL`, `RegulationUploadedByUserId NVARCHAR(450) NULL`.
- **Rationale**: At most one regulation per Fund → columns on the aggregate are simpler than a child table; mirrors `FundingAgreement`. `ObjectKey.Build(FileCategory.FundRegulation, ownerSegment: "admin", entityId: fundId, deterministicSuffix: <guid16>, ".pdf")`.

## D4 — `Process.FundId` required FK (pre-production, no migration)

- **Decision**: `dbo.Processes` gains `FundId INT NOT NULL` with `CONSTRAINT FK_Processes_Funds FOREIGN KEY (FundId) REFERENCES dbo.Funds(Id) ON DELETE NO ACTION` and `IX_Processes_FundId`. EF: `Fund.HasMany(f => f.Processes).WithOne(p => p.Fund).HasForeignKey(p => p.FundId).OnDelete(NoAction)` — mirrors `Process→Groups` (`ProcessConfiguration.cs`).
- **Pre-production**: system is not live (per product owner). The dacpac post-deploy seed creates a seed Fund first, then seeds Processes with `FundId`. No nullable/backfill phase.
- **Rationale**: Direct FK makes "Processes of a Fund" and "Fund of a Process" trivial and exact.

## D5 — Authoritative `Application.GroupId` anchor *(product-owner call: "Add authoritative Application→Process FK")*

- **Decision**: `dbo.Applications` gains `GroupId INT NOT NULL` with `FK_Applications_Groups` + `IX_Applications_GroupId`. The anchor is the **Group** (not Process) because: (a) it is what applicants actually have membership in; (b) Process and Fund both derive from it (`Group → Process → Fund`); (c) it makes Plantilla resolution exact.
- **Capture point**: at application creation (`ApplicationController.Create` → `ApplicationService.CreateApplicationAsync`, `CreateApplicationCommand`). `CreateApplicationViewModel` gains a `GroupId` (rendered as a Process/convocatoria selector listing the applicant's eligible groups under **Active** Funds). FR-018 rules: one eligible group → auto-select and hide the control; many → required choice; none → block with es-CR message.
- **Eligible groups** = groups the applicant's `ApplicationUser` is a member of (`UserGroupMemberships`) whose `Process.Fund.Status == Active` and `Process.Status == Active`.
- **Plantilla resolution fix**: `GetApplicationReviewProjection.ResolveMinimumQuotationsAsync` and `SubmitApplicationHandler` change from the `FirstOrDefault` membership join to a direct `application.Group.Process.Plantilla` lookup (`GetApplicationReviewProjection.cs:115`).
- **Reviewer visibility unchanged**: the group-overlap predicate in `ApplicationRepository.GetByStateForReviewerAsync` is retained (the anchored applicant is a member of the anchor group, so overlap still holds). The anchor is additive; we do not narrow reviewer visibility in this feature (Out of Scope).
- **Rationale**: removes the documented ambiguity (an applicant in groups across multiple Processes had no deterministic Process); enables exact Fund-on-reports (FR-012) and exact freeze (FR-020/021).

## D6 — Force-freeze via `IApplicationQueryFilter.ExcludeArchivedFund` + guards *(product-owner call: "Force-freeze in-flight work")*

- **Decision**: Extend `IApplicationQueryFilter` (`src/FundingPlatform.Application/Abstractions/IApplicationQueryFilter.cs`) with `IQueryable<Application> ExcludeArchivedFund(IQueryable<Application> source)`, implemented as `source.Where(a => a.Group.Process.Fund.Status != FundStatus.Archived)` (`ApplicationQueryFilter.cs`). Compose it next to `ExcludeDeleted` at every **non-admin** read site:
  - `ApplicationRepository`: `GetByApplicantIdAsync`, `GetForApplicantDashboardAsync`, `GetByStateForReviewerAsync`, `GetPendingAgreementPagedAsync`, `ApplicantSharesAnyGroupAsync`.
  - `ReviewerDashboardProjection`, `AdminDashboardCountersReader` (counters that feed reviewer-facing widgets), `StageExpiryReminderService`.
  - Admin reports (`ReportQueryService`) do **not** apply the freeze filter (admins retain visibility) but expose Fund as a filter/column.
- **Mutation guards (FR-021, defense-in-depth)**:
  - Controller boundary: an early guard (filter/helper) on `ApplicationController` (Create, Edit, AddItem, RemoveItem, Autosave, Submit, Remove/Withdraw, Impact) and `QuotationController` (Add/Edit) returns an es-CR error toast when the application's Fund is Archived.
  - Domain: `Application` exposes `bool IsFrozen` (set from a derived/loaded `Group.Process.Fund.Status`) and each mutating method throws `FundArchivedException` if frozen. Because the domain entity does not natively load the Fund, the guard is fed by the service layer (pass `fundArchived` into the relevant domain calls, or check in the service before invoking).
- **Rationale**: reuses the proven, idempotent soft-delete filter pattern; one predicate covers ~the same read sites; controller+domain double-guard matches the research recommendation and Constitution II.
- **Alternative rejected**: blocking archive until no active applications (the existing `Process.Close` semantics) — explicitly not chosen by the product owner.

## D7 — Admin Fund CRUD mirrors `AdminPlantillasController` (status + archive)

- **Decision**: New `AdminFundsController` (`/Admin/Funds`) + `FundService` (Infrastructure) + `AdminFundViewModels` + `Views/Admin/Funds/{Index,Create,Edit,Details}.cshtml` + `AdminFundsResources` (es-CR). Authorization `[Authorize(Roles = "Admin")]` + `[SupplierAdminDenied]` consistent with sibling admin controllers. Index has an Active/Archived status filter (mirrors `AdminPlantillasController` archive + `Plantillas/Index.cshtml` status badge). Archive/reactivate buttons use the spec-024 confirm dialog (`data-confirm`, `confirm-dialog.js`). Flash via `TempData["SuccessMessage"]/["ErrorMessage"]` → spec-024 toast bridge (`_NotificationToasts.cshtml`).
- **Audit**: `FundService` injects `IAdminAuditEventWriter` and writes `AdminAuditEvent.Record(actor, "fund.create|fund.edit|fund.archive|fund.reactivate|fund.regulation.set|fund.regulation.remove", "fund", fundId, payloadJson)` — new action/target constants on `AdminAuditEvent` (`src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs`).
- **Sidebar**: add `new("funds", "Fondos", "/Admin/Funds", "ti ti-coin", new[] { "Admin" })` to the admin entries in `_Layout.cshtml`.

## D8 — Process create/edit Fund selector

- **Decision**: `AdminProcessCreateViewModel` and the edit path gain a required `FundId` (dropdown of **Active** Funds). `AdminProcessesController.Create`/edit set/validate it; `Process.Create` factory takes `fundId` (or a `SetFund`/reassign method for edit, FR-009). Index (`Views/Admin/Processes/Index.cshtml`) adds a Fund column + a Fund filter dropdown next to the existing `ProcessStatus` filter.
- **Rationale**: mirrors the existing required Name field validation and `ProcessStatus` filter already in `AdminProcessesController`.

## D9 — Reports Fund filter/column (now exact)

- **Decision**: Add `int? FundId` to `ListApplicationsRequest`, `ListFundedItemsRequest`, `ListAgingApplicationsRequest` (and applicants where process-scoped). Filter clause via the new anchor: `q.Where(a => a.Group.Process.FundId == req.FundId)`. Add `FundName` to the relevant row DTOs (`ApplicationRowDto`, `FundedItemRowDto`, aging row) + the CSV header/line in `AdminReportsService`. Views (`Applications/FundedItems/Aging.cshtml`) gain a Fund `<select>` populated from Active+Archived Funds (admins can report on archived).
- **Rationale**: D5's anchor makes this a single deterministic join; admins keep visibility into archived Funds (no freeze filter on report queries).

## D10 — Seed data

- **Decision**: New dacpac post-deploy script ordered before Process/Group seeds: create a seed Fund (`Fondo General`, Active), update existing seed Processes' `FundId`, and ensure seed applications (if any in demo seed) have a `GroupId`. Follow the idempotent `MERGE` pattern in `PostDeployment/02_SeedMigracionInicialProcess.sql`.

## Constitution alignment

- **I Clean Architecture**: `Fund`/`FundStatus` in Domain; `IFundService`/storage abstractions in Application; `FundService`/EF config/`ApplicationQueryFilter` in Infrastructure; controllers/views in Web. No inward dependency violations.
- **II Rich Domain Model**: Fund behavior methods; `Application` freeze guard as a domain method.
- **III E2E (non-negotiable)**: Playwright per user story (US1, US2, US3, US4, US5, US6) — see quickstart.
- **IV Schema-first dacpac**: all schema via `.sql` (new `dbo.Funds`, `Processes.FundId`, `Applications.GroupId`); no EF migrations.
- **Conventions**: es-CR copy, reuse spec-014/024/audit, no new NuGet dependencies.

## Open items — RESOLVED at plan review (2026-06-10)

- **OI-1**: ✅ Application-creation selector = auto when one eligible group / required choice when many / block when none (FR-018). Labeled by Process ("convocatoria").
- **OI-2**: ✅ `fund-regulations` size cap = 20 MiB.
- **OI-3**: ✅ Admin report Fund filter lists all Funds including Archived (admins retain reporting visibility into archived Funds).
