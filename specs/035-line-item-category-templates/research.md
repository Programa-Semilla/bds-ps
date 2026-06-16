# Research: Line-Item Category Templates, Application-Level Impacts with Per-Item Attribution, and Quotation Reuse

**Feature:** 035 | **Date:** 2026-06-12 | **Evolved:** 2026-06-16 | **Phase:** 0 (Outline & Research)

This document resolves the unknowns and open questions from the spec and brainstorm against the current codebase, so Phase 1 design and the task list can proceed without re-discovery. Every decision below is grounded in files read during research (paths cited).

> **Evolution (2026-06-16).** A late requirement moved impact data collection from **per item** to the **application level with one or more impacts**, and made the line item carry a **multi-impact attribution + a short justification** instead of its own impact field values. **Still authoritative:** D1, D3 (now applied at the application level), D4, D5, D7 (now on the impacts step), D10, D12. **Superseded:** D2 → **D13**; D8 → **D15**; the impact parts of D9 and D11 → **D16**; new **D14** for attribution/justification. The superseded decisions are kept below for history, each tagged `[SUPERSEDED]`.

---

## D1. Category-field model: re-key the impact-template pattern, owned by the category

**Decision:** Add two entities mirroring the impact-template parameter/value pattern, owned 1:1 by `Category`:
- `CategoryField` — child of `Category` (label, key, data type, required, sort order), analogous to `ImpactTemplateParameter`.
- `CategoryFieldValue` — EAV value keyed by **`ItemId`** + `CategoryField`, analogous to `ImpactParameterValue` (which is keyed by ApplicationId today; the category value is keyed by item because a category is chosen per item).

`Category` gains mutators (it has **none** today — `src/FundingPlatform.Domain/Entities/Category.cs` exposes only a constructor): `Update(name, description)`, `Activate()`/`Deactivate()`, `AddField(...)`, `ClearFields()`, and a read-only `Fields` collection — exactly mirroring `ImpactTemplate` (`src/FundingPlatform.Domain/Entities/ImpactTemplate.cs`).

**Rationale:** The spec confirmed (brainstorm) a category owns its field set 1:1 — no standalone reusable template catalog. Re-keying the proven impact-template pattern (entity + child params + EAV values + index-based repeating-row admin UI + `ParameterDataType` enum) maximizes reuse and consistency, and avoids inventing parallel machinery (Constitution VI).

**Reuses the existing `ParameterDataType` enum** (`src/FundingPlatform.Domain/Enums/ParameterDataType.cs` — `Text=0, Decimal=1, Integer=2, Date=3`) for `CategoryField.DataType`. No new enum.

**Alternatives rejected:** (a) A standalone `CategoryTemplate` catalog with a `Category → template` FK — rejected in brainstorm (more machinery than the seed implies). (b) A JSON column for category values — rejected; the EAV pattern is what the codebase already uses for impact and keeps per-field querying/labeling uniform.

---

## D2. Impact relocation: re-key `ImpactParameterValues` from Application to Item — `[SUPERSEDED by D13]`

**Decision:** Move impact from `Application` to `Item`, reversing spec 021's relocation:
- `Item` gains `ImpactTemplateId` (`int?`), an `ImpactTemplate` nav, an `ImpactParameterValues` collection, and a domain `SetImpact(template, values)` method (moved down from `Application.SetImpact`, `src/FundingPlatform.Domain/Entities/Application.cs:177`).
- `dbo.ImpactParameterValues` re-keys its shadow FK from `ApplicationId` → `ItemId`; the unique index changes from `(ApplicationId, ImpactTemplateParameterId)` → `(ItemId, ImpactTemplateParameterId)`; FK becomes `→ Items ON DELETE CASCADE`.
- `Application` loses `ImpactTemplateId`, `ImpactTemplate`, `ImpactParameterValues`, `Impact` (VO getter), and `SetImpact`.
- `ItemDto.Impact` (`src/FundingPlatform.Application/DTOs/ItemDto.cs:7`) — already exists as a per-item slot, currently always null — becomes the populated landing spot. `ApplicationDto.Impact` is removed.

