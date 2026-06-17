# Review Guide: Programa Semilla Official Brand Alignment

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-17

---

## What This Spec Does

It re-skins the whole web app to the client's **official** Programa Semilla brand — the exact palette
from the brand book, the real logo files, a dark teal sidebar, cleaner tables, a tidier actions menu,
and the official partner-logo footer. It is visual-only: no backend, database, routes, permissions, or
features change. Think "make the existing app wear the real brand instead of the placeholder one we
shipped in spec 019."

**In scope:** Design tokens (palette), real logo/footer assets, shared chrome (sidebar/topbar/footer),
standardized page headers, filter cards + a "Limpiar filtros" action, de-zebra'd tables, a kebab
actions column, typography, favicon, a full applicant+reviewer+admin+auth sweep, and a narrow
re-tint of the two PDF brand images.

**Out of scope (where reviewer feedback matters most):** Any backend/business logic, schema,
route/action renames, localization copy, the PDF generation pipeline/layout/body (only the two brand
PNGs change), Tabler upgrade, and new dependencies. See [Out of Scope](spec.md#out-of-scope).

## Bigger Picture

This is the third turn of the same crank: [spec 011](../011-warm-modern-facelift/) built the
token-driven facelift system, [spec 019](../019-programa-semilla-brand/) pivoted it to a *PDF-sampled*
teal and placeholder logos, and **037 is the designer override [spec 019 explicitly anticipated in its
OQ-001](../019-programa-semilla-brand/spec.md#open-questions)** now that the official brand book exists.
Because the platform is already token-driven (`tokens.css` is the single raw-hex file + a Tabler
variable bridge), most of the work is a ~20-value token remap; the rest is asset swaps and a few
component restructures. The existing `tests/.../Brand/` E2E suite and `scripts/*` gate scripts were
built for exactly this kind of pivot and are reused. If you want to sanity-check the approach,
[research.md](research.md) D1–D13 is where the real engineering decisions live — it's more decision-
dense than the spec.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Skim [spec.md Purpose + Scope](spec.md#purpose) and then [research.md D1–D5](research.md). As you read:

- The whole thing hinges on "remap tokens, everything cascades." Is that confidence justified, or are
  there surfaces that bypass the token system? (Plan's answer: a per-role *sweep* task per story to
  catch stragglers — [T018](tasks.md), [T021](tasks.md), [T027](tasks.md). Is a manual sweep enough,
  or should there be a grep gate for stray `btn-primary`-without-token / inline hex in views?)
- The spec keeps the brand name text "Programa Semilla" and the `sidebar-brand`/`sponsor-strip`
  testids specifically so the existing brand E2E stays green ([research D13](research.md)). Does that
  feel like the right stability/clarity trade, or is it papering over real UI changes the tests should
  re-assert?

### Key decisions that need your eyes (12 min)

**Dark sidebar for ALL roles, including applicants** ([FR-015](spec.md#functional-requirements),
[research D1](research.md))
The sidebar is already dark (`navbar-dark`), so this is a re-tint to `#12343B`, not a restructure.
But applicants currently get a softer feel.
- Question: is a dark "enterprise admin" sidebar right for the applicant experience, or should
  applicants keep something lighter? (This was confirmed during brainstorming but is the most
  reversible big call.)

**PDF re-tint scoped to two PNG swaps** ([FR-023](spec.md#functional-requirements),
[research D4](research.md))
The user asked for UI and PDF to "match now," but the PDF has its own print-tuned teal `#1f6363` in
carve-out-guarded layout files. The plan swaps only the two brand PNGs (logo disc + partner strip) and
leaves the print CSS untouched, so the byte-identical carve-out gate stays green.
- Question: is "logo disc + partner strip read official teal, but printed body headings stay `#1f6363`"
  an acceptable definition of "match," or do you want the printed heading teal changed too (a bigger,
  carve-out-reopening change deferred to a future micro-spec via [OQ-004](spec.md#open-questions))?

**Kebab actions column** ([FR-020](spec.md#functional-requirements), [research D8](research.md))
Secondary row actions (Reenviar invitación / Restablecer / Inhabilitar) move into a `⋯` menu via a new
`_RowActionsMenu` partial, preserving every route/verb/testid.
- Question: T026 spreads the kebab to ~8 other admin tables for consistency. Is that the right blast
  radius for this PR, or should the kebab stay on the Users page this pass and roll out elsewhere
  later?

**`#F9A61C` orange held in reserve, not wired to status** ([research D5](research.md))
Orange fails AA as text, and re-pointing a status to it would be a semantics change the spec forbids.
- Question: the guideline says orange is for "pending/attention" states — are you OK with it being a
  reserved decorative token for now (no status currently uses it), or do you want a concrete surface
  to adopt it in this spec?

**Page background → off-white `#F6F8FA`** ([FR-004](spec.md#functional-requirements))
Spec 019 deliberately chose pure white; this re-introduces a faint tint (cards stay white).
- Question: does the off-white page read well behind dense admin tables, or keep pure white?

### Areas where I'm less certain (5 min)

- [research D6](research.md) (asset location): I chose to keep assets in `wwwroot/lib/brand/` rather
  than the guideline's suggested `wwwroot/images/brand/`. Reasonable, but if you have a convention I'm
  unaware of, this is a cheap change now and annoying later.
- [tasks.md T016/T038](tasks.md) (asset budget): the official files are **raster PNGs** replacing tiny
  SVGs. I believe they'll fit under 400 KB gz after optimization, but I haven't measured the actual
  provided files — this is the most likely place a task balloons (re-export/resize, or accept a budget
  bump). Flagged as [OQ-001](spec.md#open-questions).
- [tasks.md T020](tasks.md): the PDF-preview-iframe-against-new-chrome check is a manual visual step,
  not an automated assertion. If you consider that a coverage gap, it could become a snapshot test.

### Risks and open questions (5 min)

- If the auth-hero **vertical logo renders soft** at large display size (raster, not vector), do we
  accept it or request a vector original? ([OQ-001](spec.md#open-questions) / [NFR-005](spec.md#non-functional-requirements))
- The footer image swap **changes the partner set** (drops "10 años", adds "De la mano con su PYME").
  Confirmed acceptable, but it's a visible institutional change — worth a second look at
  [research D7](research.md).
- E2E churn: the kebab relocation forces an [AdminUsersListPage](tasks.md) page-object change and
  cascades to several user-admin test classes ([T028](tasks.md)). If those page objects are shared
  more widely than expected, T028 grows. Is the "preserve all `row-action-*` testids" strategy enough
  to contain it?
- Schema-freeze is asserted by [T040](tasks.md) (`git diff main -- src/FundingPlatform.Database/`
  empty). Cheap and decisive — good — but worth confirming no view change accidentally pulls in a
  migration.

---
*Full context in linked [spec](spec.md), [plan](plan.md), [research](research.md), and [tasks](tasks.md).*
