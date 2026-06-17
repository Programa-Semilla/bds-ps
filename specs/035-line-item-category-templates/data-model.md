# Data Model: Line-Item Category Templates, Application-Level Impacts with Per-Item Attribution, and Quotation Reuse

**Feature:** 035 | **Date:** 2026-06-12 | **Evolved:** 2026-06-16 | **Phase:** 1 (Design)

Schema is source-of-truth in the dacpac (`FundingPlatform.Database`); EF Core maps it (Constitution IV). Greenfield flow → destructive edits, no backfill scripts (research D10).

> **Evolution (2026-06-16).** Impact moves from **per item** (the prior 035 design, D2) to the **application level with multiple impacts** (D13): new `dbo.ApplicationImpacts`; `dbo.ImpactParameterValues` re-keyed to `ApplicationImpactId`. Line items gain a many-to-many **attribution** to declared impacts (`dbo.ItemImpacts`, D14) and a single `ImpactJustification` column; `dbo.Items.ImpactTemplateId` is dropped. Category-field tables (`CategoryFields`/`CategoryFieldValues`) and quotation reuse are **unchanged**.

---

## New entities

### `CategoryField` (table `dbo.CategoryFields`)

Child of `Category`. Mirrors `ImpactTemplateParameter`.

| Column | Type | Constraints |
|---|---|---|
| `Id` | INT IDENTITY | PK |
| `CategoryId` | INT | NOT NULL, FK → `dbo.Categories(Id)` ON DELETE CASCADE |
| `Name` | NVARCHAR(200) | NOT NULL — internal key |
| `DisplayLabel` | NVARCHAR(300) | NOT NULL — es-CR user-facing label |
| `DataType` | INT | NOT NULL — `ParameterDataType` enum (Text=0, Decimal=1, Integer=2, Date=3) |
| `IsRequired` | BIT | NOT NULL DEFAULT(1) |
| `SortOrder` | INT | NOT NULL DEFAULT(0) |

Index: `IX_CategoryFields_CategoryId (CategoryId)`.