**Rationale:** Greenfield (no production application data), so re-keying is a clean schema edit, not a migration. `ItemDto.Impact` and the reviewer per-item impact fields (`ReviewItemDto.ImpactTemplateName`/`ImpactParameters`, `src/FundingPlatform.Application/DTOs/ReviewApplicationDto.cs:24`) already exist; `ReviewService.MapToReviewDto` (`src/FundingPlatform.Application/Services/ReviewService.cs:331`) carries a comment that the current "same per-application impact on every item" is a placeholder for exactly this refactor.

**Required-impact-value validation placement:** keep in the application service (mirrors current `ApplicationService.SetApplicationImpactAsync`, which validates required params and throws), now operating per item. The submit-time "impact required" gate moves from `Application.Submit`'s `ImpactTemplateId is null` check (`Application.cs:428`) into `Application.Validate(minQuotations)` as a per-item check: every item must have `ImpactTemplateId` set (collected into the existing all-errors-at-once list, Constitution gate). This is consistent with the existing pattern; the stale standalone `src/FundingPlatform.Domain/Entities/Impact.cs` entity (if still present) is removed as dead code.

---

## D3. Impact gating removed: any active impact template is selectable per item

**Decision:** Per-item impact selection is **not** gated by the Plantilla. The applicant picks from **all active `ImpactTemplate`s** (`IImpactTemplateRepository.GetAllActiveAsync`). The entire Plantilla→impact-template mechanism is removed (see D4). The downstream consumers of `ProcessPlantilla.ImpactTemplateIdsCsv` that drove the old "which templates may I pick" gate (`ProcessService`, `GetApplicationReviewProjection`, `ApplicationService`, `SetApplicationImpactCommand`, `ImpactDto`) are simplified to "all active templates."

**Rationale:** Confirmed decision 2-B in brainstorm. Drops per-process impact governance in favor of simplicity; admins still control the template catalog via active/inactive.

---

## D4. Plantilla teardown: surgical removal of impact-template gating, keep the rest

**Decision:** Remove only the impact-template machinery; keep `MinimumQuotationsPerItem` + `RequiredFieldFlags` and the assignment/archive/process-detach behavior. Concrete removal set (all verified during research):

- **dacpac:** drop `dbo.PlantillaImpactTemplates.sql` (the M2M join — the only schema object to drop); drop the `ImpactTemplateIdsCsv` column from `dbo.ProcessPlantillas.sql`.
- **EF:** delete `PlantillaImpactTemplateConfiguration.cs` entirely; remove the `ImpactTemplateIdsCsv` mapping from `ProcessPlantillaConfiguration.cs` (lines 32-34).
- **Domain (`Plantilla.cs`):** remove `_impactTemplates`, `ImpactTemplates`, `AttachImpactTemplate`, `DetachImpactTemplate`; in `AssignTo` (lines 129-161) **remove the `_impactTemplates.Count == 0` guard and the CSV snapshot** — otherwise every assignment throws. `ProcessPlantilla` loses `ImpactTemplateIdsCsv` + `ImpactTemplateIds()`.
- **Application/Infrastructure:** remove `ImpactTemplateIds` from `CreatePlantillaCommand`/`EditPlantillaCommand`/`PlantillaDetail`, `ImpactTemplateCount` from `PlantillaListRow` (`IPlantillaService.cs`); gut the attach/reconcile blocks in `PlantillaService.Create/Edit/Get/List`.
- **Web:** remove the "Plantillas de impacto disponibles" checkbox block from `Views/Admin/Plantillas/Create.cshtml` (lines 49-76) and `Edit.cshtml` (lines 56-73); remove `ImpactTemplateIds`/`AvailableImpactTemplates` from `AdminPlantillaCreate/EditViewModel` and `AdminPlantillaImpactTemplateOption`; remove `LoadImpactTemplateOptionsAsync` from `AdminPlantillasController`; drop the `ImpactTemplateCount` column from `Plantillas/Index.cshtml`.

**Rationale:** Confirmed "no dead code" directive. The `AssignTo` guard + CSV snapshot is the critical ripple — leaving it would break Plantilla→Process assignment once attachment is gone.

---

## D5. Quotation reuse: no schema change; reference-counted blob retention

