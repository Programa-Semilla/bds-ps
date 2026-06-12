# Phase 1 Contracts: Batch user creation

## 1. CSV template contract

**Header row** (exact order; es-CR labels). Header matching is trim + case/accent-insensitive and tolerates a leading UTF-8 BOM on the first column:

```
Grupo,Proceso,Fondo,Nombre,Apellido 1,Apellido 2,Email,Teléfono,Cédula,Código de usuario
```

**Downloadable template** (`GET /Admin/Users/Batch/Template`) returns this header plus one commented/example data row, `Content-Type: text/csv; charset=utf-8`, `Content-Disposition: attachment; filename="plantilla-usuarios.csv"`.

**Data row rules**:
- Up to **200** data rows (rows beyond the header). More → whole-file rejection.
- `Apellido 2` and `Teléfono` may be empty; all other columns are required per row (a missing required cell errors that row, not the file).
- Role is implicit: every row creates a **Solicitante**. There is no role column.
- `Proceso`/`Fondo` are validation-only (confirm the chain); they are not stored.

**Example**:
```
Grupo,Proceso,Fondo,Nombre,Apellido 1,Apellido 2,Email,Teléfono,Cédula,Código de usuario
Norte,Migración inicial,Fondo General,Ana,Rojas,Mora,ana.rojas@example.cr,506 8888 1111,1-1234-5678,COD-001
Sur,Migración inicial,Fondo General,Luis,Mora,,luis.mora@example.cr,7777-2222 / 8888-3333,2-3456-7890,COD-002
```

## 2. Service contract — `IUserAdministrationService.CreateUsersBatchAsync`

```csharp
// FundingPlatform.Application/Admin/Users/IUserAdministrationService.cs  (added method)
Task<BatchUserCreateResult> CreateUsersBatchAsync(
    IReadOnlyList<BatchUserImportRow> rows,
    string actorUserId,
    CancellationToken ct);
```

**Behavior**:
1. Pre-scan for in-file duplicates (Email / canonical Cédula / Código). Mark later occurrences errored ("duplicado en el archivo").
2. For each not-yet-errored row, in order:
   a. Validate required cells; normalize phone; validate+canonicalize cédula física; check Código length.
   b. Resolve `Fondo`/`Proceso`/`Grupo` by name and validate the `Group.ProcessId → Process.FundId` chain. On failure → errored.
   c. Build `CreateUserRequest` (role `Applicant`, `GroupIds = [resolvedGroup.Id]`, canonical LegalId, `CedulaFisica`, trimmed UserCode).
   d. Call `CreateUserAsync`. On `Succeeded` → record success (carry email). On failure → map `DomainError.Code` to an es-CR reason → errored.
3. Return `BatchUserCreateResult { Succeeded, Errored }`.

**Guarantees**:
- Per-row atomic (each `CreateUserAsync` commits independently); a later row's failure never rolls back an earlier success.
- `Succeeded.Count + Errored.Count == rows.Count`.
- **Does not** issue invitations or send email (that is the controller's HTTP-context-bound responsibility, per the invitation seam).
- **Does not** parse CSV (the controller parses and hands typed rows in).

**Postcondition for the controller**: the `Succeeded` outcomes carry the email needed to issue the spec-033 invitation per created user.

## 3. Controller contract — routes on `AdminUsersController` (`[Route("Admin/Users")]`, admin-only)

| Route | Method | Purpose | Result |
|---|---|---|---|
| `Admin/Users/Batch` | GET | Render the upload page (file input, template-download link, 200-row + CSV-only hints, all es-CR). | `View(Batch)` |
| `Admin/Users/Batch` | POST `[ValidateAntiForgeryToken]` | Accept `IFormFile csv`. Run **file-level validation** → on failure re-render `Batch` with one es-CR message and create nothing. On success: parse → `CreateUsersBatchAsync` → for each succeeded row call `IssueAndSendInvitationAsync(email)` (best-effort) → render `BatchResult`. | `View(BatchResult, AdminUserBatchResultViewModel)` |
| `Admin/Users/Batch/Template` | GET | Stream the CSV template. | `File(text/csv)` |

**File-level rejection conditions** (FR-003) → re-render `Batch` with the first matching es-CR message, create nothing:
- not a `.csv` / unreadable / parse error,
- header missing or not matching the template (order/names),
- zero data rows,
- more than 200 data rows,
- (defensive) upload byte size above a small in-memory cap.

**Authorization**: same as the rest of `AdminUsersController` (administrator only). No anonymous access.

**Invitation pass**: identical to single-create — `IssueAndSendInvitationAsync` issues a fresh 72h single-use token (superseding prior unused), composes the absolute `/Account/ResetPassword` link, and best-effort sends the es-CR invitation email (10s timeout, transport failure swallowed). A send failure does not change the row's "succeeded" status (FR-011).

## 4. View models (Web)

```csharp
public sealed class AdminUserBatchUploadViewModel
{
    public string? ErrorMessage { get; set; } // file-level rejection (es-CR), shown on re-render
}

public sealed record AdminUserBatchResultRow(int RowNumber, string KeyField, string? Reason);

public sealed class AdminUserBatchResultViewModel
{
    public IReadOnlyList<AdminUserBatchResultRow> Succeeded { get; init; } = [];
    public IReadOnlyList<AdminUserBatchResultRow> Errored { get; init; } = [];
    public int TotalRows => Succeeded.Count + Errored.Count;
}
```

## 5. es-CR resource keys (added to `AdminUsersResources`)

Indicative keys (final copy in implementation):
`BatchTitle`, `BatchUploadHelp`, `BatchTemplateDownload`, `BatchFileLabel`, `BatchSubmit`,
`BatchError_NotCsv`, `BatchError_HeaderMismatch`, `BatchError_Empty`, `BatchError_TooManyRows` ("El archivo supera el máximo de 200 filas."),
`BatchResultTitle`, `BatchResultSucceededHeading`, `BatchResultErroredHeading`, `BatchResultRowFormat`,
and row reasons: `BatchRow_MissingNombre`, `BatchRow_MissingApellido`, `BatchRow_EmailBlank`, `BatchRow_EmailInvalid`, `BatchRow_EmailInUse`, `BatchRow_EmailDupInFile`, `BatchRow_CedulaInvalid`, `BatchRow_CedulaInUse`, `BatchRow_CedulaDupInFile`, `BatchRow_CodigoBlank`, `BatchRow_CodigoTooLong`, `BatchRow_CodigoInUse`, `BatchRow_CodigoDupInFile`, `BatchRow_GroupNotFound`, `BatchRow_ProcessNotFound`, `BatchRow_FundNotFound`, `BatchRow_ChainMismatch`.
