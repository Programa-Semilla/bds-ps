# Review Guide: Admin UX/UI Facelift (Spec 017)

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-08

---

## What This Spec Does

The `/Admin` landing page today is a frozen 3-card grid that pre-dates four shipped admin specs (009 Users + Reports stub, 010 Reports content, 015 multi-currency, 016 Groups). New capabilities only show up in the sidebar; admins discover them by spelunking. This spec replaces that landing with a capability-complete dashboard (4 action KPIs + 9 grouped capability cards + optional activity feed), then sweeps every admin sub-surface to spec 011's warm-modern bar (tokens, partials, illustrated empty states, voice-guide copy). It also normalizes three legacy `Admin*` routes, groups the sidebar under an "Administración" header, and re-templates the Reports tab strip + `_KpiTile`.

**In scope:** [`/Admin` dashboard rewrite](spec.md#user-story-1--admin-home-dashboard-capability-map--action-kpis-priority-p1); [10-surface sweep at FR-008](spec.md#sub-surface-sweep-us2); [illustrated empty states across every admin table](spec.md#empty-states-us3); [sidebar grouping under "Administración"](spec.md#sidebar-grouping-us4); [route normalization for AdminCurrencies / AdminExchangeRates / AdminLegacyQuotations](spec.md#route-normalization-us5); [Reports tab pill-chip + KPI ticker re-template](spec.md#reports-tab-ux-us6); [optional activity feed sourced from `AdminAuditEvent`](spec.md#activity-feed-us7-optional).

**Out of scope:** Schema edits ([FR-027](spec.md#cross-cutting), [SC-016](spec.md#measurable-outcomes)); new admin capabilities (no audit-log viewer surface, no bulk actions, no saved queue views — [OOS-2](spec.md#out-of-scope)); HTTP redirects from old admin routes ([FR-020](spec.md#route-normalization-us5) — pre-prod, no shim); localization ([OOS-4](spec.md#out-of-scope)); dark mode ([OOS-6](spec.md#out-of-scope)); any change to PDF carve-outs ([FR-028](spec.md#cross-cutting)). The sentinel admin's exclusion rules from spec 009 are explicitly preserved untouched ([OOS-10](spec.md#out-of-scope)).

## Bigger Picture

This is the admin-area peer to spec 011 (which lifted applicant + reviewer surfaces) and spec 008 (which seeded the partial library + illustration set). It is not introducing new capability — it is closing the consistency gap that opened after specs 010 / 015 / 016 each landed sidebar entries without ever updating the landing page. The dashboard's KPI tiles re-use the existing spec-010 `_KpiTile` (re-templated in place by US6, not forked); the activity feed re-uses the spec-008 `_EventTimeline`; the empty-state illustrations come from the existing 9-scene set. Two new partials (`_AdminDashboard`, `_CapabilityCard`) are the only structural additions.

The plan deliberately constrains blast radius: schema is locked closed, no new fonts/illustrations/managed deps, route renames are attribute-only (class names + namespaces stay), and the activity feed degrades to hidden when empty so US7 cannot block US1 delivery. The most aggressive scope call is treating the route rename as a hard 404 with no redirect shim — defensible only because the platform is pre-production. If real bookmarks exist anywhere external (analytics dashboards, internal docs, stored deep-links in test fixtures), this gets noisy.

This spec is also the first admin-area spec authored after the durable feedback memory captured: "UX/UI quality wins over E2E selector stability for facelift work." That's why the plan budgets a full POM rewrite across 9 sub-surfaces ([T059–T067](tasks.md#pom-rewrites-parallel-with-sweeps-where-the-new-html-is-stable)) instead of trying to preserve old selectors.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [spec.md Overview](spec.md#overview), [User Story 1](spec.md#user-story-1--admin-home-dashboard-capability-map--action-kpis-priority-p1), and the [Functional Requirements section for the dashboard (FR-001..FR-007)](spec.md#requirements-mandatory). As you read, consider:

- Are these the right 4 KPIs for an admin operator's daily workflow ([FR-002](spec.md#requirements-mandatory))? The spec calls out in [Assumptions](spec.md#assumptions) that the set is not locked — is that the cheapest hedge, or should v1 try to validate the set with admins before shipping?
- Is the section grouping (Usuarios y acceso / Catálogo / Operaciones) the right mental model ([FR-004](spec.md#requirements-mandatory)), or do "Currencies" and "Exchange Rates" belong in Operaciones rather than Catálogo since they're operational data, not a static catalog?
- The plan picks "section header is the navigable target to `/Admin`" for the sidebar grouping ([R3](research.md#r3--fr-015-implementation-choice-sidebar-grouping)). Does that survive accessibility scrutiny? Some assistive tech treats `<a>` headers oddly.

### Key decisions that need your eyes (12 min)

**Hard 404 on legacy admin routes, no redirect shim** ([FR-020](spec.md#route-normalization-us5), [SC-012](spec.md#measurable-outcomes), [R8](research.md#r8--route-normalization-scope))

The plan removes `/Admin/AdminCurrencies` etc. with no HTTP redirect, justified by "pre-prod, no real bookmarks." Class names + namespaces stay (`AdminCurrenciesController` keeps its name; only the route attribute changes).
- Question for reviewer: Is "pre-prod" the right characterization? Are there test fixtures, internal documentation links, Slack messages, or stored E2E specs that hard-code the old paths and would silently break? The plan only greps source files — it does not audit test data or documentation surfaces.
- Question for reviewer: Is "attributes only, class names unchanged" the right balance, or does keeping `AdminCurrenciesController` as a class name perpetuate the cognitive-load bug at the source-code level? [R8](research.md#r8--route-normalization-scope) defers a class rename to a future "admin module reorganization" spec — is that future spec ever going to ship, or is this just deferring the pain forever?

**KPI failure mode: render `0`, log WARN, no error tile** ([R2](research.md#r2--pending-supplier-failure-mode), [FR-002](spec.md#requirements-mandatory))

When a sub-projection throws (DB hiccup, missing config, etc.), the tile renders `0` rather than `—` or a red error badge. The rationale: all four sources are in-process repository queries, so failure is a logic bug not a network partition.
- Question for reviewer: Does silent `0` mask real bugs? An admin staring at "0 pending suppliers" when the query is broken has no signal that anything is wrong; their next action is to do nothing. Would a small visual diff (e.g., the count rendered with a muted "no data" state visible to the admin) be worth the divergence from `_KpiTile`'s contract?
- Question for reviewer: The WARN log lands in structured logs; is anyone actually watching those for the pre-prod environment? If not, this failure mode is functionally silent.

**Activity feed scope and graceful degrade** ([FR-024](spec.md#activity-feed-us7-optional), [User Story 7](spec.md#user-story-7--admin-index-activity-feed-priority-p3))

The feed is P3, hides entirely when zero events, and only consumes the existing 4-action `AdminAuditEvent` vocabulary ([R5](research.md#r5--activity-feed-event-copy-mapping)) — no expansion of the model.
- Question for reviewer: Is an admin dashboard's "wow moment" really hurt by shipping without the feed, or is it a decoration we should drop entirely (cutting US7) to reduce surface area? The plan budgets [4 tasks (T037–T042)](tasks.md#phase-5-user-story-7--admin-index-activity-feed-priority-p3) on a P3 with hide-when-empty; that's reasonable but not negligible.
- Question for reviewer: The feed's deep-link to `/Admin/Groups/{id}/Edit` for `group.delete` events deliberately renders without a link ([R5](research.md#r5--activity-feed-event-copy-mapping)). Is that the right UX, or would a tombstone link to a "this group no longer exists" page be more useful for audit trails?

**Sweep parallelism budget** ([Phase 7](tasks.md#phase-7-user-story-2--sub-surface-sweep-at-warm-modern-bar-priority-p1), [T047–T067](tasks.md#phase-7-user-story-2--sub-surface-sweep-at-warm-modern-bar-priority-p1))

Nine surfaces × {sweep, POM rewrite} = ~18 parallelizable tasks, the largest concurrent burst in the plan. The plan flags this as "one team per sub-surface" if `spex-teams` is enabled.
- Question for reviewer: Is the partial inventory (`_PageHeader`, `_DataTable`, `_StatusPill`, `_EmptyState`, `_ActionBar`, `_ConfirmDialog`, `_FormSection`) stable enough that 9 concurrent rewrites won't trip on shared partial edits? [R7](research.md#r7--existing-partial-inventory-confirmation) confirms the partials exist, but does not confirm none of the 9 sweeps will need to extend a shared partial in conflicting ways.
- Question for reviewer: The voice-guide pass [T057](tasks.md#phase-7-user-story-2--sub-surface-sweep-at-warm-modern-bar-priority-p1) is sequenced *after* the structural sweeps. If a sweep introduces new copy that violates the voice guide, the rework cost is real. Should voice-guide review run *during* each surface sweep instead?

### Areas where I'm less certain (5 min)

- [`Views/Admin/Index.cshtml` callers](spec.md#assumptions): The spec's last assumption claims the legacy 3-card layout has zero callers other than the sidebar. The plan ([T076](tasks.md#phase-9-user-story-5--route-normalization-priority-p2)) greps source for old route strings but I'm not sure whether it greps for *view file references* (e.g., a partial including the legacy index, breadcrumb code that names the view, redirects from elsewhere). If a controller anywhere does `return View("~/Views/Admin/Index.cshtml", legacyModel)`, the rewrite breaks it. I read this as a planning-time grep step that may or may not exist.
- [Phase 7 sweep coverage of empty-state branches](tasks.md#phase-7-user-story-2--sub-surface-sweep-at-warm-modern-bar-priority-p1) vs. [Phase 4 empty-state edits](tasks.md#phase-4-user-story-3--illustration-backed-empty-states-priority-p1): both phases edit the same `Index.cshtml` files. Phase 4 lands first ([per the dependency graph](tasks.md#dependencies)), but Phase 7 then re-touches them for the broader 7-criteria sweep. This is intentional ordering, but the merge surface between Phase 4 and Phase 7 is the same view — an implementer running Phase 7 on a surface where Phase 4 hasn't shipped yet would hit a conflict. The dependency graph is "Phase 4 → Phase 7" in spirit, but tasks.md doesn't mark this explicitly.
- [`AgingThresholdDays` is the spec-010 config](spec.md#assumptions): plan and data-model both reference reusing it. I did not verify that the config is actually exposed in the way the projection needs (singleton vs. per-request, sync vs. async). If it's bound through `IOptions<T>`, the projection signature is clean; if it's hardcoded somewhere odd, a small wiring task is missing.
- [The `[P]` tags](tasks.md#format-validation): I count 27 story-labeled tasks but the story labels reflect intent, not strict mutual exclusion. The plan's notion of "parallel" (different files, no incomplete-task dependency) holds for the sweep tasks, but several setup/foundational tasks are marked `[P]` despite being interface scaffolding that downstream tasks immediately consume. Is that the right model, or should `[P]` be reserved for tasks where the parallel actor genuinely doesn't need to coordinate?

### Risks and open questions (5 min)

- If the demo seed in dev is rich enough to populate KPIs but sparse enough to leave the activity feed empty, the dashboard will look "wow" with the feed hidden. Will reviewers / designers ever see the populated-feed state during the SC-021 designer/product sign-off? [SC-021](spec.md#measurable-outcomes) requires sign-off against the four reference fixtures from [SC-002](spec.md#measurable-outcomes), but only one of those (the prod-like dataset) is the demo seed.
- The sweep grep for `style=` ([T056](tasks.md#phase-7-user-story-2--sub-surface-sweep-at-warm-modern-bar-priority-p1), [SC-006](spec.md#measurable-outcomes)) would also catch `data-style=` or `style="display:none"` toggles that some Tabler.io components emit. Is the grep regex tight enough, or will the sweep require a later carve-out for partials we don't own? [Tabler vendored CSS/JS is in `wwwroot/lib/`](spec.md#assumptions) and the grep is scoped to `Views/Admin/**`, so this is probably fine — but worth a sanity check.
- The wire-weight budget is < 30 KB gzipped ([FR-030](spec.md#cross-cutting), [SC-020](spec.md#measurable-outcomes)). Two new partials plus a re-templated `_KpiTile` with a JS ticker should fit easily, but the plan does not list a hard ceiling per partial. If the dashboard composition adds inline SVG illustrations or a large client-side ticker, the budget tightens fast.
- Designer/product sign-off ([SC-021](spec.md#measurable-outcomes)) is recorded as a PR description item. Is there a person designated to give that sign-off, and do they review during the PR cycle, or is this a self-assessment item the implementer fills in? The success criterion only says "recorded as an explicit review item," not "approved by named reviewer."

---

*Full context in linked [spec](spec.md), [plan](plan.md), [research](research.md), [data-model](data-model.md), [quickstart](quickstart.md), and [ADMIN-SWEEP-CHECKLIST](ADMIN-SWEEP-CHECKLIST.md).*
