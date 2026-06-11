# Feature Specification: Searchable Dropdowns

**Feature Branch**: `031-searchable-dropdowns`
**Created**: 2026-06-11
**Status**: Draft
**Input**: User description: "I want all the dropdowns which values are data driven, not static values like status, but any other dropdown used either for filtering or for editing an entity, to provide an autocomplete. So I can start typing and it will narrow the options to those matching my typing against some of the content."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Filter a long data-driven list by typing (Priority: P1)

An admin standing in front of a filter toolbar (e.g. the Users, Suppliers, Processes, or Reports screens) wants to scope results to a particular Fund, Process, Group, supplier branch, or similar. Today they must open a long native dropdown and scroll to find the entry. With this feature, focusing the control lets them type a fragment of the option's name and the list narrows in real time to only the matching entries; they pick one and the filter applies exactly as before.

**Why this priority**: Filtering toolbars hold the longest data-driven lists (Funds, Processes, Groups can each grow without bound) and are used constantly by admins. This is where the scroll-and-hunt pain is worst, so it delivers the most value and is independently shippable.

**Independent Test**: Open an admin filter toolbar with a data-driven dropdown holding more than the threshold number of options, type a fragment of one option's display text, confirm only matching options remain, select one, and confirm the filtered result set matches what the plain dropdown would have produced for that same value.

**Acceptance Scenarios**:

1. **Given** a data-driven filter dropdown with more options than the threshold, **When** the user focuses it and types a substring that appears in some option labels, **Then** only options whose display text contains that substring (case- and accent-insensitive) remain visible.
2. **Given** the user has typed a fragment and one option is highlighted, **When** they press Enter, **Then** that option becomes the committed value and the dependent filter/result set updates as if the option had been chosen from the plain dropdown.
3. **Given** the user has typed a fragment that matches no option, **When** they blur the control, **Then** no new value is committed and the control retains its previously committed value (or the "all"/empty state for an optional filter).
4. **Given** a data-driven dropdown with options at or below the threshold, **When** the user focuses it, **Then** it behaves as the existing plain dropdown with no search box.

---

### User Story 2 - Pick an entity reference while editing a form (Priority: P1)

A user editing an entity (an applicant choosing the eligible Group or impact template for an application; an admin assigning a Fund or Plantilla to a Process; a user editing a quotation's supplier branch or currency) needs to select a value from a data-driven list. With this feature they type to narrow the list and commit a real option, so the form posts the correct id/code and persists exactly as before.

**Why this priority**: Edit forms are the other half of the request and carry data-integrity weight — the committed value is a foreign key. Searchability speeds entry while the "must pick a real option" rule protects the bound id. Independently shippable alongside or after US1.

**Independent Test**: Open an edit form containing a data-driven dropdown above the threshold, type a fragment, select a matching option, submit the form, and confirm the persisted entity references the same id/code that the plain dropdown would have submitted.

**Acceptance Scenarios**:

1. **Given** an edit form with a searchable data-driven dropdown, **When** the user types a fragment and selects a matching option, **Then** the form submits the selected option's underlying id/code unchanged.
2. **Given** a required data-driven dropdown, **When** the user types text matching no option and submits, **Then** server-side validation behaves identically to the case where no option was selected in the plain dropdown (no new or invalid value is fabricated from the typed text).

---

### User Story 3 - Search each level of a cascading control (Priority: P2)

A user working a multi-level control — the Fund → Process → Group cascade, the Province → Cantón → Distrito location cascade, or the spec-029 group drilldown — wants to type-filter each level. Choosing a parent re-narrows the child options as it does today; the search box on each level reflects the rebuilt option set.

**Why this priority**: Cascades are where lists are simultaneously longest and most numerous, but they layer on top of US1/US2 behavior and depend on the rebuild-refresh interaction, so they ship after the flat-control foundation is proven.

**Independent Test**: On a cascade, type-filter the parent level and select a value; confirm the child level's options rebuild to the parent-scoped set; then type-filter the child level and confirm the search box operates over the newly rebuilt options, not the stale set.

**Acceptance Scenarios**:

1. **Given** a Fund → Process → Group cascade where each level exceeds the threshold, **When** the user type-filters and selects a Fund, **Then** the Process level rebuilds to that Fund's processes and its search box filters over the rebuilt options.
2. **Given** the spec-029 group drilldown, **When** the user type-filters the group options, **Then** only matching groups for the chosen Process show, and checking/unchecking still accumulates selected groups as before.
3. **Given** the location cascade, **When** the user changes Provincia, **Then** the Cantón search filters over the newly loaded cantones for that province.

---

### Edge Cases

- **Accents and case**: Typing "jose" matches "José"; typing "CARTAGO" matches "Cartago". Matching ignores case and diacritics for es-CR display text.
- **No matches**: Typing a fragment that matches nothing shows an es-CR empty-state message ("Sin coincidencias") and commits nothing.
- **JavaScript disabled / enhancement failure**: The control falls back to the plain native `<select>`, which remains fully usable.
- **Threshold boundary**: A control with exactly the threshold number of options is NOT enhanced; one option above is enhanced.
- **Option set rebuilt at runtime** (cascade parent change): The search view refreshes to reflect the new options; any prior typed text is cleared or re-evaluated against the new set so no stale match lingers.
- **Pre-selected server value**: A control rendered with a server-committed selection displays that selection's label on first paint, before any typing.
- **Optional "all" filters**: Clearing the search and committing nothing returns the control to the empty/"all" state where that is a valid filter value.
- **Duplicate display labels**: Two options sharing a display label remain individually selectable by their distinct underlying values; selection commits the highlighted option's own value.
- **Keyboard-only and assistive-tech users**: The control is operable by keyboard alone and exposes combobox semantics so screen readers announce the role, the filtered option count, and the active option.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a reusable client-side enhancement that turns a flagged data-driven dropdown into a searchable combobox presenting a text field that filters the available options live as the user types.
- **FR-002**: Option matching MUST be a case-insensitive and accent-insensitive substring match against each option's display text, appropriate for es-CR content.
- **FR-003**: The committed value MUST always be one of the control's real options. Typed text MUST only filter and MUST NOT become a stored value. If the user leaves the control with no option chosen, it MUST retain its previously committed value, or the empty/"all" state for an optional filter.
- **FR-004**: The control MUST be fully keyboard operable — type to filter, Up/Down to move the highlight, Enter to commit the highlighted option, Escape to close without changing the committed value — and MUST expose accessible combobox semantics announceable by assistive technology.
- **FR-005**: The underlying native dropdown MUST remain the authoritative source of the selected value: its value and change notification continue to drive form submission and any dependent (cascade) logic. This feature MUST NOT change any server-side contract, request/response shape, route, or database schema.
- **FR-006**: The enhancement MUST apply only when a control's option count exceeds a configurable threshold (default 7). At or below the threshold the control MUST render as the existing plain dropdown.
- **FR-007**: The enhancement MUST apply to all in-scope data-driven controls: supplier branches, currencies, eligible groups, impact templates, funds, plantillas, and item categories; each level of the Fund → Process → Group cascade and the Province → Cantón → Distrito location cascade; and the group-options filter of the spec-029 group drilldown selector.
- **FR-008**: When a control's option set is rebuilt at runtime (e.g. a cascade parent changes its child's options), the searchable view MUST refresh to reflect the new option set, with no stale option or stale match remaining selectable.
- **FR-009**: Static enum-style dropdowns MUST be excluded from the enhancement: application status/state, user role, supplier verification status, process status, stage kind, identification type, and yes/no toggles.
- **FR-010**: All user-facing copy introduced by the control (search placeholder, empty-state, accessibility labels) MUST be in es-CR (e.g. placeholder "Escriba para filtrar…", empty-state "Sin coincidencias").
- **FR-011**: The enhancement MUST be progressive: with scripting unavailable or if enhancement fails for any control, that control MUST remain a usable native dropdown that submits the same value.
- **FR-012**: The feature MUST NOT introduce any new externally hosted (CDN) asset or any new managed/vendored third-party dependency; it MUST reuse the already-vendored UI styles.

