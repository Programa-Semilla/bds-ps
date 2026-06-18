# Phase 0 Research: Supplier Recommendation Algorithm Rewrite

**Spec:** spec.md | **Date:** 2026-06-18

All "open" decisions inherited from the unified requirements doc (§28.2/§28.3/§28.13) and the brainstorm were resolved before specification; this file records the implementation-shaping decisions and the concrete seams found in the codebase. No `NEEDS CLARIFICATION` items remain.

---

## D1 — Where the scoring lives

**Decision:** Rewrite the existing `FundingPlatform.Domain/ValueObjects/SupplierScore.cs` value object in place. Keep the `SupplierScore.ComputeForItem(List<(Quotation, Supplier, SupplierBranch?)>)` entry point (its call site is `ReviewService.cs:345-353`), expand the `SupplierScore` record to carry the seven per-criterion scores + total + eligibility/recommended/tie flags, and replace the internals with the §14 algorithm.

**Rationale:** The inputs the new algorithm needs are already passed to `ComputeForItem` — `Supplier` exposes `HaciendaStatus`/`CcssStatus`/`SicopStatus` (nullable enums) and `IsPmeOrPyme`; `Quotation` will carry the new delivery/warranty fields and already carries `ConvertedCrcAmount`. Keeping the entry point minimizes blast radius to the one call site and the DTO mapping. Aligns with Constitution II (Rich Domain Model) — scoring is a domain value object, not service logic.

**Alternatives considered:** A new `IRecommendationService` in Application — rejected (the math is a pure function of domain data; a service adds indirection for no testability gain, and would pull scoring out of the domain). A persisted `RecommendationScoreDetail` table (§22.8 literal) — rejected per the spec's live-computation decision (see D7).

---

## D2 — Tie semantics, two distinct rules

**Decision:** Implement two different tie rules, exactly as §14 states:
- **Price (§14.9):** lowest normalized-CRC price → 2; **if ≥2 providers tie for lowest, all tied get 1** (none get 2).
- **Delivery (§14.7) and Warranty (§14.8):** shortest / longest → 2; **if ≥2 tie, all tied get 2**.

**Rationale:** Explicit client rule for price; the delivery/warranty default ("all tied get 2") was confirmed in brainstorming. The asymmetry is the most likely implementation error, so it gets dedicated unit tests.

---

## D3 — Final-score tie → manual selection (§28.3)

**Decision:** When exactly one eligible provider holds the strict maximum total → that provider `IsRecommended = true`. When ≥2 eligible providers share the maximum total → **no provider is recommended**; the item result carries a `HasRecommendationTie` flag and the tied quotations are marked `IsTiedAtTop`. The UI shows "selección manual requerida" and flags the tied set.

**Rationale:** Reviewer makes the final per-item supplier selection anyway (`Item.Approve(supplierId,…)`); the engine declining to break the tie is the honest behavior. Lowest-price tiebreak was rejected because it reintroduces the price primacy §14 removes.

---

## D4 — CCSS `sin inscripción` eligibility + the progression gate (§28.13)

**Decision:** Two layers:
1. **Recommendation exclusion (scoring):** in `ComputeForItem`, a candidate whose `Supplier.CcssStatus == CcssStatus.SinInscripcion` is filtered out of the eligible set *before* the winner comparisons. It is returned with `IsEligible = false` + a block reason, never scored, never recommended. Price/delivery/warranty winners are computed over the eligible set only. If the eligible set is empty → no recommendation, item flagged "ningún proveedor elegible".
2. **Progression gate (advance):** the reviewer approves an item via `ReviewService.ReviewItemAsync` → `Item.Approve(selectedSupplierId, comment)`. Add the eligibility guard so an item **cannot be approved** with a `sin inscripción` provider. The reviewer can still *open the dropdown and pick* such a provider (UI), but submitting the Approve decision is rejected with an es-CR message naming the item + provider. Since an application advances by having its items approved, blocking the item-approve is exactly "the application cannot move forward."

**Gate placement (Rich Domain Model):** Put the authoritative guard in the **domain** — `Item.Approve` looks up the selected quotation's `Supplier.CcssStatus` (the review flow loads `Quotation.Supplier`) and throws a domain exception (`SupplierIneligibleException` or a guarded `DomainError`) when it is `SinInscripcion`. `ReviewService` ensures suppliers are loaded and translates the failure to the es-CR reviewer message; the controller re-renders the review surface with the error. This keeps the invariant un-bypassable (Constitution II) while the service owns the es-CR translation.

**Important nuance — null ≠ sin inscripción:** `CcssStatus` is nullable; `null` means *sin revisar* (unreviewed), which is **not** a hard block — it merely scores 1. Only the explicit enum value `CcssStatus.SinInscripcion` (=1) blocks. Documented and unit-tested.

**Slice B/C boundary:** The gate is anchored at today's per-item reviewer Approve step. Slice C (auditor workflow) re-anchors it; slice B introduces no new workflow states.

---

## D5 — New quote fields: domain shape + unit normalization

**Decision:** Add to `Quotation`: delivery lead time and warranty, each a value + a `DurationUnit` (`Days`/`Months`). Model each as a small immutable value object **`TimeDuration(int Value, DurationUnit Unit)`** with a computed `InDays` (`Unit == Months ? Value * 30 : Value`). New enum `DurationUnit { Days = 1, Months = 2 }` (TINYINT via `HasConversion<byte>`, mirroring the slice-A status-enum pattern). The entity rejects `Value <= 0` and an undefined unit in its constructor/mutators.

