# Contract: Server-Side Identification Validation

Authority: the domain `Identification` value object. Surfaces (ViewModel attribute, controllers) delegate to it; the client mask is never trusted (FR-014).

## Canonical form + regex (per `IdentificationType`)

| Type | Canonical form | Regex (post-canonicalize) | Max stored len |
|---|---|---|---|
| CedulaFisica | `1-2345-6789` | `^\d-\d{4}-\d{4}$` | 11 |
| CedulaJuridica | `3-101-123456` | `^\d-\d{3}-\d{6}$` | 12 |
| Dimex | `12345678901` / `123456789012` | `^\d{11,12}$` | 12 |
| Nite | `3-101-123456` | `^\d-\d{3}-\d{6}$` | 12 |
| Pasaporte | `A1B2C3` (uppercased) | `^[A-Z0-9]{1,20}$` | 20 |

`Identification.Canonicalize(type, raw)` strips to alphanumerics then regroups; validation = regex match on the canonical value. Shape/length only — no check-digit (Out of Scope).

## Presence rule (FR-015)

| Context | Rule |
|---|---|
| Required field (applicant Register; admin user create/edit when Role = Applicant; supplier add) | type **and** value both required |
| Optional field (admin user create/edit when Role ≠ Applicant) | both present or both absent |

Messages (es-CR):
- value present, type missing → **"Seleccione el tipo de identificación."**
- type present, value missing (required) → **"La identificación es obligatoria."**
- value present, type present, shape mismatch → **"La identificación no tiene el formato de {tipo}."** (`{tipo}` = es-CR label)

All identification errors are added to `ModelState` and surfaced **together** with other validation errors (Quality Gate).

## Reusable attribute

`IdentificationFormatAttribute` (Web/Validation) takes the sibling type-property name as a ctor arg (default `"IdentificationType"`; supplier VM passes `nameof(SupplierIdentificationType)`), resolves that property on the ViewModel, and returns invalid when `!Identification.IsValid(type, value)` for a present value. Combined with the controller-level presence check (mirroring the existing "Cédula obligatoria para Solicitante" check) it covers FR-014 + FR-015.

## Supplier lookup normalization (FR-013)

`SupplierController.Search` / `SupplierCatalogService.SearchByLegalIdAsync(legalId, …)` → `SupplierRepository.GetByLegalIdWithBranchesAsync(legalId)` already calls `Supplier.NormalizeLegalId(legalId)` before `s.LegalId == canonical`. With `NormalizeLegalId` extended to the canonical reformat, a query typed as `3101123456`, `3-101-123456`, or `3 101 123456` all canonicalize to `3-101-123456` and match the stored supplier. Endpoint route/shape unchanged (`GET …/Supplier/Search?legalId=`).

## Affected write paths

| Path | Change |
|---|---|
| `AccountController.Register` | build `Applicant` via the identification VO (type from `RegisterViewModel.IdentificationType`) |
| `AdminUsersController.Create` / `Edit` → `IUserAdministrationService` | `CreateUserRequest` / update request gain `IdentificationType`; Applicant set via VO; presence check when Role = Applicant |
| `SupplierController.Add` | pass `AddSupplierViewModel.SupplierIdentificationType` to `Supplier.CreateDraft`; validate via VO (existing try/catch path may translate the VO throw to ModelState) |
| `AccountController.Profile` (GET) | rebuild `ProfileViewModel` with persisted type + canonical value (read-only); POST unchanged |
