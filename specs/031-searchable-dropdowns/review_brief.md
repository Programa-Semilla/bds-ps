# Review Brief: Searchable Dropdowns

**Spec:** specs/031-searchable-dropdowns/spec.md
**Generated:** 2026-06-11

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Data-driven dropdowns (those whose options come from the database/catalogs — Funds, Processes, Groups, supplier branches, currencies, eligible groups, impact templates, item categories, geography) become **searchable**: focus the control, type a fragment, and the list narrows in real time to matching options. It applies to both filtering toolbars and entity-edit forms. Static enum dropdowns (status, role, etc.) are untouched. The change is presentation-only — no server, route, or schema changes.

## Scope Boundaries

- **In scope:** A reusable client-side enhancer over all in-scope data-driven controls, including each level of the Fund→Process→Group and Province→Cantón→Distrito cascades and the spec-029 group drilldown's group filter.
- **Out of scope:** The existing remote supplier autocomplete (already a typeahead), multi-select enum filters, free-text fields, and any server-side/remote-paged option search.
- **Why these boundaries:** The request targets long *data-driven* lists. Static enums don't benefit, and remote search isn't needed since these lists already render client-side.

## Critical Decisions

### In-house vanilla enhancer, not a vendored library
- **Choice:** Build a small in-house progressive enhancer rather than vendor Tom Select / select2.
- **Trade-off:** More code we own vs. honoring the repo's no-CDN / reuse-vendored / spec-approval-for-deps conventions and avoiding an asset-budget hit.
- **Feedback:** Comfortable owning a small combobox module instead of adopting a library?

### Native `<select>` stays authoritative
- **Choice:** The real `<select>` remains in the DOM; the combobox is a view over it. Its value/change still drive submission and cascades.
- **Trade-off:** Slightly more DOM bookkeeping vs. zero server/contract changes and maximal test/back-compat safety.
- **Feedback:** Agree this is the right integrity guarantee for id/FK-bound fields?

### Threshold default of 7
- **Choice:** Only enhance controls with more than 7 options (configurable).
- **Trade-off:** Consistency (everything searchable) vs. not adding a search box over 2–3 items.

## Areas of Potential Disagreement

### Must pick an existing option (no free text)
- **Decision:** Typed text only filters; the committed value is always a real option.
- **Why this might be controversial:** Some users like typing a brand-new value inline.
- **Alternative view:** Allow free-text fallback for certain fields.
- **Seeking input on:** Any in-scope control where free-typed values should be accepted? (Current answer: none.)

### E2E selector churn
- **Decision:** Where enhancing a control restructures markup, affected E2E selectors/page objects may be rewritten (UX quality wins per conventions).
- **Why this might be controversial:** Touches existing green tests.
- **Alternative view:** Keep markup frozen to avoid any test edits.
- **Seeking input on:** Acceptable to update page objects for affected surfaces.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Opt-in flag (tentative) | `data-searchable` | Marks a select for enhancement; final mechanism decided in plan.md |
| Search placeholder | "Escriba para filtrar…" | es-CR copy |
| Empty state | "Sin coincidencias" | es-CR copy |
| Threshold default | 7 | Min option count to enhance |

## Open Questions

- [ ] Exact opt-in mechanism (`data-searchable` attribute vs. auto-detecting data-driven selects) — deferred to plan.md.
- [ ] Whether affected Playwright page objects target the retained native select or the combobox input — deferred to plan.md.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Combobox/native-select desync breaks form submission | High | FR-005 keeps native select authoritative; SC-002 verifies value equivalence |
| Cascade rebuild leaves stale filtered options | Med | FR-008 requires the search view to refresh on option rebuild |
| E2E breakage on restructured markup | Med | Filtered E2E gate (SC-007); page objects updated where needed |
| Accent handling wrong for es-CR | Med | FR-002 mandates accent-insensitive matching; edge cases enumerated |

---
*Share with reviewers before implementation.*
