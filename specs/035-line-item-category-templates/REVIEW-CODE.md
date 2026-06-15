# Code Review — 035 Line-Item Category Templates, Per-Item Impact, Quotation Reuse

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Tasks:** [tasks.md](tasks.md)
**Reviewer:** Claude (speckit.spex-gates.review-code) · **Date:** 2026-06-15

---

## Code Review Guide (30 minutes)

> This section guides a code reviewer through the implementation changes,
> focusing on high-level questions that need human judgment.

**Changed files:** Broad refactor across all layers — dacpac (4 new/changed tables,
2 dropped), Domain (Category/CategoryField/CategoryFieldValue/Item/Application/Plantilla),
EF configs, Application services + DTOs, Web controllers/views/JS. ~40+ source files,
plus integration + E2E + unit test suites.

### Understanding the changes (8 min)

- Start with [`Entities/Item.cs`](../../src/FundingPlatform.Domain/Entities/Item.cs): the
  pivot of the whole feature. Impact relocated here from `Application` (`ImpactTemplateId`,
  `ImpactParameterValues`, `SetImpact`), `CategoryFieldValues` added, `TechnicalSpecifications`
  removed, `ChangeCategory` clears category values on switch.
- Then [`Entities/Application.cs` `Validate(minQuotations)`](../../src/FundingPlatform.Domain/Entities/Application.cs):
  the all-at-once submit gate now aggregates per-item missing-impact + missing-required-field
  errors. This is the single server-side enforcement point for [SC-006](spec.md).
- Question: the impact relocation is an **atomic refactor** (Phase 2 Foundational) — does the
  decomposition into Item-owned impact + EAV category values read cleanly, or does any
  application-level impact vestige remain? (See teardown grep in [quickstart.md](quickstart.md) §5.)

### Key decisions that need your eyes (12 min)

**AI quote-comparison context excludes per-item impact** (`Infrastructure/AiComparison/SupplierAssembler.cs:91-103`, relates to [FR-009](spec.md#L122) / [SC-004](spec.md#L151))

Research [D6](research.md) chose category-values-only (scrubbed) for the AI surface and
**deliberately excludes impact** — rationale: impact is irrelevant to comparing supplier
quotes and free-text must clear PII redaction. A strict reading of FR-009/SC-004 lists the
AI context among the surfaces that show "category values **and** per-item impact." The plan
flagged this as **pending user confirmation**.
- Question: accept the exclusion and evolve the spec to record it, or add impact to the AI
  context through the same PII scrub?

**Reference-counted blob retention** (`Services/ApplicationService.cs` `RemoveQuotationAsync` / `ReplaceQuotationDocumentAsync`, relates to [FR-007 edge](spec.md#L72))

Reuse means multiple `Quotation` rows share one `DocumentId`. Removal detaches the row
**first**, then deletes the blob only when `application.CountQuotationsReferencingDocument(documentId) == 0`.
- Question: is the detach-then-count ordering correct under all paths (remove vs replace-document),
  and is the count scoped correctly to the whole application's items?

**Submit-gate validation reads the category's CURRENT field set** (`SubmitApplicationHandler.cs:47-54`, relates to the [category-fields-edited-after-use edge](spec.md#L94))

The handler eager-loads `Category.Fields` so a newly-added required field blocks an in-progress
draft but does not retroactively invalidate already-submitted applications.
- Question: is "current field set vs captured values" the intended semantics for drafts, and is
  it covered by the integration test (`PerItemImpactCategoryTests`)?

### Areas where I'm less certain (5 min)

- `Services/ApplicationService.cs` `AddItemAsync`/`UpdateItemAsync` ([FR-006](spec.md#L116)):
  impact-parameter **value** validation lives in the service (needs template metadata) while the
  impact-**presence** + required-category-field checks live in `Application.Validate`. Two
  enforcement sites — confirm they can't disagree (e.g., an item passing the service add but
  failing the submit gate, or vice-versa).
- `Views/Item/_DynamicFieldWiring.cshtml` + `wwwroot/js/dynamic-fields.js`: the dynamic
  category-field + impact-parameter renderer was extracted to one shared client renderer. Verify
  the server-side `_DynamicFieldInputs.cshtml` (validation re-render path) stays in lockstep with
  the JS control mapping for all four data types.

### Deviations and risks (5 min)

- **es-CR validation messages (FIXED in this review):** `Application.Validate` shipped the two
  new 035 submit-block messages (missing impact, missing required field) in **English**, violating
  [FR-013](spec.md#L132)/[SC-006](spec.md#L153). Localized to es-CR at
  `Entities/Application.cs:439-453`. The "impacto" substring keeps `ApplicationSubmitGuardTests`'
  `Does.Contain("impact")` green. Question: the two **pre-existing** English messages in the same
  method (`"Application must have at least one item."` line 428, `"...at least N quotation(s)."`
  line 436) are outside 035's introduced/changed scope and line 428 is pinned by a unit assertion
  `Does.Contain("at least one item")` — acceptable to leave as pre-existing tech debt?
- **AI-impact exclusion** (above): the one open deviation from a strict [FR-009](spec.md#L122)
  reading. Needs a user decision before this is called 100% compliant.
- **Stale doc comment:** `ValueObjects/Impact.cs` header still references `Application.SetImpact`
  (now `Item.SetImpact`). Cosmetic; no behavior impact.
- No deviations from [plan.md](plan.md) phase structure were identified; the atomic Foundational
  refactor landed build-green (T030) and the teardown greps return zero live references
  ([SC-003](spec.md#L150), T061).
