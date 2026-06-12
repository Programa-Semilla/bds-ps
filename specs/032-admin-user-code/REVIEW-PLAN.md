# Review Guide: Admin-only user provisioning + unique applicant User Code

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-11

---

## What This Spec Does

Today anyone can self-register at `/Account/Register` and is auto-granted the applicant role. This feature shuts that door — accounts are created only by an administrator — and gives each applicant an admin-assigned **User Code**: a free-text, ≤50-char, unique identifier that becomes a searchable key alongside name, email, and the applicant's national identification, everywhere people are searched.

**In scope:** registration removal (→ 404); a new `UserCode` on the applicant, required + unique for the Solicitante role; read-only display on the applicant's profile; and widening the existing search box on five surfaces (admin users list, reviewer queue + its row-refresh, and the Applications/Applicants/Aging reports + applicants CSV) to also match identification and User Code.

**Out of scope:** the pre-existing `CodigoPersonal` account field (left untouched), supplier search, backfilling/bulk-importing codes, any self-service onboarding replacement, and User-Code format rules beyond length + uniqueness. These are called out in [spec Out of Scope](spec.md#out-of-scope) — the boundary between "new `UserCode`" and the old `CodigoPersonal` is the one most worth a reviewer's eye.

## Bigger Picture

This sits directly on top of the search/identity work the project has been accreting: spec 026 added typed identification (`LegalId` + `IdentificationType`) to the applicant, spec 016 added the reviewer-queue search, and spec 010 the report searches. This feature reuses all of those seams rather than inventing new ones. It also echoes two recent patterns: the filtered-unique-index + "duplicate is E2E-only" handling from [spec 030's `UX_Processes_Name`](plan.md#summary), and the role-driven show/hide field already used for `LegalId` on the admin form. The one genuinely new product concept is a **second** admin code on a user — see the uncertainty note below.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [User Story 2](spec.md#user-story-2---administrator-assigns-a-unique-user-code-to-each-applicant-priority-p1) and [research D1–D3](research.md#d1-where-does-usercode-live--applicant-entity-vs-aspnetusers). As you read:

- The code is stored on the **applicant**, not the account ([D1](research.md#d1-where-does-usercode-live--applicant-entity-vs-aspnetusers)). Does that match how you think about it? It means non-applicant users can never have one — which is the intent, but it also means the admin users list (an account-based query) reaches the code through a sub-query. Reasonable?
- "Required" is enforced at the controller, while the column stays nullable ([D3](research.md#d3-required-for-solicitante-placement)). Is "required for the role, nullable in storage" the right model, given legacy applicants must stay valid without a code?

### Key decisions that need your eyes (12 min)

**Two distinct codes on a user** ([spec Assumptions](spec.md#assumptions), [FR-006](spec.md#functional-requirements))
The user chose a brand-new `UserCode` rather than extending the existing `CodigoPersonal` (40→50). 
- Question: is a future maintainer going to be confused seeing two free-text "codes" on a person? Should the long-term intent (keep both vs. reconcile) be written down now, or is that premature?

**`/Account/Register` returns 404, not a redirect** ([FR-002](spec.md#functional-requirements), [D7](research.md#d7-register-removal-mechanics--404-with-no-leftover-links))
We delete the action so the route stops resolving.
- Question: is a hard 404 acceptable UX for a bookmarked sign-up link, versus a 302 to `/Account/Login`? (This was an explicit user choice; flagging only so you can veto it.)

**Reviewer queue is match-only, no code column** ([D6](research.md#d6-surfacing-the-usercode-value-as-a-column-fr-016-discretionary), [FR-016](spec.md#functional-requirements))
The admin list and applicants report/CSV get a visible "Código de usuario" column; the reviewer queue only *matches* on it.
- Question: would a reviewer triaging the queue actually want to see the code as a column, or is keeping the queue row's micro-timeline uncluttered the right call?

**Uniqueness has two enforcement points** ([D2](research.md#d2-uniqueness-over-a-nullable-column), [tasks T018/T019/T004](tasks.md#phase-4-user-story-2--admin-assigns-a-unique-user-code-priority-p1))
A service `AnyAsync` pre-check gives the friendly es-CR message; the filtered unique index is the concurrency backstop.
- Question: is the duplicated guard (service + DB) worth it, or does it risk drifting out of sync? (The project already does exactly this for `LegalId`, which argues for consistency.)

### Areas where I'm less certain (5 min)

- [FR-013](spec.md#functional-requirements) / [T033](tasks.md#phase-5-user-story-3--widen-search-to-identification--user-code-priority-p2): I assumed `/Review` and `/Review/QueueRows` share the single repository method `GetByStateForReviewerAsync`, so one edit covers both. If they've diverged, QueueRows needs its own edit — worth a 30-second confirstmation against the controller.
- [FR-015 accent-insensitivity](spec.md#functional-requirements): I treated this as "whatever the DB's current collation does" (the existing `Contains`/`LIKE` behavior), not a new normalization step. If the team expects explicit accent folding (like the spec-031 NFD strip), that's a larger change than planned here.
- [Edge case "role changed away from Solicitante"](spec.md#edge-cases): the plan **retains** the existing code rather than clearing it ([data-model state notes](data-model.md#state--lifecycle-notes)). That's an assumption — some teams would prefer clearing it. No test currently asserts the retain behavior.

### Risks and open questions (5 min)

- If a new people-search surface exists that the recon missed, "any other screen" ([SC-003](spec.md#measurable-outcomes)) wouldn't be fully satisfied. [T001](tasks.md#phase-1-setup) inventories `Register` refs but there is no equivalent grep task for *search* surfaces — should there be one before US3 closes?
- Adding a nullable column to a populated `dbo.Applicants` is migration-safe ([D2](research.md#d2-uniqueness-over-a-nullable-column)), but the filtered unique index will fail to create if the live table already contains duplicate non-null codes. It can't today (the column is new), but worth knowing if any pre-seed exists.
- The Applicants CSV export inherits the widened predicate via the shared `ListApplicantsRequest` path ([D5](research.md#d5-reviewer-queue--reports--match-additions)). If a reviewer believes the CSV uses a different query, that assumption ([T036/T038](tasks.md#phase-5-user-story-3--widen-search-to-identification--user-code-priority-p2)) deserves a check.

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
