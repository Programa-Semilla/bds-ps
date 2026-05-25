# Data Model: Structured-Field Input Masks

## Enum: `IdentificationType` (Domain)

`FundingPlatform.Domain/Enums/IdentificationType.cs`, `enum : byte` (mirrors `SupplierVerificationStatus`).

| Member | Value | Context | Canonical form | Validation regex |
|---|---|---|---|---|
| `CedulaFisica` | 1 | person | `1-2345-6789` (9 digits, 1-4-4) | `^\d-\d{4}-\d{4}$` |
| `CedulaJuridica` | 2 | supplier | `3-101-123456` (10 digits, 1-3-6) | `^\d-\d{3}-\d{6}$` |
| `Dimex` | 3 | person | plain digits, 11–12 | `^\d{11,12}$` |
| `Nite` | 4 | person + supplier | `3-101-123456` (10 digits, 1-3-6) | `^\d-\d{3}-\d{6}$` |
| `Pasaporte` | 5 | person | uppercased alnum, ≤20 | `^[A-Z0-9]{1,20}$` |

- Person selector offers {CedulaFisica, Dimex, Nite, Pasaporte}. Supplier selector offers {CedulaJuridica, Nite}.
- `CedulaJuridica` and `Nite` share the regex — distinguished only by the persisted member (documented in spec edge cases).
- es-CR display labels: "Cédula física", "Cédula jurídica", "DIMEX", "NITE", "Pasaporte".

## Value Object: `Identification` (Domain)

`FundingPlatform.Domain/ValueObjects/Identification.cs`, `sealed partial record` (mirrors `PublicCode`).

```
Identification
  Type   : IdentificationType
  Value  : string            // canonical form (see table)

  // ctor validates rawValue against Type's regex AFTER canonicalization; throws ArgumentException on mismatch
  Identification(IdentificationType type, string rawValue)

  static Identification From(IdentificationType type, string rawValue)
  static bool TryFrom(IdentificationType type, string? rawValue, out Identification? id)
  static bool IsValid(IdentificationType type, string? rawValue)   // used by the ViewModel attribute
  static string Canonicalize(IdentificationType type, string rawValue)  // strip → reformat per type
  override string ToString() => Value
```

- `Canonicalize`: strip to alphanumerics; for numeric types take the digits and regroup with hyphens per the type's pattern (cédula 1-4-4, jurídica/NITE 1-3-6, DIMEX none); passport uppercases alnum. Idempotent — feeding a canonical value back yields the same value.
- Validation = regex match on the canonicalized value. Length/shape only; **no check-digit** (Out of Scope).
- Per-type regex built with `[GeneratedRegex(..., RegexOptions.CultureInvariant)]`.

## Entity changes

### `Applicant` (`FundingPlatform.Domain/Entities/Applicant.cs`)

- New property `public IdentificationType? IdentificationType { get; private set; }` (nullable — optional admin-created users / legacy rows).
- `LegalId` stays the canonical string (existing unique index `UX_Applicants_LegalId`).
- New method `SetIdentification(IdentificationType type, string rawValue)` → `var id = Identification.From(type, rawValue); LegalId = id.Value; IdentificationType = type; UpdatedAt = ...`.
- Constructor + `UpdateProfile(...)` gain an `IdentificationType` parameter (or accept an `Identification`) so creation/edit routes the value through the VO. (Admin-optional path may pass `null` type + empty value.)

### `Supplier` (`FundingPlatform.Domain/Entities/Supplier.cs`)

- New property `public IdentificationType? IdentificationType { get; private set; }`.
- `CreateDraft(...)` gains an `IdentificationType` parameter; sets `LegalId = Identification.From(type, legalId).Value` and `IdentificationType = type`.
- `NormalizeLegalId(string)` extended to the canonical reformat (strip non-alnum, uppercase; 10 digits → `1-3-6`). Keeps signature; still called by `SupplierRepository.GetByLegalIdWithBranchesAsync` and on write → stored/queried values converge.

## Persistence (dacpac + EF)

### `dbo.Applicants` (`FundingPlatform.Database/Tables/dbo.Applicants.sql`)

```sql
[IdentificationType] TINYINT NULL,   -- spec 026; NULL = unassigned (legacy / non-applicant-role admin user)
```

### `dbo.Suppliers` (`FundingPlatform.Database/Tables/dbo.Suppliers.sql`)

```sql
[IdentificationType] TINYINT NULL,   -- spec 026
```

- `LegalId` columns unchanged (`NVARCHAR(50) NOT NULL`, unique). Canonical hyphenated values fit comfortably.
- EF: `ApplicantConfiguration` + `SupplierConfiguration` map `IdentificationType` (default byte-enum conversion; nullable).

## Seed data (`FundingPlatform.Infrastructure/Identity/IdentityConfiguration.cs`)

| Email | Old LegalId | New LegalId | Type |
|---|---|---|---|
| applicant@programa-semilla.test | `DEMO-APP-001` | `1-0001-0001` | CedulaFisica |
| reviewer@programa-semilla.test | `DEMO-REV-001` | `1-0001-0002` | CedulaFisica |
| demo-admin@programa-semilla.test | `DEMO-ADM-001` | `1-0001-0003` | CedulaFisica |

- Sentinel admin (`admin@programa-semilla.test`) has no `Applicant` row → no change.
- No seeded suppliers exist; supplier identification appears only via test-created suppliers.

## State / lifecycle

No new state machine. Identification is set at creation (Register / admin create / supplier create) and may be changed on admin user edit and supplier edit, always through the VO. Profile is read-only.
