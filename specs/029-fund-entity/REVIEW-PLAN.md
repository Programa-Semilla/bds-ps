# Review Guide: Fund (Fondo) Entity

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-10

---

## What This Spec Does

Adds a **Fund** (Fondo) as the top-level container above `Process`, so the platform can express *which fund a process draws from* and *what regulation governs it*. Each Process now belongs to exactly one Fund; a Fund carries a name, description, and an optional regulation PDF that applicants can download. Admins manage Funds and can archive them.

**In scope:** Admin Fund CRUD + Active/Archived lifecycle; one optional regulation PDF per Fund (upload/replace/remove, applicant download); required Fund on Process create/edit; Fund column/filter on the Process list and existing reports; **(added during planning)** an authoritative `Application → Group` anchor and a force-freeze of applications under an archived Fund.

**Out of scope:** Fund→Groups/Participants rollup reports; multiple regulation docs / versioning; per-Fund permissions; re-anchoring an application after creation; tightening the reviewer visibility predicate to use the new anchor; hard delete. (See [Out of Scope](spec.md#out-of-scope).)

## Bigger Picture

The Fund entity itself is a small, conventional addition (it mirrors `Process`). The weight of this feature comes from two facts the original brainstorm assumed away and the product owner chose to confront head-on — both recorded in [Planning Evolution](spec.md#planning-evolution-2026-06-10):

1. **Applications were never anchored to a Process.** Today an application is tied only to its applicant; its Process/Plantilla/Fund is inferred from the applicant's reviewer-scoping group memberships and is ambiguous when those span multiple Processes (Plantilla validation literally uses `FirstOrDefault`). This spec introduces a real `Application.GroupId` anchor — a latent data-model gap being closed under the banner of "Funds."
2. **The lifecycle had no freeze primitive.** `Process.Close()` *blocks* while work is in flight; it never froze anything. This spec adds the platform's first "freeze a slice of applications" mechanism (`ExcludeArchivedFund` + mutation guards).

So a reviewer's most valuable attention is less on "is the Fund entity right" and more on "are we comfortable changing how applications relate to the hierarchy, and introducing a freeze primitive, as part of this feature?"

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [Planning Evolution](spec.md#planning-evolution-2026-06-10) and [User Story 6](spec.md#user-story-6---anchor-each-application-to-its-fund-at-creation-priority-p1), then [research D5](research.md#d5--authoritative-applicationgroupid-anchor-product-owner-call-add-authoritative-applicationprocess-fk). As you read:

- The anchor is the **Group**, not the Process — Process and Fund derive from it. Does anchoring at the Group level (rather than Process) feel right, given applicants hold *group* memberships? ([data-model.md](data-model.md#modified-application--authoritative-anchor-fr-017))
- The anchor is captured **at application creation** and fixed thereafter ([FR-018](spec.md#requirements-mandatory)). Is "fixed at creation, no re-anchoring" acceptable, or will admins need to move an application between Processes later? (Currently deferred.)
- Was folding this data-model change into the "Fund" feature the right call, or should the anchor have been its own spec? It's defensible either way — your judgment is wanted here.

### Key decisions that need your eyes (12 min)

**New applicant-facing step at application creation** ([FR-018](spec.md#requirements-mandatory), [T021/T022](tasks.md#phase-3-user-story-6---anchor-each-application-to-its-fund-at-creation-priority-p1-mvp-critical))

Applicants who belong to multiple eligible groups must now pick a Process/"convocatoria" when starting an application; single-group applicants get auto-anchored with no prompt; zero-group applicants are blocked.
- Question: is the auto-select-when-one / choose-when-many / block-when-none rule the behavior you want, and is "convocatoria" the right label for that selector? (This is open item **OI-1**.)

**Force-freeze semantics** ([FR-005](spec.md#requirements-mandatory), [FR-020/021](spec.md#requirements-mandatory), [research D6](research.md#d6--force-freeze-via-iapplicationqueryfilterexcludearchivedfund--guards-product-owner-call-force-freeze-in-flight-work))

Archiving a Fund immediately makes every anchored application read-only and invisible to non-admins, mid-flight.
- Question: archiving will yank in-flight applications out from under applicants and reviewers with no grace period — is that the intended operational behavior, or should there be a warning/confirmation that surfaces *how many* live applications a Fund archive will freeze?
- Question: the freeze is enforced by composing a query filter at ~9 read sites ([T046](tasks.md#phase-7-user-story-4---archive-a-fund-to-freeze-its-activity-priority-p2)) plus controller+domain guards. Is the double-guard worth the surface area, or is one layer enough?

**Regulation stored as columns on the Fund row** ([research D3](research.md#d3--regulation-pdf-storage-spec-014-iobjectstorage-new-filecategoryfundregulation), [data-model.md](data-model.md#new-entity-fund-domain))

One optional PDF → six nullable columns on `dbo.Funds`, mirroring `FundingAgreement`, rather than a child table.
- Question: any anticipated near-term need for multiple regulation docs or version history that would make a child table the better starting point? (Currently out of scope.)

**Required FK with no migration path** ([research D4](research.md#d4--processfundid-required-fk-pre-production-no-migration))

`Process.FundId` and `Application.GroupId` are both `NOT NULL` from day one, justified solely by "not in production yet."
- Question: is the pre-production assumption rock-solid? If any environment has real Processes/Applications, the required FKs need a backfill phase that this plan deliberately omits.

### Areas where I'm less certain (5 min)

- [research D5](research.md#d5--authoritative-applicationgroupid-anchor-product-owner-call-add-authoritative-applicationprocess-fk): I kept the reviewer group-overlap visibility predicate unchanged and made the anchor purely additive. That avoids changing who sees what — but it leaves two parallel notions of "which groups relate to this application" (the anchor vs. the applicant's memberships). I judged that acceptable for this feature; a reviewer may prefer to unify them now rather than later.
- [FR-019](spec.md#requirements-mandatory): switching Plantilla resolution from the membership `FirstOrDefault` to the anchor changes validation behavior for any applicant who is in multiple groups. I treated this as a strict improvement (determinism), but it *is* a behavioral change to submission validation — worth a conscious sign-off.
- [tasks.md T046](tasks.md#phase-7-user-story-4---archive-a-fund-to-freeze-its-activity-priority-p2): I enumerated the non-admin read sites from a grep of `ExcludeDeleted`. If any read path reaches applications another way, it would leak archived-Fund applications. The integration test (T049) is the backstop, but the site list is the part most likely to be incomplete.

### Risks and open questions (5 min)

- If an admin archives a busy Fund, every reviewer mid-decision on its applications silently loses them ([FR-005](spec.md#requirements-mandatory)) — is a "this will freeze N applications" confirmation needed before we ship?
- Does the reports Fund filter ([FR-012](spec.md#requirements-mandatory)) showing **archived** Funds to admins (so they retain visibility) match expectations, or should archived Funds be opt-in there too? (Open item **OI-3**.)
- Is 20 MiB the right cap for `fund-regulations` ([research D3](research.md#d3--regulation-pdf-storage-spec-014-iobjectstorage-new-filecategoryfundregulation)), or do real regulation PDFs run larger? (Open item **OI-2**.)
- The whole feature hinges on the `Application.GroupId` anchor landing before the app can create applications at all (NOT NULL). Is sequencing US6 immediately after the foundational schema phase ([Dependencies](tasks.md#dependencies--execution-order)) clearly understood, so the tree never sits in a broken-create state across a checkpoint?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [research](research.md).*
