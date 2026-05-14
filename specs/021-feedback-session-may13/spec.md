# Feature Specification: Feedback Session May-13 (021)

**Feature Branch**: `021-feedback-session-may13`
**Created**: 2026-05-13
**Status**: Draft
**Input**: User description: "Consolidated implementation of the 26 refinements from the May-13 stakeholder feedback session (Milena Arias, Pao Rodríguez Marín, Danny Pérez). Introduces Proceso as a new aggregate above Group, Plantilla as a per-Process configuration snapshot with copy-on-assign semantics, lifts Impact from Item to Application, adds SupplierAdmin role, opaque PublicCode replacing 'Solicitud N.º N', CR cascading Province + Cantón catalogs, supplier search refinements, form/data-quality hardening, profile + forgot-password flows, stage-expiry windows with email reminders, acompañamiento copy pivot, public landing scaffold, admin KPI repivot, and the deleted-still-active bug fix."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Annual program cycle administration (Priority: P1)

A platform administrator opens *Administración*, creates a Process named *Crocus 2025*, attaches an existing base Plantilla (which references a set of `ImpactTemplate`s and defines minimum quotations per item), then creates Groups *Norte*, *Sur*, *Centro* under the Process. A reviewer is given access to *Norte* + *Sur* within *Crocus 2025*. Later, the admin edits the base Plantilla to change required minimum quotations; *Crocus 2025* remains unchanged because the Process holds a snapshot taken at assignment time.

**Why this priority**: Every other change in this spec assumes the system can organize work by program cycles. Without Process + Plantilla, applicants cannot be grouped, reviewers cannot be scoped, expiries cannot be set, and "Crocus 2025" / "Nexo 2026" cannot be administered.

**Independent Test**: Admin completes the full create-Process → assign-Plantilla → create-Groups → assign-reviewers flow without leaving *Administración*; then mutates the base Plantilla and verifies the Process snapshot is unaffected.

**Acceptance Scenarios**:

1. **Given** the admin is signed in and no Processes exist, **When** the admin creates *Crocus 2025* and assigns base Plantilla *PlantillaMVP-v1*, **Then** the Process appears with a snapshot of *PlantillaMVP-v1* (`ProcessPlantilla`) that does not change when the base is later edited.
2. **Given** the admin is on the User edit form for a reviewer, **When** the admin assigns the reviewer to Groups *Norte* + *Sur* inside *Crocus 2025*, **Then** the reviewer's queue shows only Applications from those Groups.
3. **Given** Groups exist under *Crocus 2025*, **When** the admin opens the admin user list and uses the group filter, **Then** the filter is a two-level cascade *Process → Group* whose options reflect the Process catalog.

---

### User Story 2 — Applicant submits an Application end-to-end on the new flow (Priority: P1)

