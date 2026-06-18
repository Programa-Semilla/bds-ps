# Research & Decisions: Spec 038

Resolves the spec's Open Questions and the plan-time design choices. Each entry: Decision / Rationale /
Alternatives. Grounded in a codebase survey (paths cited).

## D1 — Role rename strategy: rename the existing `AspNetRoles` row

**Decision:** Rename the existing `SupplierAdmin` role to `Auditor` by **updating the existing
`AspNetRoles` row** (`Name`→`Auditor`, `NormalizedName`→`AUDITOR`) in the post-deploy script, so existing
`AspNetUserRoles` memberships carry over automatically. Fresh DBs seed `Auditor` directly. Replace the
contents of `src/FundingPlatform.Database/PostDeployment/03_SeedSupplierAdminRole.sql` with an idempotent
"rename if SUPPLIERADMIN exists, else insert AUDITOR" block.

**Rationale:** Renaming the row preserves members with zero membership migration. The role name lives only in
`AspNetRoles.Name`/`NormalizedName` (no schema/FK depends on the string), so this is a data update, not a
migration. Idempotent and safe to re-run (dacpac post-deploy runs every deploy).

**Alternatives:** (a) New `Auditor` role + copy members + delete `SupplierAdmin` — more steps, transient
state. (b) Keep `SupplierAdmin` and alias — rejected; spec wants one coherent role.

**Blast radius (rename the role *string* + enum + display):** ~50 sites inventoried —
`IdentityConfiguration.cs` (roles array + demo user), `SupplierAdminOnlyAttribute`/`SupplierAdminDeniedAttribute`
constants, `AdminUserRole` enum value, 13 `[Authorize(Roles="Admin,SupplierAdmin")]` attributes, `User.IsInRole`
checks (`HomeController`, `AdminSuppliersController`), `UserAdministrationService` constant + validation message,
`AccountController` role-display map + `AssignRole` dev-seam allowlist, `StatusVisualMap`. Filter **class names**
(`SupplierAdminOnly*`) and the supplier-list DTO names (`SupplierAdminLastUsedRow`/`SupplierAdminFilter`) and the
audit code `supplier_admin.denied_access` describe the supplier-screen pattern, not the role identity — **keep
them** to bound churn (documented; revisit only if confusing). The deny-filter behavior (role can reach only
`/Admin/Suppliers*`) carries over unchanged → capability parity (FR-002).

## D2 — OQ: Auditor role es-CR display label

**Decision:** Role identifier `Auditor`; es-CR display label **"Auditor"** (replaces "Administrador de
proveedores" in `AccountController` role-display + `StatusVisualMap`).

**Rationale:** "Auditor" is correct es-CR and matches the spec/seed terminology. Single word, fits the
existing role-pill UI.

**Alternatives:** "Auditoría" (the function, not the actor) — rejected; the others ("Administrador",
"Revisor", "Solicitante") name the actor.

## D3 — Demo/seed account

**Decision:** Rename the demo seed `supplieradmin@programa-semilla.test` → **`auditor@programa-semilla.test`**
(password `Demo123!`), role `Auditor`. Still under `@programa-semilla.test` → covered by the default non-prod
allowlist so US4 mail capture works. Update CLAUDE.md's demo-seed list.

**Rationale:** Coherent naming; allowlist-safe; the spec explicitly says the auditor seed replaces the
supplier-admin one.

**Alternatives:** Keep the old email with the new role — rejected as confusing.

## D4 — Compliance status persistence: enum→TINYINT, verbatim labels in a display map

**Decision:** Three C# enums `HaciendaStatus : byte`, `CcssStatus : byte`, `SicopStatus : byte` (nullable on
the entity), mapped with `.HasConversion<byte?>()` to nullable `TINYINT` columns — the established pattern
(`IdentificationType`, `SupplierVerificationStatus` in `SupplierConfiguration.cs`). The **verbatim Spanish
labels** (which contain spaces and `/`) live in a `RegulatoryStatusLabels` display resolver, never in the DB.

**Rationale:** Stable numeric storage decouples the DB from label text; display stays verbatim per §28.5;
zero new persistence concepts. Numeric codes are assigned in the source order from spec §13 (see
contracts/interfaces.md value tables) starting at 1; `null` = "sin revisar".

