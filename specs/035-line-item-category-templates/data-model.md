# Data Model: Line-Item Category Templates, Per-Item Impact, and Quotation Reuse

**Feature:** 035 | **Date:** 2026-06-12 | **Phase:** 1 (Design)

Schema is source-of-truth in the dacpac (`FundingPlatform.Database`); EF Core maps it (Constitution IV). Greenfield flow → destructive edits, no backfill scripts (research D10).

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

---

## Modified entities

### `Category` (table `dbo.Categories` — unchanged columns)

Gains a children collection + mutators (it has none today):
- `IReadOnlyList<CategoryField> Fields`
- `Update(name, description)`, `Activate()`, `Deactivate()`
- `AddField(name, displayLabel, dataType, isRequired, sortOrder)`, `ClearFields()`

EF: `CategoryConfiguration` adds `HasMany(c => c.Fields).WithOne().HasForeignKey(f => f.CategoryId).OnDelete(Cascade)`. No table column change (the `Categories` table already has `Id/Name/Description/IsActive` + `UX_Categories_Name`).

### `Item` (table `dbo.Items`)

- **Add column** `ImpactTemplateId INT NULL`, FK → `dbo.ImpactTemplates(Id)` ON DELETE NO ACTION.
- **Drop column** `TechnicalSpecifications`.
- Domain gains: `ImpactTemplate` nav, `ImpactParameterValues` collection, `CategoryFieldValues` collection (read-only), and methods:
  - `SetImpact(ImpactTemplate template, IEnumerable<ImpactParameterValue> values)` — relocated from `Application.SetImpact`.
  - `SetCategoryFieldValues(IEnumerable<CategoryFieldValue> values)` — replace-all.
  - `ChangeCategory(int newCategoryId)` — sets category and **clears `_categoryFieldValues`** when the id changes.
  - `Update(...)` no longer takes `technicalSpecifications`.
- Constructor changes: `Item(productName, categoryId)` (drops `technicalSpecifications`).

### `Application` (table `dbo.Applications`)

- **Remove** `ImpactTemplateId` column + the `ImpactTemplate` nav, `ImpactParameterValues` collection, `Impact` VO getter, and `SetImpact`.
- `Submit(...)` no longer checks `ImpactTemplateId is null`. `Validate(minQuotations)` gains per-item checks (see Validation rules).
- **Add** `CountQuotationsReferencingDocument(int documentId)` → counts quotations across all items sharing the document (supports reference-counted blob retention).

### `ImpactParameterValue` (table `dbo.ImpactParameterValues`) — RE-KEYED

- Shadow FK `ApplicationId` → **`ItemId`** (NOT NULL, FK → `dbo.Items(Id)` ON DELETE CASCADE).
- Unique index `UX_ImpactParamValues_AppId_ParamId` → `UX_ImpactParamValues_ItemId_ParamId (ItemId, ImpactTemplateParameterId)`.
- `IX_ImpactParameterValues_ApplicationId` → `IX_ImpactParameterValues_ItemId`.
- EF: the `Ignore(ImpactId)`/`Ignore(Impact)` vestigial lines in `ImpactParameterValueConfiguration` are removed if the stale `Impact` entity is deleted.

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
Fund → Process → Group → Application → Item ─┬─ Quotation ── Document (shared, ref-counted)
                                             ├─ CategoryFieldValue → CategoryField → Category
                                             └─ ImpactParameterValue → ImpactTemplateParameter → ImpactTemplate
                                                Item.ImpactTemplateId → ImpactTemplate (the pick)

Category → CategoryField (1:N, owned)
ImpactTemplate → ImpactTemplateParameter (1:N, owned)   [catalog unchanged]
Plantilla/ProcessPlantilla → (no impact-template link)  [gating removed; min-quotes + required-flags kept]
```

Impact and category-field values are now **per Item**, not per Application.

---

## Validation rules

Collected all-at-once (Constitution gate) in `Application.Validate(minQuotations)` + the application service:

- Application has ≥ 1 item (existing).
- Per item: ≥ `MinimumQuotationsPerItem` quotations (existing, `Item.HasMinimumQuotations`).
- **Per item: an impact template assigned** (`Item.ImpactTemplateId` not null) — replaces the old application-level gate.
- **Per item: all required impact parameter values present** (service-layer, per item — relocated from `SetApplicationImpactAsync`).
- **Per item: all required category-field values present** for the item's selected category.
- Required-cell shape errors surface in es-CR naming the line item + field (SC-006).

Reuse:
- Reuse candidates limited to quotations in the **same application** (FR-008).
- Reused quotation gets its own `Price`/`Currency`/`ValidUntil`; shares `DocumentId` + `SupplierId`/`SupplierBranchId`.
- Blob/document deleted only when the last referencing quotation in the application is removed (D5).

State transitions: unchanged (Draft → Submitted → …). Impact/category data is captured while Draft; immutable rules follow the existing aggregate freeze (`EnsureNotFrozen`).
