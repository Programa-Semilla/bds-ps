# Brainstorm: ALIA Transactional Email Brand UI-Lift

**Date:** 2026-06-19
**Status:** spec-created
**Spec:** specs/041-email-brand-lift/

## Problem Framing

Every system email currently shares a deliberately minimal, text-only layout — a choice made in spec 021 (FR-023/NFR-001) to maximize deliverability (no inline images, a text wordmark, a near-black button). The institution now wants its transactional mail to look and read like Programa Semilla: a branded shell with the official logo, the brand teal palette, clear CTAs, structured "Detalle" cards, and a partner-logo footer strip — the email analog of the web brand alignment (spec 037) and the PDF lift (spec 016). The seed (`seeds/emails/Notepad.md` + Beefree reference + palette + logo/footer assets) frames it as a full brand UI-lift; the companion `Respuestas correo ALIA.txt` supplies polished voseo copy for 10 emails and renames the platform to **ALIA**.

The pivotal tension: the redesign (logo header + partner strip) reverses spec 021's no-image rule. Resolved: adopt hosted absolute-URL images (using the existing `Notifications:BaseUrl`) with image-blocked degradation as the mitigation.

## Approaches Considered

### A: Component-based shared shell (CHOSEN)
- Rebuild `_EmailLayout.cshtml` into a table-based, inline-CSS, 600px branded shell + reusable partials (hero/title, status/info card, detail list, CTA button); each email body composes them. New events added via the spec-028 outbox pattern.
- Pros: mirrors how the codebase already works (shared layout + partials) and spec 037's token discipline; one place to change brand; no new deps; no build step.
- Cons: every email body must be refactored to use the new blocks.

### B: Per-email standalone templates
- Each email owns its full HTML.
- Pros: maximal per-email control.
- Cons: ~22+ copies of the brand chrome → drift, unmaintainable. Rejected.

### C: MJML / Foundation-for-Email build step
- Author in MJML, compile to HTML.
- Pros: best-in-class responsive output.
- Cons: adds a Node toolchain — violates the repo's "no new managed deps / no build step / vendored-only" posture. Rejected (noted as future option).

## Decision

Approach **A**. Locked decisions (via clarifying Q&A):
- **Image strategy:** hosted absolute-URL images (reverses spec 021's no-image rule).
- **Partner footer strip:** on every email.
- **Scope:** all existing HTML emails (transactional + Identity + Stages + Suppliers); `.text.cshtml` twins kept plain but in sync.
- **Naming:** platform = **ALIA**; brand/logo/sign-off = Programa Semilla ("Equipo Programa Semilla"); new support phone +506 4600-1234.
- **Copy:** reference-file copy canonical where it maps; light-polish the rest in the same voseo voice, preserving meaning/variables/warnings.
- **New emails:** "Lift + add the new events" — three new emails: password-changed confirmation (direct-send identity email), "solicitud en revisión" (new outbox event), "nueva empresa para revisión" (new outbox event, live trigger deferred to OQ-1).

Spec written and reviewed (`REVIEW-SPEC.md`: **SOUND**). The P1 design-system lift is independent of all three open questions.

## Open Threads

- **OQ-1 (gates FR-013 live trigger):** "Nueva empresa para revisión" — exact business trigger (newly registered applicant Company? something submitted for review?) and recipient (reviewer pool? auditors?). Template ships without a live trigger until confirmed.
- **OQ-2 (plan-time, only substantive risk):** Is "entering review" a distinct lifecycle transition from submission, or is submit→review atomic (making the new "solicitud en revisión" email redundant with the submission receipt)? Verify against the application state model in planning.
- **OQ-3 (plan-time, mechanical):** Public image-serving path — dedicated `/email-assets/` static path vs existing `wwwroot/lib`.
- Plan-time: name the new event identities (e.g. `APPLICATION_UNDER_REVIEW_APPLICANT`, `COMPANY_SUBMITTED_FOR_REVIEW`) and confirm no dacpac change is needed (string-stored event types, spec-028 pattern).
- Plan-time: where the password-changed confirmation hooks into the Identity password change/reset success path (direct-send, `InvitationEmailFactory` pattern).
- Reconciles spec 019/#17's "email signature text-only" decision (Closed Thread) — this spec deliberately reopens it to add hosted brand imagery, mitigated by image-blocked degradation (NFR-004).
