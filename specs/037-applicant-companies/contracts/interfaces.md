# Contracts & Interfaces: 037-applicant-companies

Phase 1 output. Interfaces, HTTP routes, and the batch-CSV contract. Signatures are illustrative (C#-flavored); names may be refined in implementation but the shapes are fixed.

---

## Domain repository — `ICompanyRepository` (Domain)

```csharp
public interface ICompanyRepository
{
    // Active companies for an applicant, ordered by Name (dropdown + admin list source).
    Task<IReadOnlyList<Company>> GetActiveByApplicantAsync(int applicantId, CancellationToken ct = default);

    // All companies (active + archived) for an applicant — admin Edit management card.
    Task<IReadOnlyList<Company>> GetAllByApplicantAsync(int applicantId, CancellationToken ct = default);

    // Ownership+active resolution for selection validation (returns null if not owned/not active).
    Task<Company?> GetActiveByIdForApplicantAsync(int companyId, int applicantId, CancellationToken ct = default);

    Task<Company?> GetByIdAsync(int companyId, CancellationToken ct = default);

    // Count of OTHER active companies (floor check excludes the candidate).
    Task<int> CountActiveExceptAsync(int applicantId, int exceptCompanyId, CancellationToken ct = default);

    Task AddAsync(Company company, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

`CompanyRepository` (Infrastructure) implements over `AppDbContext`.

---

## Admin service — `ICompanyAdministrationService` (Application interface, Infrastructure impl)

Mirrors `FundService`: folds DB access in, validates, writes `AdminAuditEvent`, single `SaveChangesAsync`. All methods are admin-actor-scoped (`actorUserId`).

```csharp
public interface ICompanyAdministrationService
{
    // Returns the applicant's companies (active + archived) for the admin Edit card.
    Task<IReadOnlyList<CompanyDto>> ListAsync(int applicantId, CancellationToken ct = default);

    // FR-005 — add a company to an existing applicant. Validates per-applicant active-name
    // uniqueness (D3). Returns the new CompanyDto or a UserFacingError (duplicate / invalid name).
    Task<CompanyMutationResult> AddAsync(int applicantId, string name, string actorUserId, CancellationToken ct = default);

    // FR-006 — rename. No-op (and no audit) when equal after trim. Uniqueness re-checked.
    Task<CompanyMutationResult> RenameAsync(int companyId, string newName, string actorUserId, CancellationToken ct = default);

    // FR-007/FR-008 — soft archive. Refuses when it is the applicant's last active company.
    Task<CompanyMutationResult> ArchiveAsync(int companyId, string actorUserId, CancellationToken ct = default);

    // FR-007 — unarchive. Refuses when the name now collides with an active company.
    Task<CompanyMutationResult> UnarchiveAsync(int companyId, string actorUserId, CancellationToken ct = default);
}

public sealed record CompanyMutationResult(CompanyDto? Company, UserFacingError? Error);
```

**Authorization note**: the controller resolves `companyId → owning applicant` and re-checks the route's `{id}` (user) owns it, so cross-user `companyId` tampering is rejected (no-disclosure 404).

---

## Application-create threading

```csharp
// Command (changed): CompanyName → CompanyId
public record CreateApplicationCommand(int ApplicantId, int CompanyId, int GroupId);

// ApplicationService.CreateApplicationAsync:
//   1. company = await _companyRepository.GetActiveByIdForApplicantAsync(cmd.CompanyId, cmd.ApplicantId)
//   2. if company is null  -> return CreateApplicationResult(0, UserFacingError(CompanyInvalid))   // FR-018/019
//   3. application = new Application(cmd.ApplicantId, cmd.GroupId, company.Id, company.Name)         // snapshot
//   ... existing PublicCode + version-history + persist
```

## Autosave (draft re-select, FR-015/016)

`AutosaveFieldCommand` shape unchanged. New field-key handled in `AutosaveFieldHandler.ApplyFieldMutation`:

```
field-key "CompanyId":
    parse int id from value (reject non-int)
    company = lookup Companies where Id==id && ApplicantId==application.ApplicantId && ArchivedAt IS NULL
    if null -> ArgumentException (es-CR mapped; ownership/active failure, no disclosure)
    application.SetCompany(company.Id, company.Name)     // re-copy snapshot
```

The pre-existing `"CompanyName"` field-key is **removed** (free text no longer allowed).

---

## HTTP routes (Web)

### Admin — company management (new), under existing `AdminUsersController` (`[Authorize(Roles="Admin,SupplierAdmin")] [SupplierAdminDenied] [Route("Admin/Users")]`)

| Method | Route | Purpose |
|---|---|---|
| POST | `Admin/Users/{id}/Companies/Add` | FR-005 add a company (name in body). Re-renders Edit with inline result/toast. |
| POST | `Admin/Users/{id}/Companies/{companyId}/Rename` | FR-006 rename. |
| POST | `Admin/Users/{id}/Companies/{companyId}/Archive` | FR-007/008 archive (floor-guarded). |
| POST | `Admin/Users/{id}/Companies/{companyId}/Unarchive` | FR-007 unarchive (collision-guarded). |

Each: resolve `{id}` → applicant; assert `{companyId}` (when present) belongs to that applicant (else 404 no-disclosure); call the service; surface es-CR result via toast/inline error (spec 024 pattern); `ValidateAntiForgeryToken`.

Create POST (`Admin/Users/Create`): for `Role == Applicant`, validate ≥1 non-empty company at the controller boundary (es-CR), pass `CompanyNames` in `CreateUserRequest`. Edit GET loads `UserDetailDto.Companies` for the management card.

### Admin — batch (changed)

- `GET Admin/Users/Batch/Template` — template includes the trailing `Nombre de la empresa` column + example value.
- `POST Admin/Users/Batch` — header must match the 11-column order; each row's company cell parsed and attached.

### Applicant — application create (changed)

- `GET /Application/Create` — populates company selection (`HasNoCompanies`/`IsSingleCompany`/`Companies`) via `ResolveActiveCompaniesAsync(userId)`.
- `POST /Application/Create` — validates posted `CompanyId` ∈ active companies (FR-018/019); builds `CreateApplicationCommand(applicantId, CompanyId, GroupId)`.

---

## Batch CSV contract (FR-009)

**Header (11 columns, exact order; accent/case/BOM-tolerant match):**
```
Grupo,Proceso,Fondo,Nombre,Apellido 1,Apellido 2,Email,Teléfono,Cédula,Código de usuario,Nombre de la empresa
```

`BatchUserCsvColumns`: `Count = 11`; `Ordered` gains `NombreEmpresa = "Nombre de la empresa"` last. `BatchUserImportRow` gains `string? NombreEmpresa`. Controller parses `Cell(cells, 10)`.

**Per-row company validation** (one company per row → `CreateUserRequest.CompanyNames = [trimmed]`):

| Condition | es-CR reason (`BatchUserRowReasons`) |
|---|---|
| Empty/whitespace cell | `CompanyNameBlank = "Falta el nombre de la empresa."` |
| Trimmed length > 200 | `CompanyNameTooLong = "El nombre de la empresa supera los 200 caracteres."` |

File-level rules (not-csv / 1 MiB cap / header-mismatch / empty / >200 rows) unchanged from spec 034.

---

## Admin audit contract

| Action constant | When |
|---|---|
| `company.create` | At user creation (per attached company) and via `AddAsync`. |
| `company.rename` | `RenameAsync` when the name actually changes. |
| `company.archive` | `ArchiveAsync` success. |
| `company.unarchive` | `UnarchiveAsync` success. |

Written through `IAdminAuditWriter.WriteAsync(AdminAuditEvent.Record(actorUserId, action, TargetTypeCompany, targetId, payloadJson))`; `DeriveTarget` routes `company.*` → `TargetTypeCompany`.

---

## es-CR copy (canonical strings)

| Context | String |
|---|---|
| Company dropdown placeholder | `— Seleccione una empresa —` |
| Create: company required | `Debe seleccionar una empresa.` |
| Create: no companies | `No tiene empresas asignadas. Contacte a un administrador para continuar.` |
| Admin create: ≥1 required | `Debe indicar al menos una empresa para el solicitante.` |
| Duplicate active name | `Ya existe una empresa activa con ese nombre para este solicitante.` |
| Archive last active | `No puede archivar la única empresa activa del solicitante.` |
| Unarchive name collision | `No se puede reactivar: ya existe una empresa activa con ese nombre.` |
| Submit with archived company | `La empresa seleccionada fue archivada. Seleccione una empresa activa para enviar.` |
| Batch: blank company | `Falta el nombre de la empresa.` |
| Batch: company too long | `El nombre de la empresa supera los 200 caracteres.` |
