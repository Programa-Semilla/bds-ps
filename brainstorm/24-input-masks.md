# Brainstorm: Structured-Field Input Masks

**Date:** 2026-05-24
**Status:** spec-created
**Spec:** specs/026-input-masks/

## Problem Framing

User asked for masks over "any field with a known value structure" — emails, id jurídico, cédula, "y cualquier otro que exista en el sistema actualmente". Exploration found the system already has a hand-rolled `wwwroot/js/input-masks.js` (spec 021 / FR-013) with two masks (`phone-cr`, `email`), but it is loaded only on `Application/Edit.cshtml` — a view with no phone/email fields — so real email/phone fields elsewhere go unmasked. The cédula fields (`LegalId`, `SupplierLegalId`) accept any 50-char string with no format guarantee, which also makes supplier lookup-by-legal-id unreliable (same ID stored with/without hyphens). The job: make masking consistent, identification entry type-aware, and the mechanism extensible.

## Approaches Considered

### A: Extend the hand-rolled `input-masks.js` into a data-driven registry (CHOSEN)
- Pros: no new dependency (honors CLAUDE.md vendored-only / no-CDN); reuses the established `data-mask` attribute pattern; one registry entry + attribute adds a future mask, satisfying "cualquier otro que exista".
- Cons: hand-rolled validation is less battle-tested than a library; must generalize the phone formatter carefully.

### B: Vendor a masking library (IMask / Cleave.js)
- Pros: richer masking primitives, well-tested.
- Cons: new vendored/managed dependency → spec approval per CLAUDE.md; heavier than the need; breaks the "reuse what is vendored" posture.

## Decision

Chosen: **A** — extend the hand-rolled script. New spec **026-input-masks** (not a spec-021 increment) because the feature adds a persisted `IdentificationType` concept + two dacpac columns + a domain enum — bigger than the "fold small features into 021" rule.

Key decisions reached:
- **Field scope:** email, phone-cr, cédula física, cédula jurídica, DIMEX, NITE, passport + a data-driven registry for future fields.
- **Person identification is type-aware:** a persisted `IdentificationType` (Cédula física / DIMEX / NITE / Pasaporte) with a selector that rebinds the field's mask. This avoids breaking foreign applicants that a strict 9-digit-only cédula mask would block.
- **Selector surfaces (person):** Register, admin user create, admin user edit, Profile.
- **Supplier identification:** type selector {Cédula jurídica, NITE}; lookup normalizes input to canonical form so hyphenation differences still match.
- **Behavior:** per-field — digit IDs strict (format-as-you-type, strip, auto-hyphen); email/passport soft (blur validation).
- **Persistence:** persist the type (two nullable dacpac columns: `AspNetUsers.IdentificationType`, `Suppliers.IdentificationType`).
- **Canonical stored form:** hyphenated (consistent with existing phone storage); DIMEX plain digits; passport uppercased.
- **No migration:** system is pre-production — seeds/fixtures adjusted to canonical form, no backfill.
- Server-side validation per type (defense in depth); all copy es-CR.

## Open Threads

- Domain placement of the identification invariant: per Constitution Principle II it should live in a domain value object / entity guard, with ViewModel DataAnnotations echoing it — to settle in `/speckit-plan`.
- Profile identification editability: Profile email is currently read-only / admin-managed; confirm whether the identification selector + value on Profile is editable by the user or display-only — to settle in `/speckit-plan`.
- Soft hint (optional, deferred): warn when a 10-digit value's leading digit is atypical for the chosen type (jurídica usually starts `3`).
