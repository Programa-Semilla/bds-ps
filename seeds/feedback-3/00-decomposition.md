# Feedback-3 — Decomposition Index

**Master source of truth:** `AI_Coding_Agent_Unified_Requirements.md` (this directory). This file only
*points* into it — do not duplicate requirement text here. Section numbers (§) refer to the master doc.

This feedback round is too large for one spec (18 areas / 6 phases, cross-dependencies, an external API
integration). It is sliced into independently-shippable specs below. Each future brainstorm session reads
the **master doc + this index**, pulls the named sections for its slice, and resolves the listed open
decisions. Dependency context for already-shipped slices comes from their `specs/NNN-…/spec.md` + the
CLAUDE.md *Recent Changes* log (the repo's normal cross-feature context channel) — not from seeds.

## Slice map

| Slice | Title | Master sections | Depends on | Status |
|---|---|---|---|---|
| **A** | Provider compliance model + Auditor role (foundation) | §2.3, §9, §10 (10.1/10.1.1/10.3/10.4/10.5/10.5.1/10.6), §13, §15 (15.1–15.4, 15.6, 15.7), §22.5/22.6/22.11A, §23.1, §25.1, §28.4/28.5 | — | shipped → `specs/038-auditor-provider-compliance/` (PR #69) |
| **B** | Supplier recommendation algorithm rewrite | §14, §22.7/22.8, §6 (item-line fields), §28.2/28.3 | A | spec-created → `specs/039-supplier-recommendation/` |
| **C** | Auditor workflow stage | §11, §12, §18, §19, §22.9/22.10/22.11, §23.1/23.2, §25.2/25.4, §28.9 | A | not-started |
| **D** | Regulatory freshness gating + Hacienda API sync | §15.5, §16, §17, §25.3, §28.6/28.7 | A | not-started |
| **E** | Fund process windows + applicant timing UX | §3, §22.1/22.2/22.2A, §24.1/24.2, §26.1–26.3, §28.11/28.12 | — (touches Process/Fund) | not-started |
| **F** | Per-user funding limit per process | §4, §22.3, §26.4/26.5, §28.1/28.10 | — | not-started |
| **G** | Applicant timeline + % progress | §20, §24.8 | — (nice after E) | not-started |
| **H** | Small UX/fixes grab-bag | §5 (Information tooltip + Percentage type), §6 (product-before-category), §7 (role-first), §8 (password-recovery email), §24.3–24.5 | — | not-started |

## Dependency rationale

- **A is the keystone.** B needs A's compliance enums + provider-level scoring inputs; C and D need the
  Auditor role + the audit-trail/timestamp fields A introduces.
- **B** adds quote-level fields (delivery lead time, warranty) and the explainable scoring; the new
  item-line field *order* (§6) is shared with H — assign it to whichever ships first, drop from the other.
- **C** turns the Auditor role (created in A) into a workflow actor (audit state, checklists, inbox, PDF
  confirmation). PDF generation *moves* from reviewer to auditor here.
- **D** is enforcement + automation only: the staleness *block* and the daily Hacienda API job. The
  timestamps, audit trail, and the manual "reviewed-no-change / re-authorize" action live in **A** so A is
  self-contained; D consumes them. (Confirm this A/D boundary at A's brainstorm.)

## Cross-round open decisions (§28) — assigned to the slice that must resolve them

- §28.1 max-amount conflict behavior → **F**
- §28.2 warranty scoring direction → **B**
- §28.3 final-score tie-break → **B**
- §28.4 canonical label SICOP vs CCOP → **A**
- §28.5 regulatory status label spelling/casing → **A**
- §28.6 "one month" = 30 days vs calendar month vs configurable → **D**
- §28.7 which regulatory fields block progress (Hacienda on API failure?) → **D**
- §28.8 notification channels (in-app/email/both) → first slice that notifies (**A**, new-provider)
- §28.9 checklist scope (shared vs per-role template) → **C**
- §28.10 which app states count toward funding limit → **F**
- §28.11 reception-window inclusivity → **E**
- §28.12 timezone strategy → **E**
- §28.13 supplier disqualification rules → **B** (scoring) / **A** (warning governance)
- §28.14 warning governance (who creates warnings) → **A**

## Status legend

`not-started` → `spec-created` (spec.md exists) → `shipped` (merged to main, in CLAUDE.md Recent Changes).
Update the relevant row when a slice advances.
