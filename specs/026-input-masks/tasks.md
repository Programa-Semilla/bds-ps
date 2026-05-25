# Tasks: Structured-Field Input Masks

**Input**: Design documents from `/specs/026-input-masks/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: INCLUDED — Constitution Principle III mandates Playwright E2E per user story (golden path + key error scenarios); SC-008 requires the full suite green. Unit + integration complement E2E.

**Organization**: Tasks grouped by user story (US1 P1, US2 P2, US3 P3) for independent implementation/testing.

## Format: `[ID] [P?] [Story] Description with file path`

- **[P]**: parallelizable (different file, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (story phases only)

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 [P] Add a CR-identification test-data helper (valid + invalid cédula física / cédula jurídica / DIMEX / NITE / passport generators derived from a per-test seed, producing canonical hyphenated values) in `tests/FundingPlatform.Tests.E2E/Support/IdentificationData.cs` — shared by US1 + US2 E2E.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ No user-story work begins until this phase is complete.**

- [X] T002 [P] Create `IdentificationType` enum (`: byte`, members `CedulaFisica=1, CedulaJuridica=2, Dimex=3, Nite=4, Pasaporte=5`) with es-CR `[Display(Name=...)]` labels in `src/FundingPlatform.Domain/Enums/IdentificationType.cs`.
- [X] T003 Create `Identification` value object (sealed partial record; per-type `[GeneratedRegex]`; `Canonicalize(type, raw)`, ctor validates canonical value, `From` / `TryFrom` / `IsValid`) in `src/FundingPlatform.Domain/ValueObjects/Identification.cs` — mirrors `CurrencyCode`/`PublicCode`. (dep: T002)
- [X] T004 [P] Unit tests for `Identification`: each type valid + invalid + canonicalization (e.g. `3101123456` → `3-101-123456`) + idempotence + jurídica/NITE same-shape, in `tests/FundingPlatform.Tests.Unit/Domain/IdentificationTests.cs`. (dep: T003)
- [X] T005 [P] Add `[IdentificationType] TINYINT NULL` to `src/FundingPlatform.Database/Tables/dbo.Applicants.sql` and `src/FundingPlatform.Database/Tables/dbo.Suppliers.sql`.
- [X] T006 Extend domain entities: `Applicant` (+`IdentificationType?`, `SetIdentification(type, rawValue)`, ctor + `UpdateProfile` accept the type/VO) and `Supplier` (+`IdentificationType?`, `CreateDraft` accepts the type, `NormalizeLegalId` → canonical reformat strip→`1-3-6` for 10 digits) in `src/FundingPlatform.Domain/Entities/Applicant.cs` + `Supplier.cs`. (dep: T002, T003)
- [X] T007 [P] Map `IdentificationType` (nullable byte enum) in `src/FundingPlatform.Infrastructure/Persistence/Configurations/ApplicantConfiguration.cs` + `SupplierConfiguration.cs`. (dep: T002, T005, T006)
- [X] T008 [P] Rewrite `src/FundingPlatform.Web/wwwroot/js/input-masks.js` into a `MASKS` registry (entries: `mode`, `maxLength`, `format`, `validate`) per `contracts/mask-registry.md`; event-delegated `input`/`blur` on `document`; `MutationObserver` to format server-rendered/injected values once; identification type-selector controller (`data-mask-controller` ↔ `data-mask-group`, options carry `data-mask-for`); masks `email`, `phone-cr`, `cedula`, `cedula-jur`, `dimex`, `nite`, `pasaporte`.
- [X] T009 [P] Create `IdentificationFormatAttribute` taking the **sibling type-property name** as a ctor arg (e.g. `[IdentificationFormat(nameof(SupplierIdentificationType))]`, default `"IdentificationType"`), resolving that property via `ValidationContext`, delegating to `Identification.IsValid(type, value)`, es-CR message "La identificación no tiene el formato de {tipo}." — in `src/FundingPlatform.Web/Validation/IdentificationFormatAttribute.cs`. (dep: T003)
- [X] T010 [P] Create shared `_LegalIdField.cshtml` partial (labeled type `<select data-mask-controller>` with allowed-types param + masked `<input data-mask-group>`, asp-for names passed in) in `src/FundingPlatform.Web/Views/Shared/_LegalIdField.cshtml`. (dep: T008)

**Checkpoint**: domain rule, schema, mask engine, validation attr + shared partial ready.

---

## Phase 3: User Story 1 - Type-aware identification for people (Priority: P1) 🎯 MVP

**Goal**: Person identification (Register / admin user create+edit) is type-aware, masked, persisted, round-tripped, server-validated; Profile shows it read-only.

**Independent Test**: Register choosing each type → field masks; submit valid; admin-edit shows saved type + masked value; malformed submit (client bypassed) → server rejects.

- [X] T011 [P] [US1] `RegisterViewModel`: add `IdentificationType` (Required, es-CR) + `[IdentificationFormat]` on `LegalId` in `src/FundingPlatform.Web/ViewModels/RegisterViewModel.cs`. (dep: T009)
- [X] T012 [P] [US1] `AdminUserCreateViewModel` + `AdminUserEditViewModel`: add `IdentificationType?` + `[IdentificationFormat]` on `LegalId` (required only when Role=Applicant) in `src/FundingPlatform.Web/ViewModels/Admin/AdminUserCreateViewModel.cs` + `AdminUserEditViewModel.cs`. (dep: T009)
- [X] T013 [P] [US1] `ProfileViewModel`: add read-only `IdentificationType?` + `LegalId` (init-only, server-rebuilt) in `src/FundingPlatform.Web/ViewModels/ProfileViewModel.cs`.
- [X] T014 [US1] `Account/Register.cshtml`: replace plain `LegalId` input with `_LegalIdField` (person types) + `@section Scripts` loading `input-masks.js` in `src/FundingPlatform.Web/Views/Account/Register.cshtml`. (dep: T010, T011)
- [X] T015 [US1] `Admin/Users/Create.cshtml` + `Edit.cshtml`: render `_LegalIdField` inside the existing `#legalIdField` block (so role-visibility JS still hides the whole block when Role≠Applicant); load `input-masks.js` in `src/FundingPlatform.Web/Views/Admin/Users/Create.cshtml` + `Edit.cshtml`. (dep: T010, T012)
- [X] T016 [US1] `Account/Profile.cshtml`: add read-only identification type + masked value rows with the "administrado" badge (pattern of the Email row) in `src/FundingPlatform.Web/Views/Account/Profile.cshtml`. (dep: T013)
- [X] T017 [US1] `AccountController.Register` POST: construct `Applicant` via `SetIdentification(model.IdentificationType, model.LegalId)` (VO normalizes/validates) in `src/FundingPlatform.Web/Controllers/AccountController.cs`. (dep: T006, T011)
- [X] T018 [US1] Admin user create/edit: thread `IdentificationType` through `CreateUserRequest` / update request and `IUserAdministrationService`, set the `Applicant` via the VO, and add the es-CR presence check ("Seleccione el tipo de identificación." / "La identificación es obligatoria.") when Role=Applicant, in `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs` + `src/FundingPlatform.Application/Users/*`. (dep: T006, T012)
- [X] T019 [US1] `AccountController.Profile` GET (`BuildProfileViewModelAsync`): populate read-only `IdentificationType` + canonical `LegalId` from the applicant row in `src/FundingPlatform.Web/Controllers/AccountController.cs`. (dep: T013)
- [X] T020 [US1] Seeds: change demo applicants to valid distinct cédulas (`1-0001-0001/-0002/-0003`) + `IdentificationType.CedulaFisica` in `src/FundingPlatform.Infrastructure/Identity/IdentityConfiguration.cs`. (dep: T006)
- [X] T021 [P] [US1] Integration test: applicant `IdentificationType` + canonical `LegalId` persist and round-trip via the real DB in `tests/FundingPlatform.Tests.Integration/`. (dep: T006, T007)
- [X] T022 [US1] E2E page objects + fixture: add type-selector handling to `RegisterPage`, `Admin/AdminUserCreatePage`, `Admin/AdminUserEditPage`, and update `AuthenticatedTestBase.RegisterUserAsync` to select a type + use a valid cédula (via T001 helper) in `tests/FundingPlatform.Tests.E2E/PageObjects/*` + `Fixtures/AuthenticatedTestBase.cs`. (dep: T014, T015, T001)
- [X] T023 [US1] E2E tests: update `AuthenticationTests` + `Admin/AdminUserLifecycleTests` to valid values + type, and add `Tests/InputMaskIdentificationTests`: each type formats as typed, letters rejected on numeric types, admin-edit round-trip shows saved type+value, malformed value (client validation removed) rejected server-side with es-CR error, in `tests/FundingPlatform.Tests.E2E/Tests/*`. (dep: T022)