Domain: `CategoryField` entity with `private set` properties + constructor `(name, displayLabel, dataType, isRequired, sortOrder)`. No `ValidationRules` column (the impact analog's dormant seam is not carried; out of scope).

### `CategoryFieldValue` (table `dbo.CategoryFieldValues`)

Applicant-entered value for one category field on one **item**. Mirrors `ImpactParameterValue` but keyed by `ItemId`.

| Column | Type | Constraints |
|---|---|---|
| `Id` | INT IDENTITY | PK |
| `ItemId` | INT | NOT NULL, FK → `dbo.Items(Id)` ON DELETE CASCADE |
| `CategoryFieldId` | INT | NOT NULL, FK → `dbo.CategoryFields(Id)` ON DELETE NO ACTION |
| `Value` | NVARCHAR(MAX) | NULL — stored as string; type coercion is app-layer |

Indexes: `UX_CategoryFieldValues_ItemId_FieldId UNIQUE (ItemId, CategoryFieldId)`; `IX_CategoryFieldValues_ItemId (ItemId)`.

Domain: `CategoryFieldValue` entity `(categoryFieldId, value)`; nav to `CategoryField`. Held by `Item._categoryFieldValues`.

### `ApplicationImpact` (table `dbo.ApplicationImpacts`) — NEW (D13)

A single impact the application declares: a chosen `ImpactTemplate` plus its entered values. An application has **one or more**.

| Column | Type | Constraints |
|---|---|---|
| `Id` | INT IDENTITY | PK |
| `ApplicationId` | INT | NOT NULL, FK → `dbo.Applications(Id)` ON DELETE CASCADE |
| `ImpactTemplateId` | INT | NOT NULL, FK → `dbo.ImpactTemplates(Id)` ON DELETE NO ACTION |

Indexes: `UX_ApplicationImpacts_AppId_TemplateId UNIQUE (ApplicationId, ImpactTemplateId)` (the same impact template cannot be declared twice on one application); `IX_ApplicationImpacts_ApplicationId (ApplicationId)`.

Domain: `ApplicationImpact` entity with `private set` props + ctor `(impactTemplateId)`; nav to `ImpactTemplate`; holds `_parameterValues` (the re-keyed `ImpactParameterValue` collection). Held by `Application._impacts`. Exposes `SetValues(IEnumerable<ImpactParameterValue>)` (replace-all). The per-item `Impact` value-object getter is relocated here (renders name + values for display).

### `ItemImpact` (table `dbo.ItemImpacts`) — NEW (D14, attribution join)

Many-to-many between a line item and the application's declared impacts.

| Column | Type | Constraints |
|---|---|---|
| `Id` | INT IDENTITY | PK |
| `ItemId` | INT | NOT NULL, FK → `dbo.Items(Id)` **ON DELETE CASCADE** |
| `ApplicationImpactId` | INT | NOT NULL, FK → `dbo.ApplicationImpacts(Id)` **ON DELETE NO ACTION** |

Indexes: `UX_ItemImpacts_ItemId_AppImpactId UNIQUE (ItemId, ApplicationImpactId)`; `IX_ItemImpacts_ApplicationImpactId (ApplicationImpactId)` (for the cleanup query when a declared impact is removed).

**Cascade rationale:** `Application` reaches `ItemImpacts` by two paths — `Application → Items → ItemImpacts` and `Application → ApplicationImpacts → ItemImpacts`. SQL Server forbids multiple cascade paths to one table, so **`ApplicationImpactId` is NO ACTION**. Deleting an item cascades away its attributions; deleting a declared `ApplicationImpact` does **not** cascade — the domain explicitly removes the referencing `ItemImpact` rows first (`Application.RemoveImpact`, SC-007).

Domain: `ItemImpact` entity `(applicationImpactId)`; held by `Item._itemImpacts`.

---

## Modified entities

### `Category` (table `dbo.Categories` — unchanged columns)

Gains a children collection + mutators (it has none today):
- `IReadOnlyList<CategoryField> Fields`
- `Update(name, description)`, `Activate()`, `Deactivate()`
- `AddField(name, displayLabel, dataType, isRequired, sortOrder)`, `ClearFields()`

EF: `CategoryConfiguration` adds `HasMany(c => c.Fields).WithOne().HasForeignKey(f => f.CategoryId).OnDelete(Cascade)`. No table column change (the `Categories` table already has `Id/Name/Description/IsActive` + `UX_Categories_Name`).

### `Item` (table `dbo.Items`)

- **Drop column** `TechnicalSpecifications`.
- **No** `ImpactTemplateId` column (the prior 035 design added it; the evolution **removes** it — impact field data lives on `ApplicationImpact`, not the item).
- **Add column** `ImpactJustification NVARCHAR(300) NULL` — the single short per-item justification (required at submit; nullable column, gate in `Validate`).
- Domain gains: `CategoryFieldValues` collection (read-only), `ItemImpacts` collection (read-only — the attributions), and methods:
  - `SetCategoryFieldValues(IEnumerable<CategoryFieldValue> values)` — replace-all.
  - `ChangeCategory(int newCategoryId)` — sets category and **clears `_categoryFieldValues`** when the id changes (attributions + justification unaffected).
  - `AttributeImpacts(IEnumerable<int> applicationImpactIds)` — replace-all of `_itemImpacts` (the multi-select attribution).
  - `SetImpactJustification(string? justification)` — trims; enforces ≤300 chars (domain guard); stored as the column.
  - `Update(...)` no longer takes `technicalSpecifications`; **no** impact-template/value params.
- Constructor: `Item(productName, categoryId)` (drops `technicalSpecifications`; no impact params).
- **Removed** (vs prior 035): `ImpactTemplate` nav, `ImpactParameterValues` collection, `SetImpact`.

### `Application` (table `dbo.Applications`)

- **No** `ImpactTemplateId` column (the pre-035 single-impact column stays removed; multiplicity now lives in `dbo.ApplicationImpacts`).
- Domain gains: `Impacts` (read-only `IReadOnlyList<ApplicationImpact>`) and methods:
  - `AddImpact(ImpactTemplate template, IEnumerable<ImpactParameterValue> values)` — appends an `ApplicationImpact`; rejects a duplicate template (mirrors the unique index).
  - `RemoveImpact(int applicationImpactId)` — removes the declared impact **and** every `ItemImpact` across all items that references it (SC-007; required because the FK is NO ACTION).
- `Submit(...)`/`Validate(minQuotations)` gains: app has ≥1 declared impact; each declared impact's required values present; per item ≥1 attribution; per item non-empty `ImpactJustification`; per item required category fields; every attribution targets one of the app's declared impacts (see Validation rules).
- **Add** `CountQuotationsReferencingDocument(int documentId)` → counts quotations across all items sharing the document (reference-counted blob retention, unchanged).

### `ImpactParameterValue` (table `dbo.ImpactParameterValues`) — RE-KEYED (D13)

- Owning FK → **`ApplicationImpactId`** (NOT NULL, FK → `dbo.ApplicationImpacts(Id)` ON DELETE CASCADE). (Pre-035 keyed by `ApplicationId`; prior 035 keyed by `ItemId`; the evolution keys by `ApplicationImpactId`.)
- Unique index `UX_ImpactParamValues_AppImpactId_ParamId (ApplicationImpactId, ImpactTemplateParameterId)`.
- `IX_ImpactParameterValues_ApplicationImpactId (ApplicationImpactId)`.
- EF: held by `ApplicationImpact._parameterValues`; any vestigial `Ignore(ImpactId)`/`Ignore(Impact)` lines removed with the stale `Impact` entity (dead code, SC-003).

### `Quotation` / `Document` — NO schema change

Reuse is purely behavioral (research D5). `Document` stays back-FK-free; `FK_Quotations_Documents` stays `ON DELETE NO ACTION`; `UNIQUE(ItemId, SupplierId)` stays. A reused quotation is a new row with an existing `DocumentId`.

---

## Removed schema (Plantilla teardown — research D4)

- **Drop table** `dbo.PlantillaImpactTemplates`.
- **Drop column** `dbo.ProcessPlantillas.ImpactTemplateIdsCsv`.
- (Stale `dbo.Impacts` table / `Entities/Impact.cs`, if still present from pre-021, removed as dead code.)

---

## Relationships (after)

```
Fund → Process → Group → Application ─┬─ Item ─┬─ Quotation ── Document (shared, ref-counted)
                                      │        ├─ CategoryFieldValue → CategoryField → Category
                                      │        ├─ ItemImpact ──────────────┐ (attribution, NO ACTION)
                                      │        └─ ImpactJustification (≤300 chars, per item)
                                      │                                      │
                                      └─ ApplicationImpact ◄────────────────┘ (1:N declared impacts)
                                            ├─ ImpactTemplate (the pick)
                                            └─ ImpactParameterValue → ImpactTemplateParameter → ImpactTemplate

Category → CategoryField (1:N, owned)
ImpactTemplate → ImpactTemplateParameter (1:N, owned)   [catalog unchanged]
Plantilla/ProcessPlantilla → (no impact-template link)  [gating removed; min-quotes + required-flags kept]
```

Impact field **data** is now per **ApplicationImpact** (application level, one or more). Each **Item** is *attributed* to one or more of those declared impacts via `ItemImpact` and carries its own short `ImpactJustification`. Category-field values remain per **Item**.

---

## Validation rules

Collected all-at-once (Constitution gate) in `Application.Validate(minQuotations)` + the application service:

- Application has ≥ 1 item (existing).
- **Application has ≥ 1 declared impact** (`Application.Impacts` not empty) — FR-006 / SC-006.
- **Per declared impact: all required impact parameter values present** (service-layer, per `ApplicationImpact`; errors identify the impact by name).
- Per item: ≥ `MinimumQuotationsPerItem` quotations (existing, `Item.HasMinimumQuotations`).
- **Per item: ≥ 1 impact attribution** (`Item.ItemImpacts` not empty) — FR-007.
- **Per item: non-empty `ImpactJustification`** (and ≤300 chars, enforced at the domain boundary) — FR-008.
- **Per item: every attribution targets one of the application's declared impacts** (referential invariant; removing a declared impact clears its attributions via `Application.RemoveImpact`, SC-007).
- **Per item: all required category-field values present** for the item's selected category.
- Required-cell shape errors surface in es-CR naming the line item / impact + field (SC-006).

Reuse:
- Reuse candidates limited to quotations in the **same application** (FR-010).
- Reused quotation gets its own `Price`/`Currency`/`ValidUntil`; shares `DocumentId` + `SupplierId`/`SupplierBranchId`.
- Blob/document deleted only when the last referencing quotation in the application is removed (D5).

State transitions: unchanged (Draft → Submitted → …). Impact/category data is captured while Draft; immutable rules follow the existing aggregate freeze (`EnsureNotFrozen`).
