# Review Guide: Structured-Field Input Masks

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-24

---

## What This Spec Does

Every field with a known shape — email, Costa Rica phone, and the CR identification numbers (cédula física, cédula jurídica, DIMEX, NITE, passport) — gets guided entry and rejection of malformed values. It introduces a "type of identification" the user picks, which then drives both the on-screen mask and the server validation, and it makes the supplier lookup tolerant of hyphenation differences. For: applicants registering, admins managing users, applicants registering suppliers.

**In scope:** a reusable client mask registry; a persisted `IdentificationType` on the applicant and supplier records; type selectors on Register / admin user create+edit / supplier add; read-only identification on Profile; server-side per-type validation; hyphenation-tolerant supplier lookup; es-CR copy; seed + E2E updates.

**Out of scope (worth a reviewer's eye):** no Hacienda **check-digit** validation (shape/length only) — see [Out of Scope](spec.md#out-of-scope); no bank/IBAN/postal masks; currency/price/date left as-is; **no data migration** (pre-production, [FR-020](spec.md#functional-requirements)).

## Bigger Picture

This completes spec 021 FR-013: a hand-rolled `input-masks.js` already existed but was loaded only on a view with no maskable fields, while real cédula fields accepted any 50-char string. The feature deliberately stays hand-rolled (no masking library) per the project's vendored-only / no-CDN posture. The `IdentificationType` concept is new domain vocabulary — it's the first time the system models *which kind* of legal ID a person/entity holds, which matters because a strict 9-digit-only cédula would have silently locked out foreign applicants (DIMEX/passport). It also hardens supplier deduplication, which several downstream features (quotations, comparisons) depend on.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [User Story 1](spec.md#user-scenarios--testing) and the [Functional Requirements](spec.md#functional-requirements). As you read:

- Is a **type selector in front of the ID field** the right UX, or does it add friction on the Register form most applicants hit first? Would defaulting to "Cédula física" (the common case) and collapsing the rest be better?
- The registry ([FR-002](spec.md#functional-requirements)) is built for extensibility the user explicitly asked for ("y cualquier otro que exista"). Is a generic registry warranted now, or would two more hard-coded masks have been simpler (YAGNI)?
- [research.md D2](research.md) chose **event delegation + MutationObserver** so AJAX-injected supplier partials get masked. Does that complexity feel justified versus re-scanning after each lookup fetch?

### Key decisions that need your eyes (12 min)

**Cédula jurídica and NITE share the same 10-digit shape** ([Edge Cases](spec.md#edge-cases), [data-model.md](data-model.md))
They're distinguished only by the persisted type — the server cannot tell a mistyped jurídica from a NITE.
- Is type-only differentiation acceptable for v1, or do you want the deferred leading-digit hint (jurídica usually starts `3`)?

**Identification lives on `Applicant`, not the auth user** ([plan.md Discoveries](plan.md#discoveries-that-corrected-the-spec))
Exploration found `ApplicationUser` has no legal ID; the column goes on `dbo.Applicants`.
- Does anything rely on reading a person's legal ID without an `Applicant` row (e.g. an admin/reviewer with no applicant record)? If so, where would their identification be stored?

**Profile is read-only** ([FR-009](spec.md#functional-requirements), [research.md D7](research.md))
The brainstorm assumed editable; reality is identity is admin-managed. We render it read-only with the "administrado" badge.
- Is read-only the right call, or should applicants be able to correct their own cédula (with a uniqueness re-check)?

**Canonical stored form is hyphenated** ([Assumptions](spec.md#assumptions), [research.md D4](research.md))
Matches the existing phone convention; DIMEX stored as bare digits.
- Any reporting/integration that joins on legal ID and would prefer a digits-only key?

### Areas where I'm less certain (5 min)

- [tasks.md T018](tasks.md#phase-3-user-story-1---type-aware-identification-for-people-priority-p1--mvp): threading `IdentificationType` through `IUserAdministrationService` assumes a `CreateUserRequest`-style record I have not opened end-to-end; the request/command shape may differ from the plan's guess.
- [FR-009](spec.md#functional-requirements) on the **admin** forms: the existing role-visibility JS hides the whole `#legalIdField` block when Role≠Applicant. I assumed dropping the type selector inside that block is enough; if the selector posts a value while hidden, the optional-field presence rule ([FR-015](spec.md#functional-requirements)) must still treat it as absent.
- DIMEX max length: I used 11–12 digits with no separators. If your DIMEX cards show a grouped format, the mask's lack of grouping may surprise users.

### Risks and open questions (5 min)

- Significant **E2E churn**: existing tests fill legal IDs as `LID-…`/`SUP-…`/`DEMO-APP-001`, all invalid under the new masks ([tasks.md T022/T023/T028](tasks.md#phase-3-user-story-1---type-aware-identification-for-people-priority-p1--mvp)). If a test isn't migrated to a valid canonical value, it fails — is the team comfortable with that blast radius (it aligns with the "UI quality over selector stability" posture)?
- If `Supplier.NormalizeLegalId` changes shape ([FR-013](spec.md#functional-requirements)), do any *already-stored* supplier rows (seeds/tests) need re-canonicalizing so the unique index and lookup agree? (Pre-production says no backfill — confirm no seeded suppliers exist.)
- Does making the supplier legal-ID field strict (digits only) break the existing 250 ms debounce lookup test that types a non-numeric value mid-entry?

---
*Full context in linked [spec](spec.md) and [plan](plan.md).*