**Decision:** Reuse shares the supplier/branch + the existing `Document` and creates a **new per-item `Quotation` row** with its own price/currency/validity. No schema change is required:
- `Document` (`src/FundingPlatform.Domain/Entities/Document.cs`) has no back-FK to Quotation; `FK_Quotations_Documents` is `ON DELETE NO ACTION`; EF maps `HasOne(Document).WithMany()` — the model already supports many quotations → one document.
- New application-service method `ReuseQuotationAsync(appId, itemId, sourceQuotationId, price, currency, validUntil)` constructs the `Quotation` with the source's `DocumentId` + supplier + branch (via the existing multi-currency path: `new Quotation(...)` → `SetCurrencyAndAmountAsync(...)` → `item.AttachQuotation(...)`), **skipping the upload + `new Document()`**. Reuse candidates are sourced from the same application's existing quotations only (FR-008).
- **Reference-counted retention:** the two unconditional blob-delete sites — `ApplicationService.RemoveQuotationAsync` (`:557`) and `ReplaceQuotationDocumentAsync` (`:537`) — gain a guard: only delete the blob (and only then consider the `Document` row) when **no other `Quotation` in the application references that `DocumentId`**. Implemented as a domain count method on the `Application` aggregate (`CountQuotationsReferencingDocument(documentId)`), with the blob I/O staying in the service (`TryDeleteQuotationBlobAsync`).

**Note (pre-existing gap, out of scope):** `RemoveItemAsync` and `SoftDelete` already perform no blob/Document cleanup, so shared documents are safe there by omission. The pre-existing item-removal blob leak is not introduced or fixed by this feature.

**`UNIQUE(ItemId, SupplierId)` preserved:** a reused quotation lives on a *different* item, so the constraint still holds. Reusing the same supplier twice on one item remains blocked (correct).

---

## D6. AI quote-comparison context (FR-009): include category description, exclude impact — refinement flagged

**Decision (recommended, flagged for user confirmation):** Add the line item's **product name + category-field label/value pairs** to the AI comparison item context (today `ItemHeader` is only `"Ficha {LineCode|Id}"`, `src/FundingPlatform.Infrastructure/AiComparison/SupplierAssembler.cs:88`). Do **not** add per-item impact to the AI context. Route the new free-text category values through PII scrubbing before they enter the assembled payload.

**Rationale:**
- The AI compares *supplier quotes per item*. Knowing **what is being quoted** (product + category specs) materially improves the comparison; **impact** (jobs created, etc.) is applicant-evaluation metadata irrelevant to comparing quotes. So a literal reading of FR-009 ("AI quote-comparison context shows … category values and per-item impact") is refined to: category values **yes**, impact **no**.
- PII boundary: `PiiRedactor.RedactStructured` (`src/FundingPlatform.Infrastructure/AiComparison/Redaction/PiiRedactor.cs:32`) scrubs only 5 named members; the `Body`/structured channel is otherwise passed verbatim. Free-text category values can carry incidental PII (cédula/phone/email), so they MUST be scrubbed. The unwired `RedactFileText` regex (cédula/phone/email patterns, `PiiPatterns.cs`) is reused to scrub the category-value strings as they are assembled.

**Open for user confirmation:** whether to include category values in the AI context at all (this is the one place FR-009 is refined). If the user prefers strict FR-009, impact is added too — but the PII-scrub requirement on free-text stands either way.

---

## D7. "No active impact templates" / last-template deactivation

**Decision:** No special guard preventing deactivation of the last active impact template. Per-item impact is required at submit, but existing drafts keep their already-assigned template (stored as `Item.ImpactTemplateId` + values). When zero active templates exist, the add/edit-item flow surfaces a clear es-CR empty-state ("no hay plantillas de impacto activas") and submission is blocked by the per-item impact gate — this is the spec's existing edge case, not a new failure mode.

**Rationale:** Constitution VI (simplicity / YAGNI). A deactivation guard is speculative; the empty-state + submit gate already make the condition visible and safe.

---

## D8. Applicant flow restructure: fold impact into the item form; remove the standalone Impact step — `[SUPERSEDED by D15]`

