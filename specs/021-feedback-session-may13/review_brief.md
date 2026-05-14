# Review Brief: Feedback Session May-13 (021)

**Spec:** specs/021-feedback-session-may13/spec.md
**Generated:** 2026-05-13

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Consolidates the 26 refinements from the May-13 stakeholder feedback session into a single delivery. Introduces an annual program-cycle aggregate (`Process`) above the existing `Group`, a per-Process configuration snapshot (`Plantilla` / `ProcessPlantilla`) that references `ImpactTemplate`s and stage-expiry windows with copy-on-assign semantics, lifts `Impact` from Item to Application, adds a fourth role *SupplierAdmin* scoped exclusively to supplier CRUD, replaces the *"Solicitud N.º N"* counter with an opaque base32 `PublicCode`, hardens forms (input masks, required markers, autosave, /review confirmation, cascading CR Province + Cantón), adds an end-to-end forgot-password flow with strength legend, pivots applicant-facing copy from *financiamiento* to *acompañamiento*, scaffolds a public `/` landing page, repivots the admin dashboard (Personas activas + Fondos entregados; pending-quotation tile moves to reviewer dashboard), and fixes the *"deleted-still-active"* defect.

## Scope Boundaries

- **In scope:** 8 user stories (3 P1, 3 P2, 2 P3), 34 functional requirements, 6 non-functional requirements, 16 measurable success criteria. Schema changes via dacpac: new `Process`, `Plantilla`, `ProcessPlantilla`, `Province`, `Canton`, `PasswordResetToken` tables; `Group.ProcessId` FK; `Application.Impact` + `Application.PublicCode`; `Item.Impact` dropped; `SupplierBranch.{ContactPersonName, ProvinceId, CantonId}`; `User.CodigoPersonal`. New ASP.NET Identity role *SupplierAdmin*. Hosted background service for expiry reminders. Reuses existing SMTP, no new managed dependencies.
- **Out of scope:** BCCR exchange-rate auto-fetch, AI/Tropic quotation extraction, OTP, guided onboarding tour, user-initiated email-change request flow, foreign supplier addresses, multi-Process Applicant membership, visual-regression tooling, public marketing site beyond the FR-031 scaffold.
- **Why these boundaries:** Stakeholder directive — "single shot" delivery of every committed refinement, with BCCR + AI explicitly flagged as research workstreams. Out-of-scope items are either deferred to future specs (OTP, tour, email-change request) or operationally constrained (foreign addresses outside CR market focus).

## Critical Decisions

### Process is a NEW aggregate above Group, not a rename
- **Choice:** New `Process` entity. Every existing `Group` gains a `ProcessId` FK. Migration seeds a *"Migración inicial"* Process so spec-016 data survives.
- **Trade-off:** Largest schema change in 021 vs. preserving spec-016's group-overlap predicate semantics. Choosing additive aggregate keeps reviewer-access scoping intact and matches stakeholder language ("agrupar usuarios por procesos").
- **Feedback:** Is the additive-aggregate shape preferable to renaming Group→Process and introducing sub-groups underneath?

### Plantilla snapshot-on-assign (copy-on-assign), not live reference
- **Choice:** Assigning a base Plantilla to a Process creates an immutable `ProcessPlantilla` row whose payload is frozen at assignment time.
- **Trade-off:** Slightly more storage + duplicated config per Process vs. exposing assigned Processes to surprise behaviour when base Plantillas are edited. Stakeholder asked for this explicitly to protect in-flight cycles.
- **Feedback:** Confirm one-to-one cardinality is the right default (OQ-1) vs. allowing many snapshots per Process for different Application kinds.

### Impact moves from Item to Application (no migration; no production data)
- **Choice:** `Item.Impact` column dropped outright; `Application.Impact` added (single `ImpactTemplate` per Application, captured upfront). Creation flow: Impact first, items inline after.
- **Trade-off:** Coarser granularity vs. matches stakeholder's *"ficha (remodelación)"* mental model and the meeting note *"agregar una ficha, definir su impacto y luego ingresar los ítems"*. Acceptable because no production data exists.
- **Feedback:** Confirm Application-level Impact is the right granularity (the alternative — a new `Ficha` mid-level entity between Application and Item — was considered and rejected for simplicity).

