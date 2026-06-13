# Brainstorm: Line-Item Category Templates, Per-Item Impact, and Quotation Reuse

**Date:** 2026-06-12
**Status:** spec-created
**Spec:** specs/035-line-item-category-templates/

## Problem Framing

The applicant submission flow captures each line item's detail with a single free-text `Item.TechnicalSpecifications` field, declares one **impact** for the whole application, and forces the applicant to re-enter the vendor and re-upload the vendor's quotation document for every line item — even when one vendor quote covers several products. The seed (`brainstorm/seeds/applicant_flow_categories_reuse_quotes.md`) asked to mirror the existing **Impact Template** admin pattern for **categories**, move impact to the line-item level, and allow reusing a quotation across line items in the same application.

Grounding facts confirmed against the codebase before brainstorming:
- `Category` today is `{ Id, Name, Description, IsActive }` — a static catalog row with no fields. Every `Item` has a required `CategoryId`.
- The analog `ImpactTemplate → ImpactTemplateParameter → ImpactParameterValue` (EAV) drives admin-configurable fields; impact was deliberately moved **up** to the Application by spec 021 (`Application.ImpactTemplateId`, values keyed by `ApplicationId`).
- `Quotation` belongs to one `Item` (`ItemId` FK + `UNIQUE(ItemId, SupplierId)`), with `SupplierId/SupplierBranchId/Price/Currency/ValidUntil/DocumentId` + multi-currency snapshot — not shareable across items.
- `Plantilla`/`ProcessPlantilla` carry three things: `MinimumQuotationsPerItem` (keep), `RequiredFieldFlags` (keep), and the impact-template gating snapshot `ImpactTemplateIdsCsv` (remove).

## Approaches Considered

### Scope shape

#### A: One cohesive spec — CHOSEN
- Pros: the three changes converge in the single "add a line item" UI; ships and demos as one stakeholder-facing flow; no half-rebuilt flow between PRs.
- Cons: larger spec + PR; teardown of spec-021 impact wiring rides along.

#### B: Decompose into three sequential specs
- Pros: smaller, independently shippable slices.
- Cons: the applicant flow is half-migrated between specs; awkward to demo/test.

#### C: Two specs (dynamic per-item data + quotation reuse)
- Pros: clean seam between "dynamic line data" and "quotation reuse."
- Cons: still leaves a partially-migrated flow; stakeholder wanted one shippable change.

### Category ↔ template cardinality

#### A: Category owns its field set 1:1 — CHOSEN
- Pros: simplest; "Category Template" = the category's configured fields; no extra catalog entity.
- Cons: identical field sets across categories must be defined twice.

#### B: Standalone named templates, category → template FK
- Pros: reuse one field set across categories; mirrors `ImpactTemplate`.
- Cons: more machinery than the seed implies (the seed says the category *determines* the fields).

### Per-item impact

- **Relocation (1-A, CHOSEN):** remove application-level impact entirely; impact lives only on the item.
- **Gating (2-B, CHOSEN):** any **active** impact template is selectable per item — the Plantilla no longer gates impact-template choice (drops per-process impact governance for simplicity).

### Quotation reuse mechanics

#### A: Share vendor + uploaded document; price per line item — CHOSEN
- Pros: matches real multi-product quotes (a price per product); preserves `UNIQUE(ItemId, SupplierId)` and all per-item review/selection invariants; minimal change (reuse pre-fills vendor + reuses the existing `Document`, price editable). Editing one line's price doesn't affect others.
- Cons: each reusing line still gets its own `Quotation` row (intentional).

#### B: Share the entire quotation record (many-to-many Item↔Quotation)
- Pros: one record, edited in one place.
- Cons: forces a single price across all linked lines (contradicts "5 products → 5 line items"); ripples through review, supplier selection, and the spec-023 edit flow.

## Decision

Chosen: **A (one spec) + category-owns-fields-1:1 + full impact relocation, ungated + quotation reuse A (share vendor+document, per-item price)**, with the **teardown of all dead code in-scope** (user directive: "no vestigial remnants"). Additional confirmed decisions:
- `Item.TechnicalSpecifications` is removed; category fields replace it.
- Every application-render surface (applicant detail, reviewer queue/detail, admin, funding-agreement PDF, AI quote-comparison context) must show per-item category values + per-item impact.
- The `Plantilla` survives for `MinimumQuotationsPerItem` + `RequiredFieldFlags`; only its impact-template gating is removed (`PlantillaImpactTemplates` join, `Plantilla.ImpactTemplates`/`Attach`/`Detach`, `ProcessPlantilla.ImpactTemplateIdsCsv` + `ImpactTemplateIds()`, the `AssignTo` ">=1 impact template" guard, and the admin picker).
- Edge-case defaults all confirmed: live category-field edits (submitted apps keep stored values; new required fields apply to drafts only); hard-delete of an in-use category blocked, deactivation allowed; changing an item's category clears prior field values; missing required category/impact values block submit with es-CR messages; a shared document survives until its last referencing quotation is removed; N quoted products require N line items.

Spec written to `specs/035-line-item-category-templates/spec.md`, reviewed **SOUND** (`REVIEW-SPEC.md`), reviewer brief at `review_brief.md`.

## Open Threads

- Should category-field values flowing into the AI quote-comparison context (spec 020) be subject to the existing PII/redaction boundary? — pin in plan.
- Is deactivating the *last* active impact template guarded, given per-item impact is now required to submit? — pin in plan.
- dacpac ordering for the new `CategoryField` / `CategoryFieldValue` tables, the item-keyed impact relocation, and the drop of `PlantillaImpactTemplates` — greenfield (no backfill), confirm in plan.
- Domain placement of the new invariants (category-field-clearing on category change; per-item impact required; document-retain-until-last-reference) on the `Item`/`Application` aggregates per Rich Domain Model — pin in plan.
- Whether the reviewer/applicant detail layout needs a per-line "category fields" sub-section design pass, or reuses the impact-values render pattern — design in plan.
