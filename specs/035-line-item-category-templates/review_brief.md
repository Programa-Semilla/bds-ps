# Review Brief: Line-Item Category Templates, Per-Item Impact, and Quotation Reuse

**Spec:** specs/035-line-item-category-templates/spec.md
**Generated:** 2026-06-12

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Reshapes the applicant submission flow around the line item. Each submission **category** gains an admin-configured set of fields ("category template") that render dynamically when an applicant picks that category — replacing the free-text technical-specifications field. **Impact** moves from one application-wide choice to a per-line-item choice. A vendor quotation that covers several products is captured as one line item per product, but the vendor and uploaded document are entered once and **reused** by sibling line items (each keeping its own price). The obsolete application-level impact wiring and the per-process impact-template gating are removed entirely, with no dead code left behind. The flow is greenfield, so no data migration is needed.

## Scope Boundaries

- **In scope:** Admin category-field configuration; category-driven dynamic line-item fields; per-item impact (any active impact template, no process gating); quotation reuse within one application; rendering category values + per-item impact on every application surface (applicant, reviewer, admin, funding-agreement PDF, AI comparison context); full removal of the old application-level impact and Plantilla impact-template gating.
- **Out of scope:** Data migration; cross-application quotation reuse; conditional/file/dropdown field types and custom per-field validation; a standalone shared category-template catalog; any change to the minimum-quotations rule or required-field flags.
- **Why these boundaries:** The three changes converge in one UI flow, so they ship together (single spec). Everything deferred is either speculative (YAGNI) or a separate subsystem.

## Critical Decisions

### Category owns its field set 1:1
- **Choice:** A category *has* its fields directly; no separate reusable template entity shared across categories.
- **Trade-off:** Simpler model and admin UX; the cost is duplicate definition if two categories need identical fields.
- **Feedback:** Is any field set genuinely shared across categories today such that 1:1 will cause real duplication?

### Impact relocates fully to the line item, ungated
- **Choice:** Application-level impact is removed; each line item picks from *any active* impact template (the Plantilla no longer gates impact).
- **Trade-off:** Honors "impact per line item" and removes per-process governance of impact templates; admins lose the ability to restrict which impact templates a process offers.
- **Feedback:** Confirm the team is comfortable dropping per-process impact-template restriction entirely.

### Quotation reuse = share vendor + document, per-item price
- **Choice:** Reuse carries over supplier/branch and the uploaded document; each line item keeps its own price/currency/validity in its own quotation row. Editing one does not affect others.
- **Trade-off:** Matches real multi-product quotes and preserves every existing per-item invariant; rejected the "one shared quotation record" model that would force a single price across lines.
- **Feedback:** Is per-line price the right semantics, or do any real quotes apply one indivisible price across products?

## Areas of Potential Disagreement

### "No dead code" teardown is in-scope, first-class work
- **Decision:** FR-010/011/012 require fully removing application-level impact and the Plantilla impact-template gating, verified by codebase search (SC-003).
- **Why this might be controversial:** It's a sizable refactor touching spec-021 wiring (reviewer views, PDF, projections) layered onto a feature delivery.
- **Alternative view:** Leave the old impact path dormant and only add the new one.
- **Seeking input on:** Confirm the teardown should land in this spec rather than a follow-up.

### Single large spec vs. decomposition
- **Decision:** One spec for all three changes.
- **Why this might be controversial:** Each change is independently shippable; a single spec means a larger PR and a half-rebuilt flow if split mid-stream.
- **Alternative view:** 2–3 sequential specs.
- **Seeking input on:** Stakeholder already chose single-spec; flagging for awareness.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Per-category field set | "category template" (a category owns its fields) | Mirrors "impact template" vocabulary |
| Per-field type set | text / decimal / integer / date | Reuses the existing impact data-type set |
| Reuse scope | same application only | Cross-application reuse explicitly excluded |
| Removed field | technical specifications (free text) | Replaced by category fields |
| Preserved on Plantilla | minimum-quotations-per-item, required-field flags | Only the impact-template association is removed |

## Open Questions

- [ ] Should category values flowing into the AI quote-comparison context respect the spec-020 PII/redaction boundary? (planning)
- [ ] Is deactivating the *last* active impact template guarded, now that per-item impact is required? (planning)
- [ ] Should the now-vestigial Plantilla impact-template admin UI be removed in the same PR or staged? (resolved: same PR, per "no dead code")

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Teardown of spec-021 application-impact read paths misses a surface | Med | SC-003 search gate + SC-004 enumerates all five render surfaces |
| New free-text category fields leak PII into AI prompts | Med | Planning question to extend spec-020 redaction boundary |
| Shared-document deletion semantics (last-reference retention) implemented wrong | Med | Explicit edge case + acceptance scenario; entity-level invariant |
| es-CR copy gaps in new admin + applicant UI | Low | FR-013 + SC-005; existing localization conventions |

---
*Share with reviewers before implementation.*