**Alternatives:** Store the Spanish string in `NVARCHAR` — rejected (fragile matching, accent/whitespace
drift, larger rows, breaks the repo's enum-as-tinyint convention).

## D5 — Greenfield, no backfill

**Decision:** Drop the four BIT columns (`HasElectronicInvoice`, `IsCompliant{CCSS,Hacienda,SICOP}`); add the
new nullable status columns with no data translation. Existing rows read `null` → "sin revisar".

**Rationale:** Confirmed with stakeholder; matches the repo's greenfield convention (specs 029/035/036/037).
Dacpac column drop + add; `DropObjectsNotInSource` handles the removal on deploy.

**Alternatives:** Backfill `true→al día`/`false→sin información` — rejected; no business value, and `false`
historically meant "unset" not "non-compliant".

## D6 — Audit trail: extend generic `AdminAuditEvent`

**Decision:** Record regulatory/PME/warning changes via the existing `IAdminAuditEventWriter` with new
constants `supplier.regulatory_changed`, `supplier.regulatory_reviewed` (no-change), `supplier.pme_changed`,
`supplier.warning_changed`, plus `TargetTypeSupplier = "supplier"` and a `supplier.` prefix route in
`AdminAuditEventWriter.DeriveTarget` that sets **`TargetId = supplierId`** (not the usual sentinel "0", so the
trail is queryable per provider via `IX_AdminAuditEvents_Target`). Payload JSON:
`{ supplierId, field, oldValue, newValue, source, kind }` (values are the numeric enum codes or the bool).
Add es-CR phrases to `AdminAuditEventCopyProvider` so the admin "Actividad reciente" feed renders them.

**Rationale:** Matches the dominant audited-mutation pattern (`fund.*`, `company.*`); no new table (YAGNI).
The **freshness display** does not query this trail — it reads the per-field `…LastReviewedAt/By/Source`
columns on `Supplier`. So the trail is purely history.

**Alternatives:** Dedicated `ProviderRegulatoryAuditEvent` table with typed prev/new columns (seed §22.6) —
deferred. **Slice D caveat:** `AdminAuditEvents.ActorUserId` is `NOT NULL FK→AspNetUsers`; the Hacienda API
job (slice D) has no human actor, so D must either seed a system principal or introduce the dedicated table
then. Flagged so D doesn't get blocked. For slice A every change has a real auditor actor → fine.

## D7 — Audited mutation flow: introduce `SupplierComplianceService`

**Decision:** New `ISupplierComplianceService` (Application) + `SupplierComplianceService` (Infrastructure),
mirroring `CompanyAdministrationService`: it loads the supplier, calls the domain method(s), stages audit
rows, and commits atomically in one `SaveChangesAsync`. The current `AdminSuppliersController.Edit` (which
mutates the entity directly with no audit) routes its compliance/PME/warning save through this service; a new
`ConfirmReviewed` action does likewise. Name edit, branch edit, verify, reject stay on their current path
(out of this slice's audit scope).

**Rationale:** The existing supplier Edit has no service and no audit; the repo's audited-admin-mutation
precedent is a service. Keeps audit + commit atomic (audit row never orphaned if save fails).

**Alternatives:** Wire `IAdminAuditEventWriter` directly into the controller — rejected; violates the
Application-orchestration precedent and leaks unit-of-work concerns into Web.

## D8 — Domain methods on `Supplier`

**Decision:**
- `ApplyRegulatoryEdit(HaciendaStatus?, CcssStatus?, SicopStatus?, bool isPmeOrPyme, bool hasWarning, string? warningNote, string actorUserId, DateTime nowUtc)` → returns `IReadOnlyList<RegulatoryChange>`. For each regulatory field whose **value changed**, sets `…LastReviewedAt=now`, `…LastReviewedBy=actor`, `…Source=Manual` and emits a `regulatory_changed` change; PME and warning changes emit their own change kinds (no last-reviewed metadata). Unchanged fields are untouched.
- `ConfirmRegulatoryReviewed(RegulatoryField field, string actorUserId, DateTime nowUtc)` → refreshes that field's last-reviewed metadata and returns a `regulatory_reviewed` (no-change) marker. **Guards: throws if the field's status is null** (resolves OQ below).
- `SetWarning`/`SetPmeOrPyme` are folded into `ApplyRegulatoryEdit` for the single Edit POST; warning-note trimmed, ≤1000.

**Rationale:** Rich domain; the service just audits the returned change list. Per-field metadata update on
*change* (plus the explicit re-authorize) gives correct freshness semantics.

## D9 — OQ: "Reviewed — no change" before a value is set

**Decision:** **Disabled until a value exists.** The domain `ConfirmRegulatoryReviewed` guards (throws) on a
null status; the UI hides/disables the per-field "Confirmar revisión" control when the status is unset.

**Rationale:** Re-authorizing "nothing" is meaningless; freshness only matters once a value is recorded.
Simplest, avoids a null "reviewed value".

**Alternatives:** Allow it and stamp a timestamp with no value — rejected (confusing freshness on an unset
field).

## D10 — OQ: warning-note max length

**Decision:** `NVARCHAR(1000)`, mirroring `RejectionReason`. Empty note + flag off clears the warning.

**Rationale:** Consistency with the existing free-text reason field; ample for a note.

## D11 — New-provider notification: send via the allowlist-wrapped Notifications `IEmailSender`

**Decision:** New `IProviderCreatedNotifier` (Application) + `ProviderCreatedNotifier` (Infrastructure). It
resolves all users in role `Auditor` (EF join on `Roles.NormalizedName == "AUDITOR"`, select email + name),
renders the email body from a text-template (`Views/Emails/Suppliers/ProviderCreatedAuditor.cshtml`, read as
text with `{{TOKEN}}` replacement — the `InvitationEmailFactory` pattern), and sends **one message per
auditor through the Notifications-path `IEmailSender`** (the one wrapped by `RecipientAllowlistFilter` in
non-prod). Triggered best-effort after `CreateSupplierBranchHandler` commits; failures are caught + logged and
never block provider creation (FR-024).

**Rationale — critical finding:** The **direct-send (Abstractions) `IEmailSender`** used by invitations/
forgot-password is **NOT** wrapped by `RecipientAllowlistFilter` (only the Notifications path is —
`NotificationsServiceCollectionExtensions`). FR-023 requires the allowlist to apply, so we must use the
Notifications sender. It is not coupled to the outbox — `SendAsync(EmailMessage)` just sends a fully-rendered
message — so it fits a provider-scoped send while giving allowlist protection and correct `Sender:*` config
for free.

**Alternatives:** (a) Reuse the invitation direct-send path — rejected; bypasses the allowlist (would email
real addresses in dev/test). (b) Route through the spec-021 outbox — rejected; the outbox is application-scoped
(`EnqueueAsync(eventType, applicationId, versionHistoryId, …)`), no natural fit for a provider event, and
would need a new non-application event shape.

**Trigger scope:** The only provider-creation path today is the applicant draft-supplier flow
(`CreateSupplierBranchHandler` → `Supplier.CreateDraft` → `SaveChanges`). Wire the notifier there. (Admin has
no create path; verify/reject/edit are not "creation".) Draft suppliers are exactly what need auditor review,
so this satisfies "any creation path" for the current surface. Recipients with zero auditors → no-op.

## D12 — Freshness display

**Decision:** A small es-CR helper formats `…LastReviewedAt` as relative recency ("revisado hace N días por
<nombre>", "hoy", "sin revisar") from the per-field columns. Shown on the auditor's provider Detail and on the
reviewer-facing supplier/quote render during application review.

**Rationale:** Direct column read; no audit-table query. Source tag (`Manual`/`Api`/`System`) shown alongside
(only `Manual` occurs in slice A).

## D13 — Review-surface render sites for warning + freshness

**Decision:** Render the provider **warning banner** + per-field **freshness** in: (1) the auditor's
`Views/Admin/Suppliers/Detail.cshtml`; (2) the reviewer's view of a submitted application where supplier/quote
info renders. Candidate reviewer render sites confirmed during survey: the quotation block in
`Views/Application/Review.cshtml` (supplier names per quote) and the spec-020 AI-comparison surface that
renders supplier data. **Implementation task:** pin the exact reviewer review partial(s) by grepping the
quote/supplier render and add a shared `_SupplierComplianceBadge`/warning partial so all sites stay DRY. The
post-approval `_SupplierVerificationPage.cshtml` PDF (spec 018) currently prints Hacienda/CCSS/SICOP as
"Al día"/"Pendiente" from the old booleans — it MUST be updated to read the new statuses (otherwise it breaks
when the bools are dropped); minimal change: map status→label, keep the table.

**Rationale:** Warnings/freshness must reach reviewers "during review" (FR-016/019). The PDF page is a forced
touch because it currently binds to the dropped booleans.

**Alternatives:** New standalone reviewer "supplier compliance" page — heavier; deferred unless the inline
badge proves insufficient.

## D14 — es-CR copy

**Decision:** Extend `AdminSuppliersResources` with the new labels (status field headings, PME, warning, the
re-review action, freshness phrases) and a `RegulatoryStatusLabels` map for the verbatim status values. Email
body in `Views/Emails/Suppliers/ProviderCreatedAuditor.cshtml`. All es-CR.

## D15 — Concurrency

**Decision:** Add `RowVersion ROWVERSION` to `dbo.Suppliers` + `[Timestamp] byte[] RowVersion` on the entity
+ `.IsRowVersion()` in `SupplierConfiguration`. The supplier Detail edit posts the RowVersion; on
`DbUpdateConcurrencyException` the service surfaces an es-CR "recargue" message (existing pattern).

**Rationale:** Constitution OC mandate; real multi-auditor + slice-D API write contention. Token is needed by
D regardless.

## Open items intentionally deferred (not slice A)

- Recommendation scoring + delivery/warranty quote fields → **B**.
- Auditor workflow stage, checklists, inbox, PDF-to-auditor → **C**.
- 1-month staleness **blocking** + daily Hacienda API sync (+ the automated-actor audit concern from D6) → **D**.
- Dedicated `ProviderRegulatoryAuditEvent` table — only if D needs it.
- In-app notification center — out of scope entirely.