**Checkpoint**: US1 independently testable — MVP deliverable.

---

## Phase 4: User Story 2 - Supplier identification + tolerant lookup (Priority: P2)

**Goal**: Supplier identification is type-aware (jurídica/NITE) + masked; lookup matches regardless of typed hyphenation.

**Independent Test**: Supplier add → choose jurídica/NITE → field masks; type a known ID with and without hyphens → same lookup hit; new NITE supplier persists + round-trips.

- [X] T024 [P] [US2] `AddSupplierViewModel`: add `SupplierIdentificationType` (jurídica/NITE) + `[IdentificationFormat(nameof(SupplierIdentificationType))]` on `SupplierLegalId` (pass the sibling type-property name explicitly, since it differs from the default `IdentificationType`) in `src/FundingPlatform.Web/ViewModels/AddSupplierViewModel.cs`. (dep: T009)
- [X] T025 [US2] `Supplier/Add.cshtml`: add the supplier type selector (jurídica/NITE) + `data-mask` on `#supplier-legal-id-input` (driven by the selector), keep the 250 ms debounce lookup, load `input-masks.js` in `src/FundingPlatform.Web/Views/Supplier/Add.cshtml`. (dep: T010, T024)
- [X] T026 [US2] `SupplierController.Add` POST: pass `SupplierIdentificationType` to `Supplier.CreateDraft`; validate identification via the VO (translate the domain throw to a ModelState es-CR error in the existing try/catch) in `src/FundingPlatform.Web/Controllers/SupplierController.cs`. (dep: T006, T024)
- [X] T027 [P] [US2] Integration test: store a supplier as `3-101-123456`, then `SearchByLegalIdAsync("3101123456")` / `"3-101-123456"` / `"3 101 123456"` all return the same Hit (normalization), in `tests/FundingPlatform.Tests.Integration/`. (dep: T006)
- [X] T028 [US2] E2E: add the type selector to `SupplierPage`; update `SupplierQuotationTests` to valid jurídica + type; add `Tests/SupplierIdentificationLookupTests`: hyphenated vs bare-digit query → same supplier hit; new NITE supplier persists + round-trips, in `tests/FundingPlatform.Tests.E2E/*`. (dep: T025, T022)

