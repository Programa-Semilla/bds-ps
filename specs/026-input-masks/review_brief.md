# Review Brief: Structured-Field Input Masks

**Spec:** specs/026-input-masks/spec.md
**Generated:** 2026-05-24

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Make input masking consistent, type-aware, and extensible across every field with a known value structure: email, CR phone, and CR identification numbers (cédula física, cédula jurídica, DIMEX, NITE, passport). Today only phone + email masks exist (spec 021 / FR-013) and they are wired on almost no surface, while identification fields accept any 50-char string. This adds a declarative mask registry, a persisted identification **type** (with a selector that rebinds the field's mask), server-side per-type validation, and hyphenation-tolerant supplier lookup. Completes spec 021 FR-013.

## Scope Boundaries

- **In scope:** masks for email/phone/cédula-física/cédula-jurídica/DIMEX/NITE/passport; data-driven registry; persisted `IdentificationType` on person + supplier; type selector on Register, admin user create/edit, Profile, and supplier add/lookup; server validation; canonical-form storage; supplier-lookup normalization; es-CR copy; accessibility.
- **Out of scope:** check-digit/checksum validation; bank/IBAN/postal masks (no such fields); reformatting currency/price/date; passport rules beyond non-empty alnum; data migration (pre-production).
- **Why these boundaries:** the user asked to mask *structured* fields that exist today; speculative field types and Hacienda checksum logic are deferred (YAGNI, Principle VI).

## Critical Decisions

### Type-aware identification instead of a single strict cédula mask
- **Choice:** persist an `IdentificationType` and let a selector rebind the mask.
- **Trade-off:** adds a UI field + two nullable schema columns, but a strict 9-digit-only cédula mask would block foreign applicants (DIMEX/passport).
- **Feedback:** is the person type set {Cédula física, DIMEX, NITE, Pasaporte} complete and correct?

### Extend the hand-rolled script, not a library
- **Choice:** grow `input-masks.js` into a registry; no new dependency.
- **Trade-off:** hand-rolled validation vs. a tested library; honors no-CDN/vendored-only posture.
- **Feedback:** acceptable, or is a vendored library wanted despite the dependency-approval cost?

### Canonical stored form = hyphenated
- **Choice:** store the masked hyphenated string (DIMEX plain digits, passport uppercased), matching existing phone storage.
- **Trade-off:** human-readable + consistent vs. a digits-only dedup key; lookup must normalize either way.

## Areas of Potential Disagreement

### Cédula jurídica vs NITE share the same 10-digit shape
- **Decision:** distinguish them by persisted type, not by format; server shape-validation is identical for both.
- **Why controversial:** server can't tell a mistyped jurídica from a NITE.
- **Alternative view:** add a leading-digit heuristic (jurídica starts `3`).
- **Seeking input on:** is type-only differentiation acceptable for v1? (Spec says yes; soft hint deferred.)

### Profile identification surface
- **Decision:** selector listed on Profile (FR-009).
- **Why controversial:** Profile email is currently read-only / admin-managed.
- **Alternative view:** identification on Profile is display-only (disabled selector + masked value).
- **Seeking input on:** editable or display-only on Profile? (Flagged for `/speckit-plan`.)

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Mask keys | `email`, `phone-cr`, `cedula`, `cedula-jur`, `dimex`, `nite`, `pasaporte` | registry keys / `data-mask` attribute values |
| Domain enum | `IdentificationType` | person + supplier identification kind |
| Schema columns | `AspNetUsers.IdentificationType`, `Suppliers.IdentificationType` | nullable, via dacpac |

## Open Questions

- [ ] Domain placement of the type↔shape invariant (value object vs entity guard) — `/speckit-plan`.
- [ ] Profile identification editable vs display-only — `/speckit-plan`.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Strict cédula mask blocks foreign applicants | High | Type selector with DIMEX/passport |
| Inconsistent hyphenation breaks supplier dedup | Med | Canonical-form storage + lookup normalization (FR-007, FR-013) |
| Validation logic scattered in controllers (anti-Principle II) | Med | Plan places invariant in domain |
| Re-wiring masks across many forms churns E2E selectors | Low | UI-quality-over-selector-stability posture; E2E rewrites in scope |

---
*Share with reviewers before implementation.*
