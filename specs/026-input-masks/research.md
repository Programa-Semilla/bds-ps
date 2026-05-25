# Research: Structured-Field Input Masks

Phase 0 decisions. All NEEDS CLARIFICATION resolved during brainstorm + planning exploration.

## D1: Masking mechanism — extend hand-rolled vs vendor a library

- **Decision**: Extend `wwwroot/js/input-masks.js` into a data-driven registry. No new dependency.
- **Rationale**: CLAUDE.md mandates vendored-only / no-CDN; new managed/vendored deps need spec approval. The existing `data-mask` attribute pattern is already established (spec 021). The need (≤7 masks, simple strip/format/validate) does not justify a library.
- **Alternatives**: IMask / Cleave.js (rejected — new vendored dep, heavier than need).

## D2: Registry shape + dynamic nodes

- **Decision**: Registry object keyed by mask name; each entry = `{ format(raw)→string | null, maxLength, validate(value)→bool, mode: 'strict'|'soft' }`. Use **event delegation** on `document` for `input`/`blur` (match `[data-mask]`) so AJAX-injected supplier partials (`_LookupEmpty`, `_BranchPicker`, injected via `innerHTML` by the 250 ms lookup) are masked without re-init. A `MutationObserver` (or a re-scan hook the lookup calls) formats server-rendered values inside newly-added nodes once.
- **Rationale**: The supplier lookup replaces `#lookup-result-region` innerHTML; the current scan-once-on-DOMContentLoaded approach would miss those inputs. `location-cascade.js` already solved the same problem with event delegation — mirror it. Delegation also makes "add a field = tag it" true with zero wiring.
- **Alternatives**: Re-run `applyMasks()` after every fetch (rejected — brittle, must remember at each call site). Scan-once (rejected — misses dynamic nodes).

## D3: Domain placement of the type↔shape invariant

- **Decision**: `IdentificationType` enum + `Identification` value object in `FundingPlatform.Domain`. The VO validates the value against the type's regex and canonicalizes it in its constructor (per-type `[GeneratedRegex]`), mirroring `CurrencyCode`/`PublicCode`. `Applicant`/`Supplier` get a `SetIdentification(IdentificationType, string rawValue)` method that builds the VO and assigns the two persisted columns (`LegalId` canonical string + `IdentificationType`). ViewModel `[IdentificationFormat]` attribute + the JS mask delegate to / echo the same rule.
- **Rationale**: Constitution Principle II (rich domain model) — the invariant must live in the entity/VO, not controllers. Two columns (not a single owned VO via `.HasConversion`) preserve the existing `UX_*_LegalId` unique index and the string-based supplier lookup.
- **Alternatives**: Validation only in a ViewModel attribute (rejected — anemic, scatters the rule). Full owned-VO column mapping (rejected — complicates the unique index + lookup for no gain).

## D4: Canonical stored form

- **Decision**: Hyphenated canonical string per type — cédula física `1-2345-6789` (`^\d-\d{4}-\d{4}$`), cédula jurídica / NITE `3-101-123456` (`^\d-\d{3}-\d{6}$`), DIMEX plain digits `^\d{11,12}$`, passport uppercased alnum `^[A-Z0-9]{1,20}$`. Phone unchanged `^\d{4}-\d{4}$`.
- **Rationale**: Matches the existing phone storage convention (hyphenated, human-readable in DB/reports). DIMEX has no standard CR hyphenation → plain digits. Stakeholder chose hyphenated.
- **Alternatives**: Digits-only canonical (rejected — diverges from phone precedent; the hyphenated form is just as good a key once normalization is deterministic).

## D5: Supplier lookup hyphenation tolerance