### Opaque PublicCode replaces *"Solicitud N.º N"* everywhere
- **Choice:** Generated base32 `PublicCode` matching `^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$`; numeric `Id` remains primary key for joins; display layer maps both ways.
- **Trade-off:** Loses raw count visibility (stakeholder wants this) and adds collision-retry logic vs. dictation-safe support workflow + privacy.
- **Feedback:** Confirm the alphabet (no 0/O/1/I/L) and the two-group `XXXX-XXXX` shape over alternatives (year-prefixed sequence, process-prefixed sequence).

### Fourth role *SupplierAdmin* — supplier CRUD only, nothing else
- **Choice:** Read+write on `Supplier` + `SupplierBranch` + toggle `IsCompliant`. Denied on Applications, Users, Processes, Reports, Groups (HTTP 403 + audit event).
- **Trade-off:** Strictest isolation vs. denying SupplierAdmins read-only context on Applications that cite their suppliers. Stakeholder asked for the strict cut.
- **Feedback:** Confirm validate-only vs. full CRUD (OQ-7) — defaulted to full CRUD.

### "Single shot" mega-spec vs. decomposition
- **Choice:** One spec, one delivery, internally prioritized P1 → P2 → P3.
- **Trade-off:** Largest spec in the project's history (34 FRs, 16 SCs, 8 user stories) vs. shipping every committed meeting refinement together. Stakeholder explicitly endorsed this shape.
- **Feedback:** Confirm comfort with single-spec scope; alternative would be a 020-architectural + 022-UX-polish split.

## Areas of Potential Disagreement

### Scope size

- **Decision:** Bundle all 26 meeting items into 021.
- **Why this might be controversial:** Single PR family will be the largest in project history; review surface is wide.
- **Alternative view:** Split into 020-architectural-shifts (Process / Plantilla / Impact-relocation / SupplierAdmin / PublicCode / stage-expiry) and 022-UX-polish (autosave / masks / hints / strength legend / forgot-password / public landing / admin KPI repivot / bug fix).
- **Seeking input on:** Is the single-shot framing right, or do you prefer the architectural / polish split?

### Impact granularity

- **Decision:** Impact at Application level, not at a new mid-level `Ficha` entity.
- **Why this might be controversial:** Stakeholder used "ficha (remodelación)" language that implies a mid-level grouping.
- **Alternative view:** Introduce `Ficha` between Application and Item; Impact lives on Ficha; an Application can have multiple fichas with distinct impacts.
- **Seeking input on:** If multi-ficha applications are anticipated, Application-level Impact will need to revisit. Confirm one-Impact-per-Application is right.

### Stage-expiry hard-block on POST

- **Decision:** Expired stages return HTTP 422 on transitions and hard-block.
- **Why this might be controversial:** Some operations teams prefer admin-overridable soft-blocks.
- **Alternative view:** Allow admin to extend an expired window from the admin panel rather than blocking outright.
- **Seeking input on:** Should there be an admin-override path that bypasses the 422, or is the hard-block the right ops posture?

### PublicCode placement on Funding Agreement PDF

- **Decision:** PublicCode surfaces on the Funding Agreement PDF; placement deferred to plan (OQ-4).
- **Why this might be controversial:** Spec 018's PDF template is a legal artefact; changes ripple to printed materials.
- **Alternative view:** Keep PDF with numeric Id for legal continuity; PublicCode is platform-internal display only.
- **Seeking input on:** Confirm PublicCode should appear on the Funding Agreement PDF.

### Public landing scaffold in 021 vs. later

