# Quickstart: Spec 038 — Auditor + Provider Compliance

## Run

```bash
dotnet run --project src/FundingPlatform.AppHost      # dev (auto-deploys dacpac → role rename + new columns)
dotnet build FundingPlatform.slnx
```

## Manual walkthrough

1. **Auditor role** — sign in as `auditor@programa-semilla.test` / `Demo123!`. Confirm access to
   `/Admin/Suppliers` and that other `/Admin/*` areas remain blocked (capability parity with the old
   SupplierAdmin).
2. **Compliance edit (US1)** — open a provider (`/Admin/Suppliers/{id}`). Confirm: no "Factura electrónica"
   control; Hacienda/CCSS/SICOP are **dropdowns** (verbatim Spanish values, blank = "sin revisar"); a PME/PYME
   toggle. Set each + save; reload → values persist.
3. **Audit + freshness (US2)** — after the save, each changed field shows "revisado hoy por …". Use
   "Confirmar revisión" on a set field → timestamp refreshes, value unchanged. Confirm the field's "Confirmar
   revisión" is disabled when the status is unset. Recent admin "Actividad reciente" feed shows the
   `supplier.*` events.
4. **Warning (US3)** — set the warning flag + note as auditor. As a reviewer, open an application using that
   provider → warning + note shown prominently; reviewer cannot edit it; the application still advances.
5. **Notification (US4)** — create a provider via the applicant supplier-add flow → every auditor receives an
   email (smtp4dev in dev/E2E) with provider name, legal id, created time, creator, and a link to
   `/Admin/Suppliers/{id}`.

## Tests

```bash
dotnet test tests/FundingPlatform.Tests.Unit          # Supplier domain methods; RegulatoryStatusLabels; ReviewFreshness
dotnet test tests/FundingPlatform.Tests.Integration   # EditComplianceAsync writes audit rows; ConfirmReviewed refreshes timestamp; concurrency
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~ProviderCompliance|FullyQualifiedName~AuditorRole"
```

E2E (per CLAUDE.md delivery bar — filtered, not the full suite): one class per user story.
US4 uses `MailCaptureClient.WaitForAsync(filter: m => m.ToAddresses.Contains(auditorEmail))`. E2E provisions an
auditor via `/Account/SeedUser` + `/Account/AssignRole?role=Auditor`.

## Watch-outs

- **Role rename is broad** (~50 sites). After renaming, grep for any leftover `"SupplierAdmin"` /
  `SUPPLIERADMIN` string in src + tests.
- **PDF page** `_SupplierVerificationPage.cshtml` binds to the dropped booleans — it MUST be repointed to the
  new statuses or the funding-agreement PDF build breaks.
- **Allowlist**: send via the **Notifications** `IEmailSender` (allowlist-wrapped), NOT the direct-send
  Abstractions sender (which is not allowlisted) — otherwise dev/test mail escapes `@programa-semilla.test`.
- **Azure prod publish** uses `--no-drop`; dropping the 4 BIT columns there must be handled deliberately
  (dev/E2E are greenfield and drop freely).