- **Decision**: Extend `Supplier.NormalizeLegalId(string)` to produce the canonical form (strip non-alphanumerics, uppercase; if 10 digits → group `1-3-6`). The repository already calls it before `s.LegalId == canonical`, and `Supplier.CreateDraft` already normalizes on write → stored and queried values converge. Signature unchanged.
- **Rationale**: Single existing insertion point (`SupplierRepository.GetByLegalIdWithBranchesAsync` line 25). Cédula jurídica and NITE share the 10-digit `1-3-6` shape, so the normalizer is type-agnostic for suppliers — no need to thread the type into the query.
- **Alternatives**: Normalize to digits-only for comparison while storing hyphenated (rejected — the column + unique index would then disagree with the compared form; deterministic hyphenated canonicalization is simpler).

## D6: Person identification column location

- **Decision**: `dbo.Applicants.IdentificationType` (TINYINT NULL), not `AspNetUsers`. `ApplicationUser` carries no legal ID; `Applicant` does.
- **Rationale**: Exploration confirmed `Applicant.LegalId` is the person legal ID (unique index `UX_Applicants_LegalId`); `ApplicationUser` has none. Admin user create/edit flows already create the `Applicant` with `LegalId`.
- **Alternatives**: A column on `AspNetUsers` (rejected — wrong entity; would duplicate/split identity).

## D7: Profile surface

- **Decision**: Read-only display of identification type + masked value on Profile, with the "administrado" badge, consistent with Email/Role. Not self-editable.
- **Rationale**: Profile identity is admin-managed today (`UpdateProfileCommand` = name/phone/address only; email read-only). Making cédula self-editable is new scope + a uniqueness/integrity risk. Stakeholder confirmed read-only.
- **Alternatives**: Editable on Profile (rejected — scope + risk). Drop Profile (rejected — stakeholder wants identification visible there).

## D8: Enum persistence type

- **Decision**: `IdentificationType : byte`, stored `TINYINT NULL`, mapped via EF default enum conversion in `ApplicantConfiguration` / `SupplierConfiguration`.
- **Rationale**: Mirrors the existing `SupplierVerificationStatus : byte` TINYINT pattern. Nullable because admin user LegalId is optional and existing rows pre-assignment.
- **Alternatives**: NVARCHAR enum names (rejected — inconsistent with the established TINYINT enum pattern).

## D9: Server-side aggregation of identification errors

- **Decision**: A reusable `IdentificationFormatAttribute` (Web/Validation) resolves the sibling `IdentificationType` property and delegates to the domain validator (`Identification.IsValid(type, value)`), producing a field-level es-CR error that aggregates with all other DataAnnotation errors (Quality Gate: all errors at once). Controllers additionally enforce "type required when LegalId required" (e.g. Applicant role) the same way the existing LegalId-required check does.
- **Rationale**: Honors the existing "collect all validation errors" gate and keeps the rule in the domain. Matches the established `ModelState.AddModelError` aggregation already used in `AccountController`/`AdminUsersController`/`SupplierController`.
- **Alternatives**: Throw from the domain VO and catch in the controller (works, mirrors `CurrencyCode.From` in `SupplierController.Add`, but yields one-error-at-a-time unless wrapped — kept as the fallback for the supplier POST path where a try/catch already exists).

## D10: Seed + E2E data

- **Decision**: Update the C# Identity seeder (`IdentityConfiguration.cs`) demo applicants to valid distinct cédulas (`1-0001-0001`, `1-0001-0002`, `1-0001-0003`) + `IdentificationType.CedulaFisica`. Update `AuthenticatedTestBase.RegisterUserAsync` + `RegisterPage`/`SupplierPage`/`AdminUserCreatePage`/`AdminUserEditPage` to select a type and use valid canonical values (generate valid-shaped unique cédulas/jurídicas from the per-test GUID digits). No backfill (pre-production, FR-020).
- **Rationale**: Existing tests use `LID-{hex}`, `SUP-{hex}`, `DEMO-APP-001` — all invalid under the new strict masks; they must move to valid values (in-scope per the UI-quality-over-selector-stability posture). No seeded supplier catalog exists, so only the live-created supplier values in tests change.
- **Alternatives**: Relax masks to accept legacy formats (rejected — defeats the feature).
