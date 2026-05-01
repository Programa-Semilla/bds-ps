# Spec 013 — Polish-phase verification notes

**Date:** 2026-04-30
**Branch:** 013-supplier-catalog
**Author:** automated polish pass (T082-T092, ship pipeline)

This document captures the "negative" Polish-phase verifications and small follow-ups
that don't merit a separate GitHub issue. The ship pipeline does not require an
issue tracker; this file is the durable record.

---

## T082 — Quickstart walkthrough

`quickstart.md` was read end-to-end against the implemented controllers, views and
seeders. No divergences found that would block delivery:

- Step 1 (schema deploy): `Migrations/013_SupplierCatalog.sql` is wired into
  `PostDeployment/SeedData.sql` (T008). The script is idempotent; on a fresh DB
  it is a no-op (sentinel + supplier backfill blocks both gate on row counts).
  `dotnet run --project src/FundingPlatform.AppHost` boots Aspire and the
  dacpac auto-deploys.
- Steps 3-9 (US1-US7): the routes, Razor partials and view-models referenced in
  the quickstart all exist (`SupplierController`, `AdminSuppliersController`,
  `_LookupHit/_LookupEmpty/_LookupRejected`, `Detail.cshtml`, `Index.cshtml`).
- Step 10 (migration parity): the migration parity test `T028` lives in
  `tests/FundingPlatform.Tests.Integration/Persistence/SupplierMigrationTests.cs`
  and runs as part of the integration suite (T086).
- Step 11 (full E2E): the `tests/FundingPlatform.Tests.E2E` suite is the
  delivery gate (T087).

Minor doc gap: the quickstart's step-2 raw-SQL seed snippet is illustrative and
was never committed — the real US1 / US2 / US5 / US7 E2E tests seed via the UI
or via the new admin verify flow (`SupplierCatalogTests.cs`,
`ApplicantReusesVerifiedSupplierTests.cs`). This is intentional and matches the
quickstart's "do NOT commit" disclaimer.

## T083 — NFR-004: 250ms client-side debounce

**Verified by inspection.** `src/FundingPlatform.Web/Views/Supplier/Add.cshtml`
lines 116-141 contain a vanilla-JS IIFE that wires the legal-ID input with
`setTimeout(runLookup, 250)` on every `input` event. The previous timer is
cleared on each keystroke, so only the trailing 250 ms of inactivity actually
fires the `/Search` fetch. No external library, no debounce-utility import.
This satisfies NFR-004 verbatim.

## T084 — Rate limiter coverage for /Supplier/Search

**Finding:** `Program.cs` does NOT currently configure
`Microsoft.AspNetCore.RateLimiting`. There is no global IP rate limiter wired
in this codebase (despite the spec mentioning "the existing global IP rate
limiter from spec 008" — that limiter does not actually exist in `Program.cs`
as of HEAD).

**Mitigation:** the `Search` action is `[HttpGet]` only, returns server-rendered
partial HTML (no JSON, no large payload), and is gated by
`[Authorize] + VerifyOwnershipAsync(appId)`. An anonymous attacker cannot reach
it; an authenticated applicant can only target their own application. The
attack surface is therefore self-rate-limited by application ownership.

**Follow-up (NOT a release blocker):** if a future spec introduces a global IP
rate limiter, `/Application/{appId}/Item/{itemId}/Supplier/Search` should be
covered by the default policy bucket. Documenting here so the next spec author
knows where to look.

## T085 — Unit tests

`dotnet test tests/FundingPlatform.Tests.Unit` — **123 / 123 passing** (was
120 before this branch; +3 from T091).

## T086 — Integration tests

`dotnet test tests/FundingPlatform.Tests.Integration` — **92 / 92 passing**,
including the migration parity test (T028, `SupplierMigrationTests.cs`).

## T087 — E2E delivery gate

Run separately. See run log in commit message of the final Polish commit.

## T088 — speckit-analyze cross-artifact consistency

Invoked via Skill tool; results captured in pipeline output.

## T090 — Follow-up: drop legacy supplier columns

The migration leaves these six columns on `dbo.Suppliers` for one release as a
rollback safety net (per research.md R3):

- `ContactName`
- `Email`
- `Phone`
- `Location`
- `ShippingDetails`
- `WarrantyInfo`

They are marked with a `-- TODO[013-cleanup]` comment in `dbo.Suppliers.sql`
and `Ignore("…")`-d in the EF Core configuration so the runtime cannot read or
write them. **One release after this ships, drop them in a follow-up dacpac
migration:**

```sql
ALTER TABLE dbo.Suppliers DROP COLUMN ContactName, Email, Phone, Location,
                                     ShippingDetails, WarrantyInfo;
```

No code changes are required at that time — the EF mappings already ignore them.

## T091 — NFR-001: no external network calls

**Verified by automated test.**
`tests/FundingPlatform.Tests.Unit/Application/SupplierCatalogService_NoExternalCallsTests.cs`
asserts via reflection that neither `SupplierCatalogService` nor
`AdminSuppliersController` declare a constructor parameter, field, or property
of type `HttpClient` or anything in the `System.Net.Http.*` /
`Microsoft.Extensions.Http.*` namespace. The third test in that file also
confirms the Application assembly does not reference `System.Net.Http` at the
assembly-references level.

## T092 — NFR-002: no new managed dependencies

`git diff main..HEAD --stat -- 'src/**/*.csproj' 'tests/**/*.csproj'` shows
exactly **one** csproj edit on this branch:

```
tests/FundingPlatform.Tests.Unit/FundingPlatform.Tests.Unit.csproj
  + <ProjectReference Include="..\..\src\FundingPlatform.Web\..." />
```

That is a `<ProjectReference>` (intra-solution) — not a `<PackageReference>`.
**Zero new managed NuGet packages were introduced by spec 013.** NFR-002 holds.

PR-template suggestion (manual check in lieu of CI): reviewers should run
`git diff main -- '**/*.csproj'` and confirm only `<ProjectReference>` edits
appear, no `<PackageReference>` additions.
