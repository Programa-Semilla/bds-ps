# Implementation Plan: Structured-Field Input Masks

**Branch**: `026-input-masks` | **Date**: 2026-05-24 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/026-input-masks/spec.md`

## Summary

Make input masking consistent, identification-type-aware, and extensible. Generalize the existing hand-rolled `wwwroot/js/input-masks.js` into a data-driven, event-delegated mask **registry** (so it also masks AJAX-injected supplier partials and so a future field opts in with one entry). Introduce a domain `IdentificationType` enum and an `Identification` value object that validates + canonicalizes a legal ID against its type (rich-domain authority, mirroring `CurrencyCode`/`PublicCode`). Persist the type on `dbo.Applicants` and `dbo.Suppliers` (two nullable TINYINT columns, dacpac). Add a type selector that rebinds the field's mask on Register / admin user create+edit / supplier add, render identification read-only on Profile, validate server-side per type (errors aggregated), and normalize the supplier lookup through the canonical form so hyphenation variants still match. Update seeds + E2E to valid canonical values.

## Technical Context

**Language/Version**: C# 13 / .NET 10.0
**Primary Dependencies**: ASP.NET MVC, EF Core 10, ASP.NET Identity, .NET Aspire; vanilla JS (no new managed/vendored dep — FR-019)
**Storage**: SQL Server via dacpac (`FundingPlatform.Database`); two new nullable `TINYINT` columns
**Testing**: Playwright .NET (NUnit, Page Object Model, `AspireFixture`); unit tests for the `Identification` VO; integration tests hit a real DB
**Target Platform**: Linux server, server-rendered MVC
**Project Type**: Web (MVC monolith, Clean Architecture)
**Performance Goals**: client masking is O(input length) per keystroke; no server perf impact
**Constraints**: es-CR copy; vendored-only / no-CDN; all validation errors surfaced at once; schema-first (no EF migrations)
**Scale/Scope**: ~7 mask types; 4 person/supplier identification surfaces + Profile read-only; ~6 forms wired; ~3 new E2E classes + existing-test/seed updates

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Clean Architecture | PASS | `IdentificationType` enum + `Identification` VO in Domain; validation rule owned by Domain; Web ViewModels/attributes + JS are surface echoes; persistence in Infrastructure configs + dacpac. Dependencies point inward. |
| II. Rich Domain Model | PASS | The type↔shape invariant + canonicalization live in the `Identification` value object; `Applicant`/`Supplier` expose `SetIdentification(type, rawValue)` (validates, normalizes, sets the two columns). No validation logic in controllers beyond translating a domain failure into a ModelState message. Resolves REVIEW-SPEC note 1. |
| III. E2E (NON-NEGOTIABLE) | PASS | Per-US Playwright tests (register types/mask/round-trip/server-reject; supplier mask + hyphenation-tolerant lookup; email/phone coverage). POM maintained. Full suite is the delivery bar (SC-008). |
| IV. Schema-First dacpac | PASS | Columns added by editing `dbo.Applicants.sql` + `dbo.Suppliers.sql`; no EF migrations. Pre-production → seeds updated in place (C# Identity seeder + any post-deploy), no backfill (FR-020). |
| V. SDD | PASS | spec → this plan → tasks → implement. Stories independently testable. |
| VI. Simplicity / YAGNI | PASS | Registry justified by the explicit "cualquier otro que exista" requirement; speculative masks (bank/IBAN/postal) out of scope; no library added. |

**No violations → Complexity Tracking left empty.**

### Planning notes resolved (from REVIEW-SPEC.md)

1. **Domain placement** → `Identification` VO + `IdentificationType` enum own the rule (Principle II). ViewModel `[IdentificationFormat]` attribute and the JS mask are echoes that delegate to the domain validator.
2. **Profile editability** → Profile shows identification **read-only** ("administrado" badge), consistent with Email/Role. `ProfileViewModel` gains read-only `IdentificationType?` + `LegalId`; `UpdateProfileCommand` is unchanged (no identification write). (Confirmed with stakeholder.)

### Discoveries that corrected the spec

- The person legal ID lives on the **`Applicant`** entity / `dbo.Applicants`, **not** `ApplicationUser`/`AspNetUsers`. The new person column is `dbo.Applicants.IdentificationType`. Spec FR-008/Key-Entities/Dependencies updated accordingly.
- `ProfileViewModel` has no `LegalId` today and identity is admin-managed → Profile is read-only display (above).

## Project Structure

### Documentation (this feature)

```text
specs/026-input-masks/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions + rationale
├── data-model.md        # Phase 1 — IdentificationType, Identification VO, entity/column changes
├── quickstart.md        # Phase 1 — how to add/verify a mask; manual test script
├── contracts/
│   ├── mask-registry.md          # client mask registry shape + catalogue
│   └── identification-validation.md  # server enum↔regex + canonical form + messages
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── FundingPlatform.Domain/
│   ├── Enums/IdentificationType.cs                 # NEW — CedulaFisica, CedulaJuridica, Dimex, Nite, Pasaporte
│   ├── ValueObjects/Identification.cs              # NEW — (Type, Value) record; per-type GeneratedRegex; canonicalize
│   └── Entities/
│       ├── Applicant.cs                            # +IdentificationType?; SetIdentification(); ctor/UpdateProfile take it
│       └── Supplier.cs                             # +IdentificationType?; CreateDraft takes type; NormalizeLegalId → canonical
├── FundingPlatform.Application/
│   ├── Users/...                                   # CreateUserRequest/Update... gain IdentificationType
│   └── Suppliers/Services/SupplierCatalogService.cs # lookup query canonicalized (via Supplier.NormalizeLegalId)
├── FundingPlatform.Infrastructure/
│   ├── Persistence/Configurations/
│   │   ├── ApplicantConfiguration.cs               # map IdentificationType (TINYINT?)
│   │   └── SupplierConfiguration.cs                # map IdentificationType (TINYINT?)
│   ├── Persistence/Repositories/SupplierRepository.cs # already calls NormalizeLegalId — unchanged signature
│   └── Identity/IdentityConfiguration.cs           # seed LegalIds → valid canonical + set type
├── FundingPlatform.Database/Tables/
│   ├── dbo.Applicants.sql                          # + [IdentificationType] TINYINT NULL
│   └── dbo.Suppliers.sql                           # + [IdentificationType] TINYINT NULL
└── FundingPlatform.Web/
    ├── wwwroot/js/input-masks.js                   # REWRITE — registry, event-delegated, type-selector controller, dynamic-node scan
    ├── Validation/IdentificationFormatAttribute.cs # NEW — delegates to Identification domain validator
    ├── ViewModels/                                 # Register/AdminUserCreate/AdminUserEdit/AddSupplier/Profile gain IdentificationType
    └── Views/
        ├── Account/Register.cshtml                 # + type selector + data-mask; load input-masks.js
        ├── Account/Profile.cshtml                  # + read-only identification rows
        ├── Admin/Users/Create.cshtml + Edit.cshtml # + type selector; integrate with role-visibility JS; load input-masks.js
        ├── Supplier/Add.cshtml                      # + supplier type selector; data-mask; load input-masks.js
        ├── Supplier/_LookupEmpty.cshtml + _BranchPicker.cshtml # data-mask on email/phone (masked via delegation)
        └── Shared/_LegalIdField.cshtml (optional)  # extract a shared type-selector+input partial

tests/
├── FundingPlatform.Tests.Unit/                     # Identification VO: per-type valid/invalid/canonicalization
├── FundingPlatform.Tests.Integration/              # persistence round-trip of type+value; supplier lookup normalization
└── FundingPlatform.Tests.E2E/
    ├── PageObjects/{RegisterPage,SupplierPage,Admin/AdminUserCreatePage,AdminUserEditPage}.cs  # + type selector
    ├── Fixtures/AuthenticatedTestBase.cs           # RegisterUserAsync uses valid cédula + type
    └── Tests/{AuthenticationTests, SupplierQuotationTests, Admin/AdminUserLifecycleTests}.cs + NEW InputMaskTests
```

**Structure Decision**: Existing Clean-Architecture MVC monolith. No new projects. New code follows the established value-object pattern (`CurrencyCode`/`PublicCode`), EF `IEntityTypeConfiguration` pattern, dacpac table files, and Playwright POM. Optionally extract a `_LegalIdField.cshtml` partial (type selector + masked input) to avoid repeating markup across Register/admin/supplier — decided in tasks.

## Complexity Tracking

> No constitution violations. Section intentionally empty.