- **Decision:** Public `/` scaffold ships in 021 with placeholder slots for reglamento + ejemplo files.
- **Why this might be controversial:** Content delivery owner (OQ-5) is undefined.
- **Alternative view:** Defer public landing entirely until content is delivered; ship 021 without the public surface.
- **Seeking input on:** Is the placeholder scaffold acceptable, or should the public landing wait for content?

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Annual program cycle aggregate | `Process` | Stakeholder language ("procesos"). New aggregate above Group. |
| Per-Process config snapshot | `ProcessPlantilla` | Snapshot of `Plantilla` at assignment time, copy-on-assign. |
| Reusable base config template | `Plantilla` | Spanish form preserved; references ImpactTemplates + min quotations + required-field toggles + stage-expiry overrides. |
| Opaque application identifier | `PublicCode` | Base32 `XXXX-XXXX` shape, dictation-safe alphabet. |
| Fourth role | `SupplierAdmin` | ASP.NET Identity role name; supplier CRUD only. |
| User per-person identifier | `CodigoPersonal` | Free-text admin-set field on User (payroll ID, program-member #, etc.). |
| Public CTA hero copy | *"¿Listo para acelerar tu negocio?"* + *"Iniciar acompañamiento"* | Replaces *"¿Listo para solicitar financiamiento?"*. |
| Welcome greeting | *"Hola, {{Nombre}}"* | Replaces *"Bienvenido/a"* (inclusive, neutral). |
| FX disclaimer | *"El monto en colones puede variar según el tipo de cambio vigente en la fecha de compra."* | Single key in es-CR catalog (FR-022). |
| Expired stage banner | *"Vencido — la etapa cerró el {{fecha}}"* | Danger style; on every stage-bound surface (FR-024). |
| Autosave indicators | *"✓ Guardado HH:MM"* / *"⚠ No guardado — reintentar"* | On-blur autosave (FR-016). |

## Open Questions

- [ ] OQ-1: Plantilla cardinality per Process — one-to-one (default) vs many-to-one.
- [ ] OQ-2: Process closure freeze semantics on FundingAgreement (default = freeze).
- [ ] OQ-3: Stage-expiry override per-Plantilla vs per-Process only (default = per-Process only).
- [ ] OQ-4: PublicCode placement on Funding Agreement PDF (template swap vs footnote).
- [ ] OQ-5: Reglamento + ejemplo file content ownership and authoring source.
- [ ] OQ-6: Reminder cadence (T-72h / T-24h / expiry) fixed vs admin-configurable (default = fixed).
- [ ] OQ-7: SupplierAdmin scope — full CRUD (default) vs validate-only-existing.
- [ ] OQ-8: Hint copy strings for FR-020's initial set — designer/copywriter authorship.
- [ ] OQ-9: Process audit-event coverage extends `AdminAuditEvent` (spec 016 pattern) — pin in plan.
- [ ] OQ-10: Provincia *"Otro/Extranjero"* handling — block in UI (default) vs catalog row.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Scope size — largest spec to date (34 FRs, 8 user stories) | High | P1/P2/P3 prioritization built into spec; plan phase can sequence delivery; SC-016 keeps E2E bar gating merge. |
| Schema cutover drops `Item.Impact` outright | High | Explicit acknowledgment that no production data exists (NFR-001); migration assigns existing Groups to seeded *"Migración inicial"* Process. |
| Stage-expiry hard-block (422) without admin override | Medium | Edge case captures the boundary; admin override path is a plan-phase decision if stakeholders push back. |
| Forgot-password introduces public anonymous routes | Medium | No enumeration on unknown emails; tokens single-use with 60-min TTL; reuse existing SMTP retry/back-off. |
| `PublicCode` collisions at scale | Low | Base32 8-char space (~32^8 = 1.1×10¹²); retry on duplicate; throw + log after 3 attempts; never expose to user. |
| Background email service failure | Low | Failure does not block stage progression; retries with exponential backoff (NFR-002); final failure logged on admin email-queue surface. |
| SupplierAdmin role-escalation attempt | Low | Direct URL access returns 403 + `AdminAuditEvent`; role tested in SC-006 across every admin surface. |
| Public `/` landing without content | Low | Slots show *"Próximamente"* placeholder; FR captures slot, not asset; content delivery is an admin operation. |

---
*Share with reviewers before implementation.*
