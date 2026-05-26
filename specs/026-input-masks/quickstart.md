# Quickstart: Structured-Field Input Masks

## Run

```bash
dotnet run --project src/FundingPlatform.AppHost
```

The dacpac auto-deploys (outside ephemeral mode), adding `Applicants.IdentificationType` + `Suppliers.IdentificationType`. Seeded demo applicants now have valid cédulas (`1-0001-0001/-0002/-0003`).

## Add a new mask (the extensibility contract, SC-006)

1. Add one entry to `MASKS` in `wwwroot/js/input-masks.js`:
   ```js
   MASKS['postal-cr'] = { mode: 'strict', maxLength: 5, format: digitsOnly(5), validate: v => /^\d{5}$/.test(v) };
   ```
2. Tag the input: `<input data-mask="postal-cr" ... >`.

No other JS change. (If it's type-switchable, add a `data-mask-for="postal-cr"` option to the controller `<select>`.)

## Manual verification (maps to acceptance scenarios)

**US1 — person identification**
1. Go to Register. The "Tipo de identificación" selector defaults to Cédula física.
2. Type `123456789` in the identification field → it shows `1-2345-6789`. Type letters → rejected.
3. Switch the selector to Pasaporte → type `A1B2C3` → accepted as-is, no hyphens.
4. Switch to DIMEX → `123456789012` (12 digits) accepted.
5. Submit a valid applicant. Sign in as admin → Users → edit that user → selector shows the saved type and the masked value (round-trip).
6. With JS disabled (or via a crafted POST), submit a malformed value for the selected type → server rejects with a Spanish field error; the value is preserved.

**US2 — supplier identification + tolerant lookup**
1. Create an application + item; Add supplier. Selector offers Cédula jurídica / NITE.
2. Choose Cédula jurídica, type a known supplier ID **with** hyphens → lookup hit. Clear, type the same digits **without** hyphens → same hit.
3. New supplier: choose NITE, enter a valid value → saved; reopen → masked value + NITE shown.

**US3 — consistency + extensibility**
1. On Register / admin user create+edit / supplier add: email blurs invalid → Spanish feedback; phone formats to `8888-8888`.
2. Confirm the cédula masks (added via single registry entries) work — the extensibility demonstration.

## Tests

```bash
dotnet test tests/FundingPlatform.Tests.Unit          # Identification VO: per-type valid/invalid/canonicalize
dotnet test tests/FundingPlatform.Tests.Integration   # type+value round-trip; supplier lookup normalization
dotnet test tests/FundingPlatform.Tests.E2E           # full suite (delivery bar, SC-008)
```

Existing tests + `AuthenticatedTestBase.RegisterUserAsync` now use valid canonical identification values and select a type; page objects (`RegisterPage`, `SupplierPage`, `AdminUserCreatePage`, `AdminUserEditPage`) expose the selector.
