# Data Model: Supplier Branch Location Cascade

Spec: [spec.md](./spec.md) · Plan: [plan.md](./plan.md)

## New entity: `District`

Mirrors `Canton` (`src/FundingPlatform.Domain/Entities/Canton.cs`). Read-only catalog row.

| Field | Type | Notes |
|---|---|---|
| `Id` | int (identity) | PK |
| `CantonId` | int | FK → `Canton`. Required (`> 0` guarded in ctor). |
| `Code` | string | `'PP_CC_DD'` (8 chars, fixed length). Unique. Extends the cantón `'PP_CC'` scheme. |
| `Name` | string | es-CR distrito name, ≤ 80 chars. |
| `Canton` | `Canton?` | Nav to parent. |

Constructor mirrors `Canton(provinceId, code, name)`: `District(int cantonId, string code, string name)` with the same guard clauses (`cantonId > 0`, non-blank `code`/`name`, trimmed).

### Table `dbo.Districts` (mirrors `dbo.Cantons.sql`)

```sql
CREATE TABLE [dbo].[Districts]
(
    [Id]        INT           IDENTITY(1,1) NOT NULL,
    [CantonId]  INT           NOT NULL,
    [Code]      CHAR(8)       NOT NULL,          -- 'PP_CC_DD'
    [Name]      NVARCHAR(80)  NOT NULL,
    CONSTRAINT [PK_Districts] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UX_Districts_Code] UNIQUE ([Code]),
    CONSTRAINT [FK_Districts_Cantons]
        FOREIGN KEY ([CantonId]) REFERENCES [dbo].[Cantons] ([Id]) ON DELETE NO ACTION
);
GO
CREATE NONCLUSTERED INDEX [IX_Districts_CantonId] ON [dbo].[Districts] ([CantonId]);  -- covers the cascade query
GO
```

EF: `DistrictConfiguration` mirrors `CantonConfiguration` — `ToTable("Districts")`, key, `Code` `HasMaxLength(8).IsFixedLength().IsRequired()`, `Name` `HasMaxLength(80)`, unique index `UX_Districts_Code`, index `IX_Districts_CantonId`. `AppDbContext.Districts` DbSet added.

### Seed `PostDeployment/02_SeedDistricts.sql`

MERGE-idempotent, same shape as `01_SeedProvincesCantons.sql`. Resolves each row's `CantonId` from the canton `Code` (`'PP_CC'`) so it is independent of identity values. ~488 rows (exact count from `research.md`). Must be `:r`-included after `01_SeedProvincesCantons.sql` in `Script.PostDeployment.sql`. A validation query (in an integration test, not the seed) asserts every cantón has ≥ 1 district and the total matches the authoritative count (SC-007).

## Modified entity: `SupplierBranch`

`src/FundingPlatform.Domain/Entities/SupplierBranch.cs`

Add alongside the existing `ProvinceId`/`CantonId`:

| Field | Type | Notes |
|---|---|---|
| `DistrictId` | `int?` | FK → `District`. Nullable (legacy rows + the both-or-neither pair rule). |
| `DistrictRef` | `District?` | Nav. |

Retained: `Province` (`string?`, ≤ 100) — now carries the **composed display value** `"{Distrito}, {Cantón}, {Provincia}"` for branches saved via the cascade; still free for legacy rows.

### `SetLocation` — extended invariant

```
SetLocation(int? provinceId, int? cantonId, int? districtId, Canton? canton, District? district)
```

Rules (superset of the spec-021 version):
1. `provinceId` and `cantonId` are **both null or both set** (unchanged).
2. If `cantonId` set: `canton` non-null, `canton.Id == cantonId`, `canton.ProvinceId == provinceId` (unchanged).
3. **NEW:** If `districtId` set: `cantonId` must be set, `district` non-null, `district.Id == districtId`, `district.CantonId == cantonId`.
4. `districtId` may be null even when the province/cantón pair is set (domain-level). The **all-three-required** rule is enforced at the form/controller layer for the three wired surfaces (see plan Decision 6 — flagged deviation vs FR-006).

Sets `ProvinceId/CantonId/DistrictId` + `CantonRef/DistrictRef`, bumps `UpdatedAt`.

### Table `dbo.SupplierBranches.sql` delta

Add column + FK (mirrors the Cantons FK):
```sql
[DistrictId] INT NULL,
CONSTRAINT [FK_SupplierBranches_Districts]
    FOREIGN KEY ([DistrictId]) REFERENCES [dbo].[Districts] ([Id]) ON DELETE NO ACTION
```
`SupplierBranchConfiguration`: `builder.Property(b => b.DistrictId);` + `builder.HasOne(b => b.DistrictRef).WithMany().HasForeignKey(b => b.DistrictId).OnDelete(DeleteBehavior.NoAction);`

## Aggregate + DTO + view-model deltas

- `Supplier.AddBranch` / `Supplier.CreateDraft` / `Supplier.EditBranch`: add `(int? provinceId, int? cantonId, int? districtId, Canton? canton, District? district)` params; after constructing/finding the branch, call `branch.SetLocation(...)`. Keep the legacy `province` string param (now receives the composed display value).
- `AddBranchInput` (Application command DTO): add `int? ProvinceId / CantonId / DistrictId`.
- `AddBranchInputViewModel` (Web): replace the free-text `Province` binding with `int? ProvinceId / CantonId / DistrictId` (display label "Provincia"/"Cantón"/"Distrito"). The composed `Province` string is server-set, not posted.
- `AdminEditBranchViewModel`: add `int? ProvinceId / CantonId / DistrictId`.
- `LocationCascadeViewModel` (was `ProvinceCantonCascadeViewModel`): add `DistrictFieldName` (default `"DistrictId"`), `IReadOnlyList<SelectListItem> Districts`, `int? SelectedDistrictId`.

## Application abstraction: `ILocationCatalogReader`

```
Task<DistrictChain?> GetDistrictChainAsync(int districtId, CancellationToken ct);
// DistrictChain { int ProvinceId; string ProvinceName; int CantonId; string CantonName; int DistrictId; string DistrictName; Canton Canton; District District; }
```
Implemented in Infrastructure (`LocationCatalogReader`) over `AppDbContext` (single query with includes). Used by both write paths to (a) validate the submitted chain server-side and (b) build the composed display string. Returns `null` for an unknown/forged district id → controller adds an aggregated `ModelState` error.

## Validation summary (per surface, server-side, aggregated)

| Field | Rule | Message (es-CR) |
|---|---|---|
| `ProvinceId` | required, exists | "La provincia es obligatoria." |
| `CantonId` | required, belongs to `ProvinceId` | "El cantón es obligatorio." / "El cantón no corresponde a la provincia." |
| `DistrictId` | required, belongs to `CantonId` | "El distrito es obligatorio." / "El distrito no corresponde al cantón." |

All added to `ModelState` and re-rendered together with the form (constitution quality gate). Validation runs only on the active sub-path (FR-012).
