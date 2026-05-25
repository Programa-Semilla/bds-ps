# Research: Supplier Branch Location Cascade

Spec: [spec.md](./spec.md) · Plan: [plan.md](./plan.md)

## Decision 1 — Cascade JS: generalize, don't duplicate

**Decision**: Generalize `province-canton-cascade.js` into a data-driven `location-cascade.js`. A source `<select>` declares `data-cascade-endpoint`, `data-cascade-param`, `data-cascade-target`, `data-cascade-placeholder`; the script binds every `select[data-cascade-source]` and drives both province→cantón and cantón→distrito with one code path.

**Rationale**: The existing script already does exactly the province→cantón mechanics (fetch, replace options, preserve prior selection via `data-cascade-current`, dispatch bubbling `change`). Parameterizing it avoids a near-duplicate file and makes the bubbling `change` naturally chain province → cantón → distrito (a province change resets both lower tiers). Backward compatible: the province source keeps identical behavior.

**Alternatives considered**: (a) a second bespoke `canton-district-cascade.js` — rejected as copy-paste with divergence risk; (b) a framework/component — rejected (no SPA stack; vendored-only, no new dep).

## Decision 2 — Display continuity via composed legacy `Province` string

**Decision**: On save, compose `"{Distrito}, {Cantón}, {Provincia}"` (most-specific first) and write it to the legacy `SupplierBranch.Province` column; FK columns are the source of truth.

**Rationale**: `_BranchPicker.cshtml:45`, admin `Detail.cshtml:160`, and the supplier DTOs all read `Province` today. Composing the string keeps every display surface working with zero changes (FR-013) and continues the spec-021 "dual-read" posture. Most-specific-first matches how a CR address is spoken when leading with the precise locality.

**Alternatives considered**: update every display surface to read `ProvinceRef/CantonRef/DistrictRef` — more churn, more E2E selector risk, no user benefit now.

## Decision 3 — Domain invariant arity (`SetLocation`)

**Decision**: Extend to `SetLocation(provinceId, cantonId, districtId, canton, district)`. Keep province+cantón both-or-neither; add "if districtId set → cantonId set and district.CantonId == cantonId". Do **not** force district-present-whenever-pair-present at the domain layer; enforce all-three-required at the form/controller layer for the three wired surfaces.

**Rationale**: The only other caller, `CreateSupplierBranchHandler` (spec-021 inline `ApplicationController` path), has **no live UI** (EVOLVE-NOTE-us2-applicant-flow) and is out of scope. This signature keeps it compiling (pass `districtId: null`) without dragging it into scope or leaving it half-consistent. The user-visible guarantee (all three) is fully delivered where users actually enter data.

**Deviation note**: spec FR-006 says "all three or none" at the data layer; the domain permits province+cantón without distrito. **Flag in REVIEW-CODE as a tracked deviation** (same handling as spec 023 FR-008). Resolution path: if the inline path is rebuilt, tighten the domain to strict all-three-or-none and wire its district.

**Alternatives considered**: strict all-three-or-none in the domain now — would break/force the orphaned inline path into scope.

## Decision 4 — Server-side hierarchy validation + aggregation

**Decision**: Resolve the submitted `DistrictId` via `ILocationCatalogReader.GetDistrictChainAsync` and assert `district.CantonId == cantonId` and `canton.ProvinceId == provinceId` server-side before any write; all failures (missing/forged levels) are added to `ModelState` and re-rendered together with the form.

**Rationale**: FR-005 (never trust client-claimed parents) + constitution quality gate (all validation errors shown at once, like spec 023). One indexed query resolves the whole chain.

## Decision 5 — Distrito dataset (source, count, reconciliation)

**VERIFIED PREMISE CORRECTION.** The spec's working assumption ("84-cantón catalog predates the 2022 Puerto Jiménez cantón") is **wrong**. The existing `01_SeedProvincesCantons.sql` Puntarenas tail is:

```
06_11 Garabito · 06_12 Monteverde · 06_13 Puerto Jiménez
```