**Normalization constant:** **1 month = 30 days** (spec assumption). Used only for cross-quote comparison; deliberately independent of slice D's "one-month" freshness rule. No normalized-days column is stored — `InDays` is computed (consistent with D7's no-derived-persistence stance).

**Rationale:** A value object keeps the value+unit+normalization invariant in one place and is reused for both fields. `int` value is sufficient (days/months are whole units in the client's examples); avoids decimal-unit ambiguity.

**Alternatives:** Storing `NormalizedDeliveryLeadTimeDays` columns (§22.7 literal) — rejected; it is derived data that must be kept in sync, and the scoring is already live.

---

## D6 — Price comparison uses normalized CRC

**Decision:** The price criterion compares `Quotation.ConvertedCrcAmount ?? Quotation.Price` (CRC quotes set `ConvertedCrcAmount = Price`). The current algorithm compares raw `Price` (`SupplierScore.cs:36`) — a latent bug across mixed currencies; the rewrite fixes it by comparing the CRC-normalized amount from spec 015.

**Rationale:** §14.3 + §22.7 treat price as a quote-level comparable; with multi-currency (spec 015) the only correct comparable is the CRC-normalized value. `LegacyNeedsReview` quotes (null `ConvertedCrcAmount`, non-CRC) are out of scope per greenfield/no-backcompat; seed data is CRC or has a snapshot.

---

## D7 — Compute live, do not persist (spec decision, recorded for plan)

**Decision:** The recommendation result is a transient computed object returned by `ComputeForItem` and mapped into the review DTO/VM on each read. No table, no migration for scores, no invalidation logic. §22.8's field list is realized as the shape of the in-memory result and the DTO, not a database entity.

**Rationale:** Pure function of stored quote + provider data; persisting buys only a staleness problem (the project already carries that cost for the AI `ComparisonArtifact`). Constitution VI (Simplicity/YAGNI).

---

## D8 — Schema migration safety for the new NOT NULL columns

**Decision:** Add the four new columns to `dbo.Quotations.sql` as **`NOT NULL` with a placeholder `DEFAULT`** (value default `1`, unit default `1`=Days), then update the dacpac **post-deploy seed scripts** to set realistic delivery/warranty values on the seeded quotations. The domain enforces the real "required, value > 0" rule at the application boundary going forward.

**Rationale:** Dev runs on a persistent SQL volume and the E2E fixture deploys the dacpac onto an existing schema; declaring bare `NOT NULL` on a populated table fails the publish (the spec-029 / spec-015 lesson — a failed publish rolls back the whole deploy). The `DEFAULT` placeholder is the established migration-safe pattern in this codebase. Greenfield means no production backfill is needed; the placeholder + seed update covers dev/E2E.

**Alternatives:** Nullable columns + app-only enforcement — rejected; weaker DB contract and the spec wants them required. NOT NULL without default — rejected (publish failure on populated DBs).

---

## D9 — AI quote comparison (spec 020) untouched (spec decision, recorded)

**Decision:** No changes to `ComparisonArtifact`, `IComparisonOrchestrator`, the comparison worker, or `_ComparisonRegion.cshtml`. The new recommendation renders in the existing score column/badge region; the AI comparison region renders independently as today.

**Rationale:** Approach A from brainstorming. Additive, not a replacement.

---

## D10 — DTO / view-model expansion + display sites

**Decision:** Expand `ReviewQuotationDto` (`ReviewApplicationDto.cs:39-66`) and `ReviewQuotationViewModel` (`ReviewApplicationViewModel.cs:59-89`) from `int Score` + 4 bools to: the seven per-criterion scores, total, `IsRecommended`, `IsEligible`, `BlockReason`, and the raw delivery/warranty value+unit (price/statuses already present). Add a per-item `HasRecommendationTie` flag to the item-level VM. Update the two display sites in `Review.cshtml`:
- table score cell (`~:255`, `@q.Score/4`) → total + per-criterion breakdown,
- supplier-selection dropdown label (`~:427`, `({q.Score}/5)`) → total (removes the stray `/5`).

**Rationale:** Explainability (FR-022) requires the full breakdown at the reviewer surface; FR-023 removes the coarse fractions. The dropdown `/5` is a known pre-existing variance fixed here.

---

## D11 — es-CR copy placement

**Decision:** New reviewer/applicant strings: field labels ("Tiempo de entrega", "Garantía", unit options "días"/"meses") via `[Display]`/markup on the VMs and `_QuoteFields.cshtml`; the block message ("No se puede aprobar: el proveedor … no está inscrito en la CCSS."), the tie message ("Selección manual requerida — varios proveedores empatan."), and "ningún proveedor elegible" centralized following the existing pattern (`SuppliersResources.cs` / the `IUserFacingErrorTranslator` for domain-error translation, mirroring slice A's `USER_CODE_IN_USE`-style mapping). No English literals.

**Rationale:** Matches the project's es-CR + centralized-error-translation conventions (CLAUDE.md, slice A's `IUserFacingErrorTranslator`).

---

## D12 — Item-line field reorder (§6/§24.4)

**Decision:** Reorder `Views/Item/Add.cshtml` to render ProductName first, then CategoryId, then the `#category-fields` dynamic partial, then remaining fields. The category-change AJAX wiring (`_DynamicFieldWiring.cshtml` → `Item/CategoryFields`) is unchanged — only DOM order moves; the dynamic-fields container stays positioned after the category select. Pure view reorder; no VM/controller/route change.

**Rationale:** §6 is an explicit ordering requirement; the existing AJAX populates `#category-fields` by id, so moving markup order does not break the wiring. Owned by slice B (ships before slice H).
