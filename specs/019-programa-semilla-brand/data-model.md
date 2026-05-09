# Data Model — Spec 019 Programa Semilla Brand Pivot

**Branch**: `019-programa-semilla-brand` · **Date**: 2026-05-09

> **Schema delta: NONE.** Per FR-038 / SC-013, this spec adds zero rows / columns / indexes / constraints / migrations to `src/FundingPlatform.Database/`. After this spec lands, `git diff main -- src/FundingPlatform.Database/` MUST be empty.

## Domain entities

This spec does not add, rename, remove, or alter any domain entity, value object, aggregate root, repository interface, or domain event. The full delta is presentational (CSS tokens, partials, view files, vendored brand assets, email templates).

## Application-layer DTOs

This spec does not add or alter any `Application/DTOs/*` type. Existing projections (e.g., `IApplicantDashboardProjection`, `IAdminDashboardProjection`) are reused as-is. View models reference the same projections; only the Razor templates that consume them are retuned.

## Infrastructure-layer changes

None at the data tier. The Funding Agreement projection (`FundingAgreementService`) and the Syncfusion HTML→PDF renderer are explicitly untouched (FR-039).

## Email-template data shape

Email templates accept the same model fields they accept today; only the literal text strings (sender display name, signature block) change. No new model property is added.

## Configuration knobs

No new `appsettings.json` keys, no removed keys, no renamed keys. Existing keys remain `FundingPlatform`-prefixed (spec 012 invariant carries forward). The only environment-side update is the `BuildInfo.g.cs` MSBuild-generated file used to cache-bust `tokens.css` (research R12) — this is a build-time artifact, not a configuration knob.

## Sentinel summary

| Layer | Delta |
|---|---|
| Domain entities | none |
| Domain events | none |
| Repository interfaces | none |
| Application services / projections | none |
| Application DTOs | none |
| Infrastructure persistence configurations | none |
| EF Core mappings | none |
| `dbo.*.sql` files | none |
| Migration scripts | N/A (dacpac) |
| Configuration keys | none |
| Email-template model shapes | none |