**Decision:** The current per-application Impact step (`ApplicationController.Impact` GET/POST + `Views/Application/Impact.cshtml` + `ImpactViewModel` + `SetApplicationImpactCommand`) is **removed**. Impact selection moves **into the item add/edit form** (`ItemController.Add/Edit` + `Views/Item/Add.cshtml`/`Edit.cshtml` + `AddItemViewModel`/`EditItemViewModel`). The item form becomes: **category (required, first)** → dynamic **category fields** → product name → **impact template (any active) + dynamic impact values**. `TechnicalSpecifications` is removed from the form, the view models, the commands (`AddItemCommand`/`UpdateItemCommand`), and the `Item` entity/table.

**Dynamic field rendering reuses the existing pattern:** the `DataType → input control` switch in `Impact.cshtml` (Razor + JS, lines 74-92 / 113-180) is generalized. The existing JSON endpoint `GET /Application/{id}/Impact/TemplateParameters/{templateId}` (returns parameter descriptors) is **kept** for the per-item impact picker; a **new** parallel endpoint `GET .../Category/{categoryId}/Fields` returns the selected category's field descriptors for the dynamic category-field form. Both feed the same client-side renderer (extract the duplicated switch into one small reused JS helper).

**Inline add-item on `Edit.cshtml`** (the quick `AddItem` POST, `ApplicationController.cs`) is reconciled: because adding an item now requires category fields + impact, the inline single-row add is replaced by a link to the full item form (or expanded to host the dynamic fields). Decision: route "Agregar línea" to the full `ItemController.Add` form (simpler, one canonical add path); remove the inline add form + `ApplicationController.AddItem`/`RemoveItem` duplication in favor of `ItemController`.

**Rationale:** The seed's required flow is category → category fields → product → impact → quotation, all per line item. A single canonical item form (rather than an application-level impact step + inline add) matches that flow and removes the now-meaningless per-application impact UI.

---

## D9. Display surfaces (FR-009): convert four render surfaces from per-application to per-item; add two — `[impact parts SUPERSEDED by D16; category-field parts stand]`

**Decision:** Per-item impact + category-field values render on:
- **Convert (impact already shown per-application, move into each item):** Applicant `Details.cshtml` (impact card at :119 → per-item block in the item table at :185), Applicant `Review.cshtml` (:49 card → per-item rows) + its projection, Reviewer `Review/Review.cshtml` (:165, already per-item-shaped but fed duplicated data — fix `ReviewService.MapToReviewDto`), Applicant draft `Edit.cshtml` (:87 impact card → per-item summary).
- **Add (absent today):** Funding-agreement PDF — add a category-fields + impact block per line item (`FundingAgreementItemRowDto` gains the fields; a PDF partial renders them; note spec 018 kept the body minimal — this is a deliberate, spec-035-authorized addition). AI comparison context — per D6.
- **No admin per-application impact view exists** (admins view via reviewer surfaces) — nothing to convert there.

Each surface reuses the `dl.row` label/value rendering already used by the per-application impact card.

**Rationale:** FR-009 + SC-004 enumerate exactly these surfaces.

---

## D10. dacpac / greenfield strategy

**Decision:** All schema changes are made directly in the dacpac (`FundingPlatform.Database`), per Constitution IV. Because no production application data exists (greenfield flow), **no backfill/migration post-deploy scripts are needed**; the dev SQL container and the ephemeral E2E fixture rebuild from the dacpac. Schema delta:
- **New tables:** `dbo.CategoryFields`, `dbo.CategoryFieldValues`.
- **Re-keyed table:** `dbo.ImpactParameterValues` (ApplicationId → ItemId + index/FK changes).
- **Altered table:** `dbo.Items` — add `ImpactTemplateId INT NULL` (FK → ImpactTemplates NO ACTION); drop `TechnicalSpecifications`.
- **Dropped:** `dbo.PlantillaImpactTemplates`; `ProcessPlantillas.ImpactTemplateIdsCsv` column.
- **Seed scripts:** any post-deploy seed that creates demo applications/impact/categories is updated to the new shape (category fields seeded; demo items get per-item impact). Demo categories gain example fields so the applicant flow is demonstrable.

**Rationale:** User confirmed greenfield; the project's schema-first rule + ephemeral E2E rebuild make destructive edits safe.

---

## D11. Domain invariant placement (Constitution II) — `[impact parts SUPERSEDED by D16]`

