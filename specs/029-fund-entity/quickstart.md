# Quickstart / Validation: Fund (Fondo) Entity

**Feature**: 029-fund-entity | **Date**: 2026-06-10

## Run

```bash
dotnet build FundingPlatform.slnx
dotnet run --project src/FundingPlatform.AppHost     # dacpac auto-deploys (new dbo.Funds, Processes.FundId, Applications.GroupId)
```

## Manual smoke (es-CR UI)

1. **Admin → Fondos** (`/Admin/Funds`): create "Fondo General" with description; upload a PDF regulation; replace it; remove it. Verify toast + audit rows (`AdminAuditEvents`, target `fund`).
2. **Admin → Procesos → Crear**: confirm Fund selector is required and lists only Active Funds; create a Process under the Fund. Verify the Fund column + filter on the Process list.
3. **Applicant create**: as `applicant@programa-semilla.test`, start an application — confirm it anchors to the eligible Group/Process (auto if one, choose if many; blocked if none).
4. **Applicant download**: with the Process's Fund Active and a regulation present, download the regulation; remove regulation → link disappears.
5. **Archive freeze**: archive the Fund. Verify (a) its applications vanish from the applicant list, reviewer queue, signing inbox; (b) applicant edit/submit/withdraw is rejected with an es-CR message; (c) admin can still see it via the Archived filter; (d) reactivate restores everything.
6. **Reports**: filter Applications/Funded Items/Aging by Fund; confirm the Fund column appears in the table and CSV export.

## E2E (Playwright + AspireFixture) — Constitution III, the delivery gate

Add one independently-runnable test class per user story (Page Object Model):

- **US1** `FundAdminCrudTests` — create/edit/archive/reactivate + regulation upload/replace/remove + validation (non-PDF, dup name, blank).
- **US6** `ApplicationFundAnchorTests` — auto-anchor (one group), choose (many), blocked (none), Plantilla resolves via anchor.
- **US2** `ProcessRequiresFundTests` — create blocked without Fund; active-only selector; reassign.
- **US3** `RegulationDownloadTests` — applicant downloads when Active+present; absent link otherwise.
- **US4** `FundArchiveFreezeTests` — archived Fund hides + freezes anchored applications across applicant/reviewer surfaces; reactivate restores.
- **US5** `FundReportFilterTests` — Process list + report Fund filter/column exact.

Seeds: `admin@programa-semilla.test` / `Sentinel123!`; demo `Demo123!`. Confirm seed Fund/Group anchors exist so existing E2E flows still create+submit applications.

```bash
dotnet test tests/FundingPlatform.Tests.Unit          # Fund domain behavior, freeze guard, anchor resolution
dotnet test tests/FundingPlatform.Tests.Integration   # real DB: FK, ExcludeArchivedFund, reports join
dotnet test tests/FundingPlatform.Tests.E2E           # full suite must be green (delivery bar)
```

## Done = full E2E suite personally executed and green (CLAUDE.md delivery bar).
