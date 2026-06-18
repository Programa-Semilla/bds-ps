# Spec 038 — Implementation Evolution & Deviations

Recorded during `/speckit-implement`. Each item is a place where the shipped
implementation diverged from `tasks.md` / `contracts/interfaces.md`, with the
reason. None change the spec's intent; they reconcile it with codebase reality.

## D-A — Boolean-removal blast radius (beyond tasks.md scope)

`tasks.md` scoped the role rename (~50 sites) and the entity/dacpac/EF changes,
but the four dropped `Supplier` booleans (`IsCompliant{CCSS,Hacienda,SICOP}`,
`HasElectronicInvoice`) flowed through more compile-time sites than the tasks
enumerated. All were updated to keep the build green:

- **`SupplierScore`** (scoring deferred to slice B): kept the same algorithm but
  re-sourced the 3 compliance points from each status's *favorable* value
  (Hacienda/CCSS `al día`, SICOP `sin sanciones`) via a new Domain helper
  `RegulatoryStatusFavorability`. The electronic-invoice point was **removed**
  with the field → **max score 5 → 4**. `SupplierScoreTests` totals updated;
  `Review.cshtml` `/5` → `/4`.
- **e-invoice removed end-to-end**: `SupplierDto`, `SupplierDetailViewDto`
  (`SupplierLookupResultDto`), `SupplierCatalogService.MapToDetail`,
  `ReviewService`/`ReviewQuotationDto`/`ReviewApplicationViewModel`,
  `_LookupHit.cshtml`, `Review.cshtml` (the `Fact-E` score chip). The lookup-card
  compliance badges are re-sourced from favorable status.
- **`SupplierRepository.HasIncompleteCompliance`** filter (2 query sites + the
  `SupplierAdminLastUsedRow` flag): redefined "incomplete" as **any status
  unreviewed (null)** — SQL-clean and meaningful under the new model.
- **`AccountController` `ResetAdminFixture` raw SQL** + the E2E
  `QuotationEditAfterReturnTests` raw INSERT: updated off the dropped columns
  (favorable status codes / column list).
- **`SupplierMigrationParityTests`**: dropped the e-invoice score assertion;
  the (spec-013) parity test is kept, re-sourced via `ApplyRegulatoryEdit`.

## D-B — `EditSupplierComplianceCommand` carries `Name`

`contracts/interfaces.md` omitted `Name` from the command, but `tasks.md` T014
says "incl. Name" and the single Detail edit form posts name + compliance
together. The command includes `Name`; the service applies it via the narrowed
`Supplier.EditByAdmin(name)`. Name changes are **not** audited (only
regulatory/PME/warning are, per scope).

## D-C — US4 notifier trigger site

T038 named `CreateSupplierBranchHandler` as the trigger, but that handler's
new-supplier branch is an **orphaned path with no live UI** (its own code
comment says so). The live applicant supplier-add flow runs through
`SupplierCatalogService.CreateDraftWithBranchAsync` (`SupplierController` →
that method). The notifier fires there, after the successful commit, best-effort.
Verified green by the `ProviderCreatedNotificationTests` E2E (mail captured).

## D-D — `SupplierCatalogService` notifier dependency is nullable

To avoid churning 7 existing test constructors of `SupplierCatalogService`, the
new `IProviderCreatedNotifier` ctor param is nullable with a `null` default. DI
always injects the real notifier in the app; tests that predate US4 get a no-op.

## D-E — Optimistic concurrency (`RowVersion`) tested only at E2E

Integration tests use the EF **InMemory** provider, which does not enforce
`ROWVERSION` concurrency tokens. `SupplierComplianceService` sets the posted
`RowVersion` as the tracked entity's original value and maps
`DbUpdateConcurrencyException` → es-CR "Los datos cambiaron; recargue la página.",
but that branch is exercised only against the real SQL Server in E2E (the unit
+ integration tests cover persistence + audit + the re-review/unset paths).

## D-F — T019 es-CR strings kept inline

Status field headings, the PME label, warning copy, and the save/concurrency
messages were kept as inline es-CR literals in `Detail.cshtml` /
`SupplierComplianceService` rather than relocated into `AdminSuppliersResources`.
The codebase already mixes both styles; relocation was deferred as low-value
churn. The verbatim status **labels** do live in one place
(`RegulatoryStatusLabels`).

## Kept-as-is (documented in research D1)

Filter class names (`SupplierAdminOnly*`), supplier-list DTO names
(`SupplierAdminLastUsedRow`/`SupplierAdminFilter`), the audit code
`supplier_admin.denied_access`, and the post-deploy script filename
`03_SeedSupplierAdminRole.sql` describe the supplier-screen pattern, not the role
identity — intentionally retained to bound churn.
