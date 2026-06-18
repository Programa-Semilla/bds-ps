# Contracts: Spec 038

## Application service — `ISupplierComplianceService`

`src/FundingPlatform.Application/Suppliers/Compliance/ISupplierComplianceService.cs`. Impl in
`Infrastructure/Services/SupplierComplianceService.cs` (mirrors `CompanyAdministrationService`: load → domain
method → stage audit → single `SaveChangesAsync`; map `DbUpdateConcurrencyException` → es-CR result).

```csharp
public interface ISupplierComplianceService
{
    // US1/US2/US3 — the supplier Detail "Edit compliance" POST.
    Task<SupplierComplianceResult> EditComplianceAsync(EditSupplierComplianceCommand cmd, CancellationToken ct);

    // US2 — "Reviewed — no change / re-authorize" for one regulatory field.
    Task<SupplierComplianceResult> ConfirmReviewedAsync(int supplierId, RegulatoryField field,
        string actorUserId, byte[] rowVersion, CancellationToken ct);
}

public sealed record EditSupplierComplianceCommand(
    int SupplierId,
    HaciendaStatus? Hacienda,
    CcssStatus? Ccss,
    SicopStatus? Sicop,
    bool IsPmeOrPyme,
    bool HasWarning,
    string? WarningNote,
    string ActorUserId,
    byte[] RowVersion);

public sealed record SupplierComplianceResult(bool Ok, string? ErrorEsCr);
// Ok=false with ErrorEsCr for: not-found, concurrency ("Los datos cambiaron; recargue la página."),
// validation (note >1000), or re-review-on-unset ("Defina un estado antes de confirmar la revisión.").
```

Behavior: `EditComplianceAsync` calls `Supplier.ApplyRegulatoryEdit(...)`, writes one `AdminAuditEvent` per
returned `RegulatoryChange` (action by kind/field), commits atomically. Empty change list → success, no audit,
no write. `ConfirmReviewedAsync` calls `Supplier.ConfirmRegulatoryReviewed(field,…)` (guards null status),
writes one `supplier.regulatory_reviewed` audit, commits.

## Application notifier — `IProviderCreatedNotifier`

`src/FundingPlatform.Application/Suppliers/Notifications/IProviderCreatedNotifier.cs`. Impl in
`Infrastructure/Suppliers/ProviderCreatedNotifier.cs`.

```csharp
public interface IProviderCreatedNotifier
{
    // US4 — best-effort; MUST NOT throw to the caller (catch+log internally).
    Task NotifyAuditorsAsync(int supplierId, CancellationToken ct);
}
```

Behavior: resolve all users in role `Auditor` (`Roles.NormalizedName == "AUDITOR"` join → email + display
name); load supplier (name, LegalId, CreatedAt, creator name if resolvable); render
`Views/Emails/Suppliers/ProviderCreatedAuditor.cshtml` (read-as-text, `{{TOKEN}}` replace — `InvitationEmailFactory`
pattern); compose absolute link to `/Admin/Suppliers/{id}` via `Notifications:BaseUrl` (dev: request host);
send **one message per auditor** through the **Notifications-path `IEmailSender`** (allowlist-wrapped in
non-prod). Subject (es-CR): `"Nuevo proveedor para revisar: {name}"`. Zero auditors → no-op. Any failure →
log, swallow.

**Trigger:** `CreateSupplierBranchHandler` (Infrastructure/Services) calls `NotifyAuditorsAsync(supplier.Id, ct)`
after its `SaveChangesAsync` succeeds, inside try/catch (never blocks creation — FR-024).

## Display helpers

- `RegulatoryStatusLabels` — `string Label(HaciendaStatus?)` / `(CcssStatus?)` / `(SicopStatus?)` returning the
  verbatim Spanish label or `"sin revisar"` for null. Also `IEnumerable<SelectListItem>` builders for the three
  dropdowns (value = numeric code, text = verbatim label, blank option = "sin revisar").
- `ReviewFreshness.Describe(DateTime? lastReviewedAt, string? byName, RegulatoryReviewSource? source)` → es-CR
  string: `"sin revisar"` | `"revisado hoy por {by}"` | `"revisado hace {n} días por {by}"` (source suffix
  e.g. `" (manual)"`). Pure; unit-tested.

## Web routes (AdminSuppliersController — `[SupplierAdminOnly]`, now Auditor-or-Admin)

| Method | Route | Change |
|---|---|---|
| POST | `/Admin/Suppliers/{supplierId}/Edit` | Re-bound to `EditSupplierComplianceCommand` (statuses + PME + warning + RowVersion) → `ISupplierComplianceService.EditComplianceAsync`. Name edit unchanged. |
| POST | `/Admin/Suppliers/{supplierId}/ConfirmReviewed` | **New.** Body: `field` (RegulatoryField) + `rowVersion` → `ConfirmReviewedAsync`. |
| GET | `/Admin/Suppliers/{supplierId}` | Detail VM gains statuses, PME, warning, per-field freshness, RowVersion. |

View models:
- `AdminSupplierDetailViewModel`: replace the 4 bools with `HaciendaStatus?/CcssStatus?/SicopStatus?` + per-field
  `*ReviewedAt/*ReviewedByName/*Source` + `IsPmeOrPyme` + `HasWarning` + `WarningNote` + `RowVersion`.
- `AdminEditSupplierViewModel`: replace the 4 bools with the three nullable status enums + `IsPmeOrPyme` +
  `HasWarning` + `[MaxLength(1000)] WarningNote` + `byte[] RowVersion`. (Name stays.)

## Role-rename contract (capability parity — FR-002)

Rename the role **string/enum/display** to `Auditor` at every inventoried site (research D1). **Unchanged
semantics:** an Auditor-only user can reach only `/Admin/Suppliers*` (the existing `SupplierAdminDenied` filter
behavior); Admin retains everything. Demo seed → `auditor@programa-semilla.test` / role `Auditor`. Dev seam
`/Account/AssignRole` allowlist gains `Auditor` (drops `SupplierAdmin`). Filter class names and supplier-list
DTO names retained (describe the screen, not the role).

## Email template

`Views/Emails/Suppliers/ProviderCreatedAuditor.cshtml` — es-CR, text-only wordmark (spec-019/021 email rules),
tokens: `{{ProviderName}}`, `{{ProviderLegalId}}`, `{{CreatedAt}}`, `{{CreatedByName}}`, `{{ReviewLink}}`.
Body prompts the auditor to review regulatory compliance.

## Out-of-scope reminder

No recommendation scoring, no delivery/warranty quote fields, no audit-stage workflow, no staleness blocking,
no Hacienda API, no in-app notifications (slices B/C/D / never).