**Decision:**
- `Item.SetImpact(template, values)` — relocated from `Application`.
- `Item.ChangeCategory(newCategoryId)` (or extend `Item.Update`) — **clears `CategoryFieldValues`** when the category changes (edge case: changing category discards prior category's values).
- `Item.SetCategoryFieldValues(values)` — replace-all, mirroring `SetImpact`.
- `Application.Validate(minQuotations)` — extended to collect, per item: missing required category fields, missing impact assignment (all-errors-at-once).
- `Application.CountQuotationsReferencingDocument(documentId)` — supports D5 reference-counted retention.
- Required-field-value validation (which specific required cells are blank) stays in the application service layer, consistent with the current impact pattern, feeding the same aggregated error list.

**Rationale:** Behavior on the entity per Rich Domain Model; cross-aggregate I/O (blob delete) stays in the service.

---

## D12. Testing strategy (Constitution III)

**Decision:** Each user story gets Playwright E2E (Page Object Model), plus unit tests for new domain behavior and integration tests against the real DB (no mocks):
- **US1 (admin category fields):** integration + E2E for create/edit/reorder/remove category fields.
- **US2 (per-item category fields + impact):** E2E golden path (category → fields → product → impact → save) + submit-blocked-on-missing-required; unit tests for `Item.SetImpact`/`SetCategoryFieldValues`/`ChangeCategory`-clears-values and `Application.Validate` per-item gates.
- **US3 (quotation reuse):** E2E reuse-then-edit-price-independence + reuse-scoped-to-application; integration for reference-counted blob retention (delete originating item's quotation, document survives; remove last reference, blob deleted).
- **US4 (display everywhere):** E2E asserting category values + per-item impact on Details/Review/reviewer detail + PDF generation includes them.
- **Teardown verification (SC-003):** a repo-wide search test/check that `TechnicalSpecifications`, application-level impact members, and Plantilla impact-template gating are absent.

Delivery bar: filtered E2E for the touched classes (not the full ~30-min suite), per project convention.

---

---

## Evolution decisions (2026-06-16)

### D13. Impact at the application level, multiple impacts (replaces D2)

**Decision:** Impact field data is collected once per declared **application impact**, and an application declares **one or more**:
- New aggregate-child `ApplicationImpact` (`dbo.ApplicationImpacts`): `ApplicationId` (FK CASCADE) + `ImpactTemplateId` (FK NO ACTION), `UNIQUE(ApplicationId, ImpactTemplateId)`. Held by `Application._impacts`.
- `dbo.ImpactParameterValues` is **re-keyed to `ApplicationImpactId`** (CASCADE), unique `(ApplicationImpactId, ImpactTemplateParameterId)`. (Pre-035 keyed by `ApplicationId`, prior 035 by `ItemId`.)
- `dbo.Items.ImpactTemplateId` (added by prior 035) is **dropped**; `Item.SetImpact`/`ImpactTemplate` nav/`ImpactParameterValues` removed from the item.
- `ApplicationDto.Impact` (single, removed in prior 035) returns as `ApplicationDto.Impacts` (list); the relocated `Impact` value-object getter lives on `ApplicationImpact` for display.
- Domain: `Application.AddImpact(template, values)` (rejects duplicate template), `Application.RemoveImpact(applicationImpactId)`.

**Rationale:** Direct expression of the new requirement (one or more impacts, each with its own template fields validated independently). Re-keying the existing EAV value table to the new owner is a clean greenfield edit (no migration). Reuses the impact-template catalog and parameter machinery untouched.

**Alternatives rejected:** (a) Keep the prior per-item impact and add a separate app-level impact — rejected: two parallel impact stores violate "no dead code" and confuse the two concepts the UX must keep distinct. (b) A JSON column of impacts on `Application` — rejected; breaks per-field querying/labeling and the established EAV pattern.

### D14. Per-item attribution + short justification (new)

**Decision:**
- New join `ItemImpact` (`dbo.ItemImpacts`): `ItemId` (FK **CASCADE**) ↔ `ApplicationImpactId` (FK **NO ACTION**), `UNIQUE(ItemId, ApplicationImpactId)`, `IX(ApplicationImpactId)`. Held by `Item._itemImpacts`; mutated via `Item.AttributeImpacts(applicationImpactIds)` (replace-all).
- New column `dbo.Items.ImpactJustification NVARCHAR(300) NULL`; domain `Item.SetImpactJustification(string?)` trims + enforces ≤300; required (non-empty) at submit via `Application.Validate`.
- **Cascade:** `Application` reaches `ItemImpacts` via both `Items` and `ApplicationImpacts`; SQL Server forbids two cascade paths to one table, so `ApplicationImpactId` is **NO ACTION** and `Application.RemoveImpact` deletes the dependent `ItemImpact` rows first (SC-007).

**Rationale:** A line item references the application's impacts rather than re-entering impact data; the multi-select attribution + a single brief justification is exactly the requirement. The NO-ACTION + domain cleanup keeps referential integrity without an illegal multi-cascade schema.

**Alternatives rejected:** per-(item × impact) justification rows — rejected; the requirement is explicitly a single short field per line item covering all its impacts (spec Out of Scope).

### D15. Flow: application-level impacts step + item-form attribution (replaces D8)

**Decision:** Two distinct surfaces, kept visually separate (UX expectation in spec):
- **Application impacts step** — a multi-impact manager (add impact: pick an **active** `ImpactTemplate` → dynamic impact fields → save; list declared impacts; remove). This repurposes the existing `ApplicationController.Impact` GET/POST + `Views/Application/Impact.cshtml` into an add/list/remove manager rather than deleting it (reverses D8's removal). The existing JSON endpoint `GET /Application/{id}/Impact/TemplateParameters/{templateId}` is reused to render a chosen template's fields.
- **Item form** — category (required, first) → dynamic category fields → product name → **impact attribution multi-select** (options = the application's declared impacts, rendered from the VM; never raw templates, FR-007) → **short justification textarea** (maxlength 300). No impact template/values here. `GET .../Category/{categoryId}/Fields` (D8) is unchanged for category fields.
- "Agregar línea" routes to the canonical `ItemController.Add` (the D8 consolidation of the inline add path stands).

**Rationale:** Matches the requirement's explicit UX: impact selection at the application level; the line-item form references already-selected impacts; the two must not be confused. Reusing the existing Impact view/endpoint as a manager is less churn than the D8 teardown.

**Note:** since the prior 035 already removed `Views/Application/Impact.cshtml` and the per-application impact actions (D8 was implemented), this step is partly a **re-introduction** in a new (multi-impact) shape — see tasks.

### D16. Display + validation placement (replaces impact parts of D9/D11)

**Decision:**
- **Application level:** an "Impactos" card listing each declared impact (name + its values) on Applicant `Details`/`Review`/`Edit`, Reviewer `Review`, and the funding-agreement PDF.
- **Per item:** the attributed impact **name(s)** + the **justification** (plus category fields) on every surface — distinct from the application-level impact values, to prevent confusion.
- **Validation** invariants on `Application.Validate` (app ≥1 impact; per-item ≥1 attribution; per-item non-empty justification; attribution targets declared); required-value detail (which impact param / which category field is blank) stays in the application service, feeding the same aggregated all-at-once es-CR error list.
- **AI context (refines D6):** category values stay in (scrubbed); the per-item **justification** is also included (scrubbed via the same PII regex) since it describes why the item is being requested, which aids quote comparison; raw impact **parameter values** stay excluded. (If strict FR-011 is preferred, impacts can be added through the same scrub.)

**Rationale:** SC-004 enumerates the surfaces; the application/line split mirrors the requirement's "prevent confusion between global impact info and per-item attribution."

---

## Open items carried to `/speckit-tasks` / planning notes

- **D16 / AI context:** include the per-item justification (default: yes, scrubbed) and exclude raw impact values — confirm during implementation if strict FR-011 (impacts included) is wanted instead.
- Exact es-CR copy for the application impacts manager (add/remove impact), the per-item attribution multi-select + justification field + its 300-char counter, and empty-states ("no hay plantillas de impacto activas" / "declare al menos un impacto antes de atribuir líneas") — drafted during implementation against existing es-CR conventions.
- Category-field admin editor + reuse picker copy (unchanged from prior 035 — already implemented).
