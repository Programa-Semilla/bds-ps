# Review Guide: Searchable Dropdowns

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-11

---

## What This Spec Does

Long data-driven dropdowns (Funds, Processes, Groups, supplier branches, currencies, geography, impact templates, item categories) become searchable: you focus the control, type a fragment, and the option list narrows in real time. It applies to both filter toolbars and entity-edit forms. Static enum dropdowns (status, role, etc.) are deliberately left alone. The whole thing is client-side progressive enhancement — the native `<select>` stays in the DOM and keeps posting the same value, so nothing changes on the server.

**In scope:** A single in-house vanilla-JS enhancer over every data-driven `<select>` flagged `data-searchable`, including each level of the Fund→Process→Group and Province→Cantón→Distrito cascades and the spec-029 group drilldown's group filter. Two es-CR strings, a small CSS block, ~10 view edits, an E2E helper, and per-story E2E.

**Out of scope:** The existing remote supplier autocomplete (already a typeahead, left untouched); multi-select enum filters; free-text fields; any server-side/remote-paged option search. These boundaries are worth a reviewer's eye — see the questions below on whether "all data-driven, client-rendered lists only" is the right line.

## Bigger Picture

This is the natural finish to a thread that's been running through the admin UX work: spec-021's supplier autocomplete, spec-025's location cascade, spec-029's Fund hierarchy + group drilldown, and the (now bundled) pre-existing cascade-filter toolbars. Each added a data-driven control; none made them type-to-filter. This spec generalizes that one interaction across all of them with a single reusable enhancer rather than another one-off script.

The deliberate decision **not** to vendor a combobox library (Tom Select / select2) is the consequential one. The repo's conventions (no CDN, reuse-what-is-vendored, spec-approval for new managed deps, a 400 KB asset budget) push toward owning a small module instead. That's cheaper to ship but means we own combobox accessibility ourselves — the place a library would normally earn its keep. The plan leans on the WAI-ARIA combobox pattern ([research.md R8](research.md)) to mitigate that; whether our hand-rolled a11y is good enough is a fair thing to probe.

---

## Spec Review Guide (30 minutes)

> Focus your time on the boundary decisions and the two interaction-correctness risks; the mechanics are conventional.

### Understanding the approach (8 min)

Read [spec.md User Scenarios](spec.md#user-scenarios--testing) and [research.md R1–R3](research.md). As you read, consider:

- Is "searchable only when option count exceeds 7" ([FR-006](spec.md#requirements)) the right default, or will mid-size lists (8–12 funds) feel like they got a search box they didn't need? The threshold is per-control overridable — is that enough?
- The enhancer keeps the native `<select>` as the authoritative posted value and drives it via a bubbling `change` ([research.md R3](research.md)). Does that feel like the right integrity guarantee, or would you expect a cleaner "replace the select entirely" design despite the form-binding/cascade cost?
- Opt-in is an explicit `data-searchable` attribute, not auto-detection ([research.md R2](research.md)). The justification is that the client can't tell an entity-id select from an enum select. Convincing, or is there an opt-out posture you'd prefer?

### Key decisions that need your eyes (12 min)

**No vendored library** ([plan.md Technical Context](plan.md#technical-context), [research.md R1](research.md))
We build `searchable-select.js` instead of adopting Tom Select. Alternatives were genuinely viable.
- Question: Given we now own combobox keyboard + ARIA + screen-reader behavior, is that an acceptable maintenance bet versus a ~40 KB vendored asset and a one-time spec-approval? Where would you want the line drawn?

**Cascade refresh via MutationObserver** ([research.md R4](research.md), [data-model.md state transitions](data-model.md#state-transitions))
When a parent selection rebuilds a child's `<option>`s, a per-select `childList` observer refreshes the combobox and re-evaluates the threshold — chosen specifically to avoid coupling the enhancer to each cascade script's internals.
- Question: Is observing DOM mutations the right decoupling, or would an explicit `options:rebuilt` event from the cascade scripts be more predictable to debug later? (T008 owns this; T012/T025 verify it.)

**Group drilldown gets a different treatment** ([research.md R9](research.md), [tasks.md T024](tasks.md))
The drilldown's group level is checkboxes, not a `<select>`, so it gets an in-place text filter inside `group-drilldown-selector.js` rather than the generic enhancer.
- Question: Is a filter-over-checkboxes the right call (preserves spec-029 multi-select + chips), or would you rather see that surface converted to a multi-select combobox for consistency? The plan chose to preserve the established UX.

**E2E drives the combobox, not the hidden select** ([research.md R10](research.md), [contracts/searchable-select.md §5](contracts/searchable-select.md))
Affected page objects move to a `SearchableSelect` helper. This touches existing green suites.
- Question: Comfortable with E2E page-object churn on the migrated suites (Users/Suppliers/Process/ExchangeRates), given the repo convention that UX quality outranks selector stability?

### Areas where I'm less certain (5 min)

- [spec.md FR-007](spec.md#requirements) vs [User Story 3](spec.md#user-scenarios--testing): the Fund→Process→Group cascade is the same component in both a US1 filter toolbar and US3's cascade scenarios. I assigned the component edit to US1 and the location/drilldown cascades + cross-cascade rebuild verification to US3 ([tasks.md Phases 3 & 5](tasks.md)). That keeps each story independently testable, but a reviewer might reasonably want all cascade behavior under US3. Is the split sensible?
- [plan.md Performance Goals](plan.md#technical-context) state "<16ms/keystroke" but there's no measurement task — I judged a perf harness as overkill for in-memory filtering of low-hundreds-size lists (YAGNI per Constitution VI). If you expect a control with thousands of options, that assumption breaks and we'd want remote search (explicitly deferred). Are any in-scope lists actually that large?
- [contracts/searchable-select.md §2](contracts/searchable-select.md): I derive the combobox input's `data-testid` as `<source>-search`. If any existing page object already uses a `-search` suffix on a sibling element, that could collide. I didn't exhaustively audit for it.

### Risks and open questions (5 min)

- If the native `<select>` is visually hidden but kept for binding, does any existing automation that calls `SelectOptionAsync` against an enhanced control silently break before its page object is migrated ([tasks.md T011/T020](tasks.md))? The mitigation is migrating those suites in the same story phase — is the sequencing tight enough?
- Accent folding uses `normalize('NFD')` + combining-mark strip ([research.md R5](research.md)). Does that correctly handle every es-CR display string we render (e.g. names with `ñ`, which should stay distinct from `n`)? Worth a sanity check on the matcher's treatment of `ñ`.
- Are there data-driven dropdowns the inventory missed? The spec enumerates a fixed list ([FR-007](spec.md#requirements)); a reviewer who knows a surface I didn't sweep (e.g. a reviewer-side select) could catch a gap before implementation.

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
