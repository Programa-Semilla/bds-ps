# Brainstorm: Searchable Dropdowns

**Date:** 2026-06-11
**Status:** spec-created
**Spec:** specs/031-searchable-dropdowns/

## Problem Framing

Data-driven dropdowns (Funds, Processes, Groups, supplier branches, currencies, eligible groups, impact templates, item categories, geography) can grow long, forcing users to scroll-and-hunt in native `<select>`s on both filter toolbars and entity-edit forms. The user wants type-to-filter autocomplete on every *data-driven* dropdown, while leaving *static* enum dropdowns (status, role, etc.) alone. A side requirement: bundle the existing uncommitted admin filter/cascade work onto the same branch as this feature.

## Approaches Considered

### A: In-house vanilla progressive enhancer (CHOSEN)
- Pros: No new vendored/managed dependency (honors no-CDN / reuse-vendored / spec-approval conventions); keeps the real `<select>` authoritative so server binding, cascade logic, and Playwright `selectOption` keep working; small asset-budget footprint; layers cleanly on the existing catalog-JSON cascades.
- Cons: We own the combobox + a11y code rather than getting it from a library.

### B: Vendor Tom Select (or select2)
- Pros: Full-featured combobox out of the box (multi-select, theming, remote).
- Cons: New front-end asset needing spec approval; asset-budget hit; heavier than the need.

### C: Native `<datalist>`
- Pros: Zero JS.
- Cons: Inconsistent cross-browser styling, weak keyboard UX, doesn't bind cleanly to a hidden id — not production-grade.

## Decision

Chose **A**. Locked sub-decisions: applies to ALL data-driven controls incl. cascades & the spec-029 group drilldown; user MUST pick an existing option (typed text only filters, never becomes a value); enhancement only above a configurable option-count threshold (default 7); matching is case- and accent-insensitive for es-CR. Spec `031-searchable-dropdowns` created and passed the spec-review gate (SOUND, no critical/important issues). Branch `031-searchable-dropdowns` was cut from `main` carrying the 57 pending working-tree changes so the prior admin-filter/cascade work ships together with this feature.

## Open Threads

- Exact opt-in mechanism (`data-searchable` attribute vs. auto-detecting data-driven selects) — deferred to plan.md.
- Whether affected Playwright page objects target the retained native `<select>` or the combobox input — deferred to plan.md.