An applicant signs in, sees the greeting *"Hola, Vivi"*, starts a new Application from the public CTA *"Iniciar acompañamiento"*. The form opens with the Impact step first (a single `ImpactTemplate` chosen from those available in the Process's Plantilla). After entering Impact, the applicant adds items inline without leaving the screen, each with category and specifications, each receiving at least the required number of quotations. Suppliers are found by name search with autocomplete (or by cédula jurídica); when no match is found, the applicant registers a new SupplierBranch including contact-person name and cascading Province → Cantón dropdowns. Every field is saved on blur with a visible *"✓ Guardado HH:MM"* confirmation. Required fields are marked. The submit button is disabled until ≥1 item, Impact, and every required field is complete. Clicking submit routes to a dedicated `/review` page showing items / suppliers / totals / Impact; the applicant clicks *"Confirmar y enviar"*. The application is then displayed under its opaque `PublicCode` (e.g. *A7K2-9XF*); the legacy *"Solicitud N.º N"* is nowhere to be seen. A CRC-converted USD amount renders with the FX disclaimer underneath.

**Why this priority**: This is the platform's primary user journey. Half of the meeting items are inside it (impact-at-application, autosave, required markers, masks, submit gating, confirmation step, PublicCode, FX disclaimer, supplier search, cantón cascade, contact-person field, greeting, CTA). Until this lands, the other improvements have no surface to live on.

**Independent Test**: A fresh applicant signs in, completes draft creation → impact → items → quotations → submit → review → confirm, and the resulting Application surfaces only under its `PublicCode` across every surface (applicant dashboard, reviewer queue, signing inbox, Funding Agreement PDF, every notification email).

**Acceptance Scenarios**:

1. **Given** a draft Application with all required fields blank, **When** the applicant inspects the submit button, **Then** it is disabled with a tooltip listing what is missing.
2. **Given** the applicant has filled name → blurs → server is reachable, **When** the field loses focus, **Then** an inline *"✓ Guardado"* indicator appears within 1 s.
3. **Given** the applicant is filling SupplierBranch, **When** the applicant picks *San José* in Province, **Then** the Cantón dropdown narrows to the cantones of *San José*.
4. **Given** the applicant clicks submit on a complete draft, **When** the system routes to `/review`, **Then** the page renders Items / Suppliers / Totals / Impact and an explicit *"Confirmar y enviar"* button.
5. **Given** the Application has been submitted, **When** the applicant looks at any surface displaying its identity, **Then** they see an *A7K2-9XF*-shaped `PublicCode`, not *Solicitud N.º 9*.
6. **Given** an Item quotation in USD on the `/review` page, **When** the page renders the CRC-converted total, **Then** the FX disclaimer *"El monto en colones puede variar según el tipo de cambio vigente en la fecha de compra"* appears below the converted amount.

---

### User Story 3 — SupplierAdmin manages the supplier catalog without other privileges (Priority: P1)

A user assigned the *SupplierAdmin* role signs in. The sidebar shows only *Empresas proveedoras* + their profile. The user lists suppliers, filters by Process, and the default sort is *last-used desc* (most recent first). They open a supplier, mark *al día = sí*; every Application currently referencing that supplier reads the new value live (no per-Application copy). They register a new SupplierBranch with Province + Cantón cascade and ContactPersonName. They attempt to visit `/admin/users` and `/admin/reports` directly by URL — both return 403 with the Tabler-styled 403 page; an `AdminAuditEvent` records the attempt.

**Why this priority**: The meeting explicitly called for delegated supplier administration without giving full admin power; it is one of the three load-bearing role-shape changes (alongside Process and Impact-on-Application).

**Independent Test**: A user is provisioned with role *SupplierAdmin* only; they can CRUD `Supplier` + `SupplierBranch` and validate "al día"; every other admin surface returns 403.

**Acceptance Scenarios**:

1. **Given** a *SupplierAdmin* user, **When** they list suppliers, **Then** the default sort is by `LastUsedAt` desc and a Process filter is visible.
2. **Given** a supplier currently referenced by three Applications, **When** the *SupplierAdmin* toggles *al día = no*, **Then** all three Applications reflect the new state on next view (no per-Application copy).
3. **Given** a *SupplierAdmin* user, **When** they GET `/admin/users`, **Then** the response is HTTP 403 and an `AdminAuditEvent` row is written.

---

### User Story 4 — Stage expiry, countdown, and email reminders (Priority: P2)

An admin sets the platform default stage windows (e.g. solicitud = 14 days, revisión = 10 days, facturación = 30 days). They override the *facturación* window for Process *Crocus 2025* to 45 days. A hosted background service runs hourly; for every active Application, it computes time-remaining in the current stage; at T-72h, T-24h, and at expiry it sends reminder emails via the existing SMTP wiring. On every stage-bound surface (applicant draft, reviewer queue row, signing inbox) a countdown banner shows remaining time; when the window passes, the banner turns red *"Vencido — la etapa cerró el {{fecha}}"* and new transitions on that Application return 422.

**Why this priority**: Important operational predictability, but Applications can still be submitted and reviewed without it. Strict dependency on US1 (Process exists for per-Process overrides).

**Independent Test**: With seeded stage windows and an Application close to expiry, the bg service emits T-72h / T-24h / expiry emails (captured in integration tests via in-process MailKit) and the affected surfaces render the countdown / Vencido banner; POST after expiry returns 422.

**Acceptance Scenarios**:

1. **Given** an Application in *solicitud* with 71h remaining, **When** the bg service runs, **Then** a single T-72h reminder email is sent to the applicant.
2. **Given** an Application whose *solicitud* window expired 10 minutes ago, **When** the applicant attempts to submit, **Then** the response is 422 with message *"La etapa cerró el {{fecha}}. Contacte al administrador."*
3. **Given** an admin opens *Procesos → Crocus 2025*, **When** they override *facturación* to 45 days, **Then** all Applications inside *Crocus 2025* use 45 days instead of the platform default.

---

### User Story 5 — Self-service profile, password legend, forgot-password (Priority: P2)

A user opens *Mi perfil*, edits FirstName / LastName / Phone / Address, and saves; *Email*, *Role*, *Group*, *CodigoPersonal* are visibly read-only with an *"administrado"* badge. From the login page they click *"¿Olvidó su contraseña?"*; the system emails a single-use 60-minute token link; they follow it, set a new password with the live strength checklist (minimum length, uppercase, number, special) ticking as criteria are met, and log in successfully. Every password field shows an eye toggle.

**Why this priority**: Removes a real friction-and-support burden (lost passwords today require admin help) and rounds out the form-quality theme. Lower than US1–US3 because it does not block end-to-end Application work.

**Independent Test**: New user → forgets password → forgot-password email → reset → login. Same user opens profile, edits FirstName, saves; verifies Email cannot be edited by them.

**Acceptance Scenarios**:

1. **Given** an unknown email on `/forgot-password`, **When** submitted, **Then** the response is identical to the known-email path (no enumeration).
2. **Given** a valid reset token, **When** the user opens the link 65 minutes later, **Then** the page renders *"Enlace inválido o expirado. Solicite uno nuevo."*
3. **Given** the user on the change-password screen, **When** they type a candidate password, **Then** the strength legend ticks each criterion live as it is met.
4. **Given** the user on `/profile`, **When** they attempt to edit the Email input, **Then** the input is read-only and visibly badged *administrado*.

---

### User Story 6 — Admin dashboard repivot + supplier search refinements (Priority: P2)

The admin dashboard now leads with *Personas activas* and *Fondos entregados* KPI tiles; the pending-quotation tile is no longer present (it has moved to the reviewer dashboard). The supplier list supports search by *name* OR *cédula jurídica* with autocomplete; the supplier admin list defaults to *last-used desc*, filterable by Process. The admin user list group filter is the two-level cascade Process → Group from US1.

**Why this priority**: Improves daily admin ergonomics. Depends on US1 (Process axis) and is partially redundant once US3 is in place — but rounds out the platform's repositioning toward "personas + fondos entregados" framing rather than per-cotización worry-work.

**Independent Test**: Seeded admin dashboard renders the two new KPI tiles with non-placeholder values; pending-quotation tile renders on the reviewer dashboard, absent from admin dashboard; supplier search by name returns expected matches.

**Acceptance Scenarios**:

1. **Given** seed data with executed FundingAgreements totalling ₡5,000,000, **When** the admin opens the dashboard, **Then** the *Fondos entregados* tile reads *₡5,000,000*.
2. **Given** an admin types *"PSCR"* in the supplier search box, **When** results render, **Then** the autocomplete shows matching `Supplier.Name`s within 300 ms (P95 at seed scale ≥ 200 suppliers).
3. **Given** the reviewer dashboard is open, **When** the page renders, **Then** the *Cotizaciones pendientes* tile is visible with current count.

---

### User Story 7 — Acompañamiento copy pivot, neutral greeting, public landing scaffold (Priority: P3)

An anonymous visitor lands on *"/"*. The hero reads *"¿Listo para acelerar tu negocio?"* with primary button *"Iniciar acompañamiento"*. Below the hero, three slot regions render: *Reglamento (descargar)*, *Ejemplo de cotización (descargar)*, *Sponsor strip* (reusing spec 019's sponsor logos). When the visitor signs in, the dashboard greeting reads *"Hola, Vivi"* (no *"Bienvenido/a"*). Every applicant-facing surface containing the word *"financiamiento"* has been audited; only the legal *Funding Agreement* PDF retains it.

**Why this priority**: Brand/copy correctness. Doesn't block any user task but is part of the meeting's explicit asks and rounds out the platform's repositioning toward an *acompañamiento* identity rather than a *financiamiento* identity.

**Independent Test**: Grep of rendered HTML on every applicant-facing surface returns zero *"financiamiento"* matches and zero *"Bienvenido/a"* matches. `/` (anonymous) renders the CTA + three slot regions + sponsor strip.

**Acceptance Scenarios**:

1. **Given** an anonymous visitor on `/`, **When** the page loads, **Then** the hero CTA reads *"¿Listo para acelerar tu negocio?"* and the button reads *"Iniciar acompañamiento"*.
2. **Given** a signed-in user *Vivi*, **When** the dashboard renders, **Then** the greeting reads *"Hola, Vivi"*.
3. **Given** the reglamento file slot has no file uploaded, **When** the public landing renders, **Then** the slot shows a *"Próximamente"* placeholder instead of a broken link.

---

### User Story 8 — Bug fix: deleted Applications no longer surface as active (Priority: P3)

An admin deletes Application *A7K2-9XF*. The Application immediately disappears from every dashboard surface (applicant, admin, reviewer). The applicant's *Solicitudes activas* counter decrements. The previous behaviour — *"Su borrador para Application #10 está listo para enviar"* persisting after deletion — never reappears.

**Why this priority**: Real defect explicitly demonstrated in the meeting screenshot. Low priority only because narrow scope; high reproducibility, low risk fix.

**Independent Test**: Create draft → delete via admin → reload every dashboard surface → verify draft no longer appears anywhere.

**Acceptance Scenarios**:

1. **Given** an Application in *Borrador* state, **When** the admin soft-deletes it, **Then** it disappears from the applicant dashboard's *Solicitudes activas* count and from any *"borrador listo para enviar"* prompt within the same request lifecycle.
2. **Given** an Application that has just been deleted, **When** any dashboard query runs, **Then** the soft-delete filter is applied and the row is excluded.

---

### Edge Cases

- **First Process bootstrap.** Empty system has no Processes; empty-state on `/admin/processes` guides creation. Applicant onboarding flow blocks gracefully until at least one active Process exists.
- **Plantilla with zero ImpactTemplates.** Allowed at create time but blocks assignment to a Process; validation message *"Plantilla no tiene plantillas de impacto asignadas."*
- **Plantilla detach with active Applications.** Blocked with explicit message *"Plantilla en uso por N solicitudes activas."* Force-detach requires admin confirmation modal + `AdminAuditEvent`.
- **Process closure with open Applications.** Blocked with list of offending `PublicCode`s; admin must resolve or transfer first.
- **Group reassignment of active Applicant.** Moving an Applicant between Groups (Processes) with in-flight Applications: blocked; admin must wait for completion or void.
- **Existing Group without Process.** Migration assigns all existing Groups to a seeded Process *"Migración inicial"* so spec 016 data survives the cutover.
- **Province "Otro/Extranjero".** Catalog includes 7 CR provinces only. Branches outside CR are out-of-scope — form blocks save with message *"Solo proveedores con dirección en Costa Rica"*.
- **Application crossing stage-expiry mid-edit.** Open draft when window closes: next field blur fails with the 422 path; banner explains.
- **PublicCode in legacy URLs/bookmarks.** Internal `/applications/{id}` routes preserved (numeric id remains primary key); display layer maps `PublicCode` ↔ id. Old bookmarks still work.
- **PublicCode collision at generation.** Generator retries on duplicate; three failed attempts log + throw; never expose collision to user.
- **Empty Impact on Application.** Application creation requires Impact upfront — cannot have null Impact at draft state. Impact step is first in the flow.
- **Reviewer with no Process memberships.** Reviewer dashboard empty-state: *"Aún no se le ha asignado a ningún grupo. Contacte al administrador."*
- **Forgot-password mid-session.** Authenticated user clicking the forgot link is redirected to the change-password flow.
- **Autosave failure (network/server).** Banner *"⚠ No guardado — reintentar"* with retry button; submit blocked until banner clears.
- **Reset token reuse.** Token marked used after first successful reset; reuse rejected with *"Enlace inválido o expirado."*
- **SupplierAdmin self-elevation attempt.** Direct URL access to admin-only routes returns 403 + audit event.
- **Reminder-email send failure.** Failure does not block stage progression; retries with exponential backoff (max 5); final failure logged on the existing admin email-queue surface.
- **No production data exists.** Schema cutover drops `Item.Impact` column outright; no migration of legacy per-item impact values is required.

## Requirements *(mandatory)*

### Functional Requirements

**Domain & architecture**

- **FR-001**: System MUST introduce a `Process` aggregate above `Group` with fields `Id`, `Name` (free text), `Status` (Active / Closed), `CreatedAt`. Every existing `Group` MUST belong to exactly one `Process`.
- **FR-002**: System MUST scope `UserGroupMembership` so that reviewers may belong to multiple Groups across one or more Processes, while Applicants belong to exactly one Group at a time and transitively to that Group's Process.
- **FR-003**: System MUST introduce a base `Plantilla` entity owned by *Administración*. A Plantilla MUST hold: minimum quotations per item, list of `ImpactTemplate` references, required-field toggles, and stage-expiry overrides.
- **FR-004**: Assigning a base Plantilla to a Process MUST create a snapshot row (`ProcessPlantilla`) whose payload is independent of subsequent base edits.
- **FR-005**: System MUST relocate `Impact` from `Item` to `Application`. Application creation MUST capture Impact (one `ImpactTemplate`) upfront, then accept items inline without leaving the screen.
- **FR-006**: System MUST store stage-expiry windows for `solicitud`, `revision`, `facturacion` as platform defaults in `SystemConfiguration` with per-Process overrides on `Process`. Window expiry MUST hard-block stage-bound POSTs with HTTP 422.

**Role**

- **FR-007**: System MUST introduce role *SupplierAdmin* permitted to CRUD `Supplier` + `SupplierBranch` and toggle `Supplier.IsCompliant`. *SupplierAdmin* MUST be denied access to Applications, Users, Processes, Reports, Groups (HTTP 403 + audit event).

**Identifier & display**

- **FR-008**: System MUST generate an opaque `Application.PublicCode` matching regex `^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$` (base32, excluding ambiguous characters 0/O/1/I/L) and surface it everywhere the Application identity appears (applicant dashboard, reviewer queue, signing inbox, Funding Agreement PDF, every notification email). The internal numeric `Id` MUST remain the primary key.

**Supplier management**

- **FR-009**: Supplier search MUST support autocomplete by `Name` OR `CedulaJuridica`. No-match path MUST allow the user to register a `SupplierBranch` (FR-012, FR-014).
- **FR-010**: `Supplier.IsCompliant` MUST be the single source of truth; all Applications referencing the supplier MUST read its live value (no per-Application copy). Non-compliant suppliers MUST remain visible per SBD regulation.
- **FR-011**: Supplier admin list MUST default-sort by `LastUsedAt` desc (derived from the most recent `Quotation`) and offer a Process filter.
- **FR-012**: `SupplierBranch` MUST include a `ContactPersonName` field.

**Forms & data quality**

- **FR-013**: Email + phone inputs MUST use input masks. Phone mask follows the CR format `8888-8888`. Email format validated client + server (RFC).
- **FR-014**: System MUST add a `Province` catalog (7 rows) and `Canton` catalog (~82 rows). `SupplierBranch` MUST reference both via foreign keys, and the UI MUST render them as cascading dropdowns (Cantón narrows on Province pick).
- **FR-015**: Every form MUST mark required fields visibly. Submit-time validation MUST enumerate every missing required field by name (not "form is invalid").
- **FR-016**: Draft Application form MUST autosave each field on `blur` and display *"✓ Guardado HH:MM"* on success. Failure MUST display *"⚠ No guardado — reintentar"* with a retry control; submit MUST remain blocked until the banner clears.
- **FR-017**: Submit MUST be disabled until the Application has ≥ 1 Item, Impact defined, and every required field complete. Submit MUST route to `/review` showing items / suppliers / totals / Impact with an explicit *"Confirmar y enviar"* button before final send.
- **FR-018**: `/profile` MUST allow the user to self-edit `FirstName`, `LastName`, `Phone`, `Address`. `Email`, `Role`, `Group`, `CodigoPersonal` MUST render as read-only fields with an *"administrado"* badge.
- **FR-019**: `User` MUST have a nullable `CodigoPersonal` free-text field, admin-set, surfaced on `/profile` (read-only to user), admin user form, and admin reports.
- **FR-020**: Form fields MUST support a `Hint` attribute (tooltip). Initial set: `Item.ProductName`, `Item.Categoria`, *Cantidad de cotizaciones*, *Cédula jurídica*, *Razón social*.
- **FR-021**: Dashboard queries MUST exclude soft-deleted Applications. The "deleted-still-active" defect path MUST be covered by an E2E regression.

**Currency**

- **FR-022**: Every applicant-facing surface displaying a CRC-converted USD amount MUST render the disclaimer *"El monto en colones puede variar según el tipo de cambio vigente en la fecha de compra."* The disclaimer string MUST live in the es-CR localization catalog as a single key.
- **FR-023**: BCCR exchange-rate auto-fetch and Tropic-based AI quotation extraction MUST NOT ship in this spec. Manual entry workflows remain.

**Notifications & timers**

- **FR-024**: Every stage-bound surface (applicant draft, reviewer queue row, signing inbox) MUST render a countdown banner for the active stage; expired windows MUST render *"Vencido — la etapa cerró el {{fecha}}"* in the danger style.
- **FR-025**: A hosted background service MUST evaluate stage-expiry windows hourly and send reminder emails at T-72h, T-24h, and expiry via the existing SMTP wiring. Retries follow NFR-002.

**Password UX**

- **FR-026**: Every password field MUST display a show/hide toggle.
- **FR-027**: Change-password and reset-password screens MUST render a live strength legend (minimum length, uppercase, number, special) that ticks each criterion as it is met.
- **FR-028**: System MUST provide an end-to-end forgot-password flow: `/forgot-password` → token email → `/reset-password?token=...` → set new password. Tokens MUST be single-use with a 60-minute TTL. Unknown-email responses MUST be indistinguishable from known-email responses.

**Copy & localization**

- **FR-029**: Public CTA MUST read *"¿Listo para acelerar tu negocio?"* with button *"Iniciar acompañamiento"*. The word *"financiamiento"* MUST be removed from every applicant-facing rendered surface; the *Funding Agreement* PDF MAY retain the legal term.
- **FR-030**: Welcome greeting MUST render *"Hola, {{Nombre}}"*. The string *"Bienvenido/a"* MUST NOT appear in any rendered applicant-facing surface.

**Public landing**

- **FR-031**: System MUST provide a logged-out `/` page rendering: the FR-029 hero CTA; three slot regions (*Reglamento (descargar)*, *Ejemplo de cotización (descargar)*, *Sponsor strip* reusing spec 019's brand kit). Slots without uploaded files MUST render a *"Próximamente"* placeholder. Files uploaded MUST flow through the existing `IObjectStorage` (spec 014).

**Admin dashboard**

- **FR-032**: Admin dashboard MUST render *Personas activas* (count of active applicants) and *Fondos entregados* (sum of executed `FundingAgreement.AmountDisbursed`) KPI tiles. Existing action KPIs MUST be preserved.
- **FR-033**: Pending-quotation tile MUST move from the admin dashboard to the reviewer dashboard with the same data source.
- **FR-034**: Admin user list group selector MUST be a two-level cascading filter *Process → Group* whose options follow FR-001.

### Non-Functional Requirements

- **NFR-001**: No production data exists. Migration drops `Item.Impact` column outright. New tables `Process`, `Plantilla`, `ProcessPlantilla`, `Province`, `Canton` ship via dacpac.
- **NFR-002**: Reminder-email background service MUST retry failed sends with exponential backoff, up to 5 attempts. Final failure MUST log to the existing admin email-queue surface.
- **NFR-003**: All new strings MUST register in the es-CR localization catalog (spec 012). English fallback only on catalog miss.
- **NFR-004**: Delivery bar — full Playwright E2E suite green before merge (project rule).
- **NFR-005**: No new managed (NuGet / npm) dependencies. New UX (input masks, autocomplete, hints, eye-toggle, strength legend) uses Tabler primitives + existing vendored JS modules.
- **NFR-006**: Supplier search MUST return results within 300 ms P95 at seed scale (≥ 200 suppliers).

### Key Entities

- **Process**: Annual program cycle (e.g. *Crocus 2025*, *Nexo 2026*). Has many `Group`s. Owns stage-expiry overrides. Owns a `ProcessPlantilla` snapshot.
- **Plantilla**: Reusable Application configuration template. References `ImpactTemplate`s, declares minimum quotations per item, required-field toggles, stage-expiry overrides.
- **ProcessPlantilla**: Snapshot of a base Plantilla taken at assignment time to a Process. Immutable to base-Plantilla edits.
- **Group**: Existing entity (spec 016) — sub-cohort within a `Process` (e.g. *Norte*, *Sur*, *Centro*). Scopes reviewer access and Applicant membership.
- **Application**: Existing aggregate, now carrying `PublicCode` (opaque base32 identifier) and an `Impact` value object (single `ImpactTemplate`).
- **Item**: Existing entity, no longer carries `Impact`.
- **Supplier**: Existing entity, with `IsCompliant` as the live "al día" signal.
- **SupplierBranch**: Existing entity, gains `ContactPersonName`, `ProvinceId`, `CantonId`.
- **Province / Canton**: New CR-geo catalog entities, cascaded in the UI.
- **User**: Existing `ApplicationUser`, gains `CodigoPersonal` free-text field.
- **PasswordResetToken**: New entity for the forgot-password flow (single-use, 60-min TTL).
- **AdminAuditEvent**: Existing entity (spec 016), extended with new event kinds for Process / Plantilla / SupplierAdmin actions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A trained admin can complete the full *create-Process → assign-Plantilla → create-Groups → assign-reviewers* flow in ≤ 3 minutes.
- **SC-002**: Editing a base Plantilla after assignment leaves the assigned Process snapshot unchanged in 100 % of cases (verified by snapshot equality before/after).
- **SC-003**: An applicant can complete *new draft → impact → 2 items → 2 quotations each → review → submit* without losing data when the browser is reloaded mid-form.
- **SC-004**: The submit button is disabled in 100 % of partial-data permutations tested by E2E and enabled only when all FR-017 conditions are met.
- **SC-005**: 100 % of Application identity displays use `PublicCode`. Grep of rendered HTML on every applicant-facing surface returns zero *"Solicitud N.º \d+"* matches.
- **SC-006**: A user assigned only *SupplierAdmin* role is allowed on `/admin/suppliers*` and denied (HTTP 403 + audit event) on every other admin surface.
- **SC-007**: Supplier name + cédula search returns autocomplete results within 300 ms P95 at seed scale ≥ 200 suppliers.
- **SC-008**: Stage-expiry T-72h / T-24h / expiry reminder emails fire with ≤ ±1 hour granularity, captured in integration tests via in-process MailKit.
- **SC-009**: Forgot-password end-to-end completes in ≤ 90 s from email send to first successful login with the new password. Tokens older than 60 min are rejected in 100 % of attempts.
- **SC-010**: Admin dashboard renders non-placeholder *Personas activas* + *Fondos entregados* values from seed; pending-quotation tile renders on reviewer dashboard and is absent from admin dashboard.
- **SC-011**: The deleted-still-active defect path (per PDF screenshot *"Su borrador para Application #10 está listo para enviar"* after deletion) is covered by an E2E regression and does not reproduce.
- **SC-012**: Public `/` page (logged-out) renders FR-029 CTA, three slot regions, and sponsor strip; grep of rendered HTML returns zero *"financiamiento"* matches across applicant-facing surfaces.
- **SC-013**: Profile page lets the user save edits to FirstName / LastName / Phone / Address; Email / Role / Group / CodigoPersonal render as read-only with *"administrado"* badge in 100 % of role permutations tested.
- **SC-014**: Required-field violations enumerate every missing field by name on submit; input masks reject malformed values inline.
- **SC-015**: *"Hola, {{Nombre}}"* greeting renders on every active-user welcome surface; *"Bienvenido/a"* string is absent from the es-CR catalog.
- **SC-016**: Full Playwright E2E suite is green before delivery (NFR-004).

## Assumptions

- The platform has no production data; the schema cutover (drop `Item.Impact`, add `Process` / `Plantilla` / `ProcessPlantilla` / `Province` / `Canton`) is performed without backfill.
- *"Crocus 2025"* and *"Nexo 2026"* are example names of in-progress program cycles, not enum values; a `Process` entity stores their names as free text.
- Plantilla assignment to a Process is one-to-one (one `ProcessPlantilla` per Process). Open question OQ-1 captures the open-ended case.
- Stage-expiry overrides live per-Process; per-Plantilla overrides are deferred (OQ-3).
- The base32 alphabet used for `PublicCode` is `A-H, J-N, P-Z, 2-9` (excluding 0, O, 1, I, L) to avoid dictation ambiguity.
- The CR-geo catalog seeds 7 provinces and ~82 cantones; foreign supplier addresses are out of scope and the form rejects them with a clear message.
- Reminder cadence is fixed at T-72h, T-24h, expiry; admin-configurable cadence is deferred (OQ-6).
- Reglamento + ejemplo files are uploaded by the admin team into existing `IObjectStorage` (spec 014); content ownership is deferred (OQ-5).
- The Funding Agreement PDF (spec 018) is the only surface allowed to retain the legal term *"financiamiento"*. PublicCode placement on that PDF is deferred to plan (OQ-4).
- BCCR exchange-rate auto-fetch and Tropic AI extraction are research workstreams. They are NOT in scope and must not be wired into the production codebase by this spec.
- OTP for sensitive profile edits, the guided onboarding tour, and the user-initiated email-change request flow are all deferred to future specs.
- Existing SMTP wiring is reused for forgot-password and reminder emails; no new email provider is introduced.
- Hint copy authorship is deferred to designer / copywriter (OQ-8); FR-020 captures the slots, not the strings.
- Process audit-event coverage extends `AdminAuditEvent` (spec 016 pattern) — pinned in plan (OQ-9).

## Dependencies

- **Spec 016** (`user-groups`) — `Group`, `UserGroupMembership`, `AdminAuditEvent`. Process sits above Group; group-overlap predicates extend to Process scope.
- **Spec 015** (`multi-currency-quotes`) — Currency catalog + ExchangeRate; FR-022 disclaimer attaches to CRC-converted USD surfaces.
- **Spec 017** (`admin-ux-facelift`) — KPI tile template, capability cards. FR-032 / FR-033 / FR-034 modify tiles in place.
- **Spec 019** (`programa-semilla-brand`) — Brand palette, sponsor strip, public page chrome. FR-031 reuses these.
- **Spec 012** (`es-cr-localization`) — Localization catalog. All new strings register here.
- **Spec 011** (`warm-modern-facelift`) — Form patterns, status pills, banner conventions for FR-016 / FR-024.
- **Spec 008** (`tabler-ui-strategy`) — Cascading select, modal, tooltip primitives.
- **Spec 014** (`azure-blob-storage`) — Reglamento + ejemplo file slots store via `IObjectStorage` if files supplied.
- **ASP.NET Identity** — `IdentityRole` add for SupplierAdmin; password-reset token provider integrated.
- **SMTP** — Existing wiring; no new provider.

## Out of Scope

- BCCR daily exchange-rate auto-fetch (research only — captured as future workstream).
- AI / Tropic quotation extraction (research only — PoC stays separate).
- Guided interactive onboarding tour (future).
- OTP for sensitive profile edits (future).
- User-initiated email-change request workflow (admin-only path stays).
- Foreign supplier addresses outside Costa Rica.
- Multi-Process Applicant membership (entrepreneur stays in one Group per FR-002).
- Visual-regression tooling (recurring open thread from specs 008 / 011 / 017 / 019).
- Audit log redesign for SupplierAdmin actions — extends `AdminAuditEvent`, no new entity.
- Public marketing site beyond the FR-031 landing scaffold.

## Open Questions

- **OQ-1**: Plantilla assignment cardinality per Process — one-to-one (default) vs many-to-one (one Process stacks multiple snapshots for different Application kinds). Pin in `/speckit-plan`.
- **OQ-2**: Process closure semantics on `FundingAgreement` aftermath — does closing a Process freeze its signed agreements? Default = yes (no further mutation).
- **OQ-3**: Stage-expiry override granularity — per-Process only (default) vs also per-Plantilla. Revisit in plan.
- **OQ-4**: PublicCode rendering on legacy Funding Agreement PDF template (spec 018) — template field swap or footnote? Pin in plan.
- **OQ-5**: Reglamento + ejemplo files — content ownership and authoring source (admin team vs Programa Semilla operations) — pending.
- **OQ-6**: Email-reminder cadence (T-72h / T-24h / expiry) — confirm with stakeholders or expose as admin config? Default = fixed cadence.
- **OQ-7**: SupplierAdmin scope — full CRUD on suppliers (default) vs validate-only-existing.
- **OQ-8**: Hint copy authorship — strings for FR-020's initial set; designer / copywriter delivery pending.
- **OQ-9**: Process audit-event coverage — extends `AdminAuditEvent` (spec 016 pattern)? Pin in plan.
- **OQ-10**: Province *"Otro/Extranjero"* — block in UI (default) vs catalog row. Revisit if foreign suppliers surface.