### Key Entities

This feature is presentation-only and introduces no new data entities. It operates over option lists already produced for existing controls (Funds, Processes, Groups, supplier branches, currencies, eligible groups, impact templates, item categories, provinces/cantones/distritos).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For every in-scope data-driven control whose option count exceeds the threshold, a user can type a fragment of any option's display text and see the list narrowed to only matching options, then commit that option.
- **SC-002**: Committing a searched option submits and persists the exact same underlying id/code that the plain dropdown would have submitted for that option — verified by existing filter and edit flows producing identical results.
- **SC-003**: No server-side request shape, route, or database schema changes as part of this feature.
- **SC-004**: A keyboard-only user can locate and commit any option in an enhanced control without using a pointer, and assistive technology announces the control's combobox role and active option.
- **SC-005**: With scripting disabled, every in-scope control still allows selecting and submitting a value via the native dropdown.
- **SC-006**: Static enum dropdowns (status, role, verification status, process status, stage kind, identification type, yes/no) show no search box and are unchanged.
- **SC-007**: The filtered end-to-end tests for the affected admin and applicant surfaces pass, and the repository asset-budget check passes with the added assets.

## Assumptions

- The threshold default of 7 options is a reasonable cut-off below which a search box adds noise; it is configurable so it can be tuned without code changes to each control.
- "Data-driven" means the option list originates from the database/repository (entities, catalogs, geography). "Static" means a fixed enum/boolean set defined in code. The inventory in FR-007/FR-009 reflects the controls present as of this spec; controls added later inherit the same classification rule.
- Existing es-CR localization conventions (resource strings) are the mechanism for the new user-facing copy.
- The already-existing remote supplier autocomplete (AJAX prefix search input) already satisfies the typeahead intent for that surface and is intentionally left unchanged.
- UX quality is allowed to take precedence over end-to-end selector stability per repository conventions; where enhancing a control restructures its markup, the corresponding end-to-end tests/selectors may be updated, provided the committed value and server contract are preserved.
- The underlying native dropdown remaining in the DOM keeps existing browser-automation `selectOption`-style interactions viable for tests where practical.

## Out of Scope

- The existing remote supplier autocomplete (AJAX prefix search) — left as-is.
- Multi-select enum filters (e.g. report state multi-selects) and any free-text fields.
- Server-side / remote search for option lists (all in-scope lists are already rendered client-side); adding remote-paged search is a future consideration.
- Restyling or re-theming dropdowns beyond what the search affordance requires.