i.e. the catalog is the **full modern 84-cantón set** (81 original + Río Cuarto `02_16` + Monteverde `06_12` + Puerto Jiménez `06_13`). The seed's own header comment ("the 2 cantones nuevos", "Monteverde 06-13") is inaccurate and self-contradictory — the actual rows are authoritative. **The distrito seed must match THIS catalog**, which dictates the load-bearing edge cases below.

**Count**: the authoritative national total is **488 distritos** for the post-2019 division (per-province: San José 123, Alajuela 116, Cartago 51, Heredia 47, Guanacaste 61, Puntarenas 60, Limón 30). Sources: [Organización territorial de Costa Rica](https://es.wikipedia.org/wiki/Organizaci%C3%B3n_territorial_de_Costa_Rica), decree N° 41548-MGP. Cantón promotions (Monteverde, Puerto Jiménez) were **reassignments, not additions**, so 488 holds. The integration test (SC-007) pins the **exact** number from the enumerated authoritative list at implementation time and asserts it — it does not hard-code a guess.

**Code scheme**: `'PP_CC_DD'` — province (2-wide `0P`) / cantón ordinal (2) / distrito ordinal (2). This is exactly the INEC/Correos `PCCDD` postal scheme (`10101` = San José / cantón 01 / Carmen), confirmed across sources. Distrito `_DD` is a zero-padded ordinal within each cantón. Seed resolves `CantonId` from the cantón `Code` (`'PP_CC'`) via lookup, not identity, staying robust.

**Source strategy**: no single off-the-shelf dataset is turnkey for our exact catalog (they lag 474–479 distritos / 81–82 cantones and predate the Monteverde/Puerto-Jiménez promotions). Build the seed by:
1. Taking the bulk hierarchy + `PP/CC/DD` keys from the cleanest machine-readable source — the [josuenoel gist](https://gist.github.com/josuenoel/80daca657b71bc1cfd95a4e27d547abe) (nested JSON keyed by zero-padded numeric codes; parses to 82 cantones / 479 distritos).
2. **Reconciling the gap to our catalog** against the official enumeration ([Anexo:Distritos de Costa Rica](https://es.wikipedia.org/wiki/Anexo:Distritos_de_Costa_Rica) + INEC Consulta DTA), district-by-district, to the per-province targets (123/116/51/47/61/60/30).

**Load-bearing edge cases for OUR catalog** (the reconciliation blast radius is confined to Puntarenas + Golfito; all other provinces' `PP_CC` ordinals are stable across sources):
- **Golfito `06_07`** → **3** distritos (Golfito, Guaycará, Pavón). The gist/postal sources show 4 because they still nest Puerto Jiménez under Golfito — **drop it**.
- **Puerto Jiménez `06_13`** → **1** distrito (Puerto Jiménez), as its own cantón. Absent from the off-the-shelf sources — **add it**.
- **Monteverde `06_12`** → **1** distrito (Monteverde). Absent from the off-the-shelf sources — **add it**.
- Plus ~7 distritos added in the 2018–2019 redivision across San José / Alajuela / Guanacaste / Puntarenas / Limón — backfill against the per-province targets.

**Validation (the oracle)**: an integration test against the real seeded DB asserts (a) every one of the 84 cantones has ≥ 1 distrito, (b) per-province distrito counts equal 123/116/51/47/61/60/30, (c) Golfito = 3, Puerto Jiménez = 1, Monteverde = 1, (d) every distrito `Code` is `'PP_CC_DD'` whose `PP_CC` prefix is an existing cantón Code. The seed is **generated and reconciled from the authoritative enumeration — never hand-typed from memory** — and this test is what proves it correct (SC-007).

**Alternatives considered**: `llperez/codigos_cr` (postal, CC0 but 81 cantones / Correos-lagged) and `investigacion/divisiones-territoriales-data` (oldest, 474 distritos) — both rejected as the bulk source for being staler than the gist, though useful as cross-checks.
