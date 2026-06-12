# Phase 1 Data Model: Batch user creation

**No persisted schema change.** This feature introduces only transient (in-memory) types and reuses existing entities. The dacpac is untouched (Constitution IV).

## Reused persisted entities (unchanged)

| Entity | Table | Role in this feature |
|---|---|---|
| `ApplicationUser` | `dbo.AspNetUsers` | Created per valid row, no password (spec 033). |
| `Applicant` | `dbo.Applicants` | Created 1:1 with the account; carries `LegalId` (canonical cédula física), `IdentificationType = CedulaFisica`, `FirstName`, `LastName`, `Email`, `Phone` (normalized), and the unique `UserCode` (spec 032). No new column. |
| `UserGroupMembership` | `dbo.UserGroupMemberships` | One row inserted per created user → resolved `Grupo`. Composite key `(UserId, GroupId)`; existing FKs/cascades. |
| `Group` / `Process` / `Fund` | `dbo.Groups` / `dbo.Processes` / `dbo.Funds` | Read-only here. Name-resolved (globally unique names) + chain-validated (`Group.ProcessId → Process.FundId`). |
| `PasswordResetToken` | `dbo.PasswordResetTokens` | One 72h single-use invitation token issued per created user (spec 033), via the existing handler. |

## New transient types (Application layer — `FundingPlatform.Application/Admin/Users/Batch/`)

### `BatchUserCsvColumns`
Canonical header definition (names + order) shared by the template download, header validation, and column mapping. Header labels are es-CR and match the intake spreadsheet:

`Grupo, Proceso, Fondo, Nombre, Apellido 1, Apellido 2, Email, Teléfono, Cédula, Código de usuario`

> Note: `Proceso` is the column the spreadsheet labels "Recurso 2025"; the template standardizes the header to `Proceso`. Header matching is trim + case/accent-insensitive and BOM-tolerant on the first column.

### `BatchUserImportRow`
Raw parsed cells for one data row.

| Field | Type | Notes |
|---|---|---|
| `RowNumber` | `int` | 1-based **data** row number (header excluded), used in the report. |
| `Grupo` | `string` | Raw cell. |
| `Proceso` | `string` | Raw cell (validation-only). |
| `Fondo` | `string` | Raw cell (validation-only). |
| `Nombre` | `string` | → FirstName. |
| `Apellido1` | `string` | Required. |
| `Apellido2` | `string` | Optional; `LastName = (Apellido1 + " " + Apellido2).Trim()`. |
| `Email` | `string` | → account email. |
| `Telefono` | `string` | Optional; normalized via `PhoneNormalizer`. |
| `Cedula` | `string` | → validated/canonicalized cédula física. |
| `CodigoUsuario` | `string` | → UserCode (≤50). |

### `BatchUserCreateOutcome`
Per-row result (discriminated by `Succeeded`).

| Field | Type | Notes |
|---|---|---|
| `RowNumber` | `int` | Mirrors the data row. |
| `KeyField` | `string` | Email (or Código if email blank) — identifies the row in the report. |
| `Succeeded` | `bool` | |
| `Reason` | `string?` | es-CR reason when `Succeeded == false`; null otherwise. |

### `BatchUserCreateResult`
Partition over all data rows.

| Field | Type | Notes |
|---|---|---|
| `Succeeded` | `IReadOnlyList<BatchUserCreateOutcome>` | Created rows (carry email for the invitation pass). |
| `Errored` | `IReadOnlyList<BatchUserCreateOutcome>` | Skipped rows + es-CR reasons. |
| invariant | — | `Succeeded.Count + Errored.Count == data row count` (SC-002). |

## Field validation & normalization rules (per row)

| Field | Rule | On failure |
|---|---|---|
| `Nombre` | required (non-blank after trim) | row errored — "Falta el nombre." |
| `Apellido 1` | required | row errored — "Falta el primer apellido." |
| `Apellido 2` | optional | — (joined into LastName when present) |
| `Email` | required; valid email; not already in system; not an earlier in-file duplicate | row errored — blank / inválido / "ya está en uso" / "duplicado en el archivo" |
| `Teléfono` | optional; `PhoneNormalizer`: take first number, strip leading `506` | never fails the row |
| `Cédula` | required; `Identification.TryFrom(CedulaFisica,…)`; not already in system; not an earlier in-file duplicate | row errored — falta / inválida / "ya está en uso" / "duplicado en el archivo" |
| `Código de usuario` | required; ≤50 chars; unique among applicants; not an earlier in-file duplicate | row errored — falta / longitud / "ya está en uso" / "duplicado en el archivo" |
| `Grupo`/`Proceso`/`Fondo` | each resolves by name (0/1) **and** forms a valid chain | row errored — "Grupo/Proceso/Fondo no existe" or "El grupo no pertenece al proceso/fondo indicado" |

**Role** is fixed to `Applicant` (Solicitante) for every row; there is no role column.

## Mapping to the reused `CreateUserRequest`

For a valid row:

```
CreateUserRequest(
    FirstName        = Nombre.Trim(),
    LastName         = (Apellido1 + " " + Apellido2).Trim(),
    Email            = Email.Trim(),
    Phone            = PhoneNormalizer.Normalize(Telefono),
    Role             = "Applicant",
    LegalId          = Identification canonical value (cédula física),
    GroupIds         = [ resolvedGroup.Id ],
    IdentificationType = CedulaFisica,
    UserCode         = CodigoUsuario.Trim())
```

The result's `DomainError.Code` maps to an es-CR row reason:

| Code | es-CR row reason |
|---|---|
| `EMAIL_IN_USE` (Identity dup) | "El correo ya está en uso." |
| `LEGAL_ID_IN_USE` | "La cédula ya está en uso." |
| `USER_CODE_IN_USE` | "El código de usuario ya está en uso." |
| `GROUP_NOT_FOUND` / `AT_LEAST_ONE_GROUP` | should not occur (chain pre-validated) — defensive es-CR fallback |

## State / lifecycle

No new state machine. Per created user, the spec-033 invitation lifecycle applies unchanged (72h single-use token; resend supersedes). The batch itself is stateless and leaves no record beyond the created users + the audit rows already written by `CreateUserAsync` (membership-update audit).