**Checkpoint**: US2 independently testable.

---

## Phase 5: User Story 3 - Consistent + extensible masking (Priority: P3)

**Goal**: Email + CR phone masked on every form that renders them; registry extensibility demonstrated.

**Independent Test**: On every form with email/phone, invalid email flags on blur; phone formats to `8888-8888`.

- [X] T029 [P] [US3] Add `data-mask="email"` / `data-mask="phone-cr"` to all email/phone inputs and ensure `input-masks.js` loads on: `Account/Profile.cshtml` (phone), `Admin/Users/Create.cshtml` + `Edit.cshtml` (email+phone), `Supplier/_BranchPicker.cshtml` + `_LookupEmpty.cshtml` (email+phone — masked via delegation from `Supplier/Add.cshtml`), and the admin branch-edit `Admin/Suppliers/Detail.cshtml` if it renders email/phone, in `src/FundingPlatform.Web/Views/*`. (dep: T008)
- [X] T030 [US3] E2E: assert email blur → "Ingrese un correo electrónico válido." and phone formats to `8888-8888` on representative forms; verify every form rendering email/phone has the mask active, in `tests/FundingPlatform.Tests.E2E/Tests/InputMaskEmailPhoneTests.cs`. (dep: T029)

**Checkpoint**: US3 independently testable.

---

## Phase 6: Polish & Cross-Cutting

- [X] T031 [P] Accessibility: confirm `aria-invalid` toggles on invalid masked inputs and every type selector + masked input is labeled, across all touched views (`src/FundingPlatform.Web/Views/*`, `wwwroot/js/input-masks.js`).
- [ ] T032 Run the FULL Playwright E2E suite, confirm green, fix any fallout (delivery bar SC-008) — `tests/FundingPlatform.Tests.E2E`.
- [X] T033 [P] Final data sweep: any remaining fixtures/seeds/sample legal IDs moved to canonical form; remove dead/orphaned mask code; verify no `data-mask` typos against the registry — repo-wide.

---

## Dependencies

- **Setup (T001)** → independent, can start anytime (E2E helper).
- **Foundational (T002–T010)** blocks all story work. Internal order: T002 → T003 → {T004, T006, T009}; T005 → T007 (also needs T006); T008 → T010; T006 needs T002+T003; T007 needs T002+T005+T006.
- **US1 (T011–T023)**: needs Foundational. T017/T018 need T006; views need T010. T021 needs T006+T007. E2E (T022, T023) last in story.
- **US2 (T024–T028)**: needs Foundational; T028 reuses the E2E selector pattern from T022.
- **US3 (T029–T030)**: needs T008 (registry). Independent of US1/US2 except shared views may already load the script.
- **Polish (T031–T033)**: after US1–US3. T032 is the gate.

**Story independence**: US1, US2, US3 each deliver standalone value and are independently testable once Foundational is done.

## Parallel Execution Examples

- **Foundational kickoff**: T002, T005, T008 in parallel (enum / dacpac / JS — different files). Then T003, then T004 ∥ T006 ∥ T009; T010 after T008.
- **US1 viewmodels**: T011 ∥ T012 ∥ T013 (different files), then their views/controllers.
- **Cross-story**: after Foundational, US1 / US2 / US3 implementation can proceed on parallel tracks (distinct files), converging at T032.

## Implementation Strategy

- **MVP = US1** (person type-aware identification). Ship + validate independently at its checkpoint.
- Then US2 (supplier + tolerant lookup), then US3 (email/phone everywhere + extensibility).
- Commit at each checkpoint (constitution Commit Discipline). T032 full-suite green is the delivery bar before PR.
