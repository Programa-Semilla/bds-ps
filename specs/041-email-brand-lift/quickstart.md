# Quickstart / Verification: ALIA Transactional Email Brand UI-Lift

**Feature**: 041-email-brand-lift · **Date**: 2026-06-19

How to run and verify the redesign. Primary gate is mail-capture (smtp4dev) inspection + filtered E2E (Constitution III, CLAUDE.md delivery bar).

## Run locally
```bash
dotnet run --project src/FundingPlatform.AppHost
```
- The Aspire `smtp4dev` sidecar captures all outbound mail; open its HTTP UI to eyeball rendered emails (brand shell, logo, partner strip, teal CTA).
- Seed accounts (all `Demo123!` / sentinel): `applicant@`, `reviewer@`, `auditor@`, `admin@…programa-semilla.test` — all on the non-prod allowlist.

## Manual verification (per success criterion)
1. **SC-001 brand rendering**: trigger a submission, an approval, an appeal message, a stage reminder, an invitation. In smtp4dev confirm each shows: Programa Semilla logo header, teal palette, partner-strip footer, "Equipo Programa Semilla" sign-off, support phone `+506 4600-1234`.
2. **SC-002 zero variable loss**: diff each redesigned email's dynamic values (name, application #, status, links, stage data) against the pre-change template — none missing.
3. **SC-003 no invented URLs / CTA rule**: an email with a link (approval) shows a teal button + fallback link; an email without (password-changed) shows neither.
4. **SC-004 images blocked**: disable image loading in the smtp4dev preview / a real client; every email stays legible; alt text shows.
5. **SC-005 consistency**: palette + partner strip identical across all emails.
6. **SC-007 text twins**: every HTML email has a present, in-sync `.text.cshtml` (check the text part in smtp4dev).
7. **SC-008 new emails once**:
   - *Under-review*: submit as applicant → open as reviewer (triggers `StartReview`) → exactly one "Tu solicitud está en revisión" to the applicant; re-open as reviewer → no duplicate.
   - *Password-changed*: complete a password reset / change → exactly one "Tu contraseña fue actualizada" to that user.
   - *Company-for-review*: render-test the stub template (no live send).

## Automated tests
```bash
# Unit — binding totality, storage-string round-trip, copy/brand guards
dotnet test tests/FundingPlatform.Tests.Unit --filter FullyQualifiedName~Notifications

# Integration (real DB) — new-event recipient matrix + idempotency
dotnet test tests/FundingPlatform.Tests.Integration --filter FullyQualifiedName~Notifications

# E2E (mail-capture) — filter to the changed/added classes
dotnet test tests/FundingPlatform.Tests.E2E --filter FullyQualifiedName~Notifications
```
Filtered E2E (the changed/added email classes) must be green before the feature is considered delivered. A full E2E run is reserved for cross-cutting concerns or explicit request.

## Definition of done (maps to spec SC-001..008)
- [ ] All existing HTML emails + the 3 new ones render in the brand shell (SC-001/005).
- [ ] No dynamic variable lost; no invented URL (SC-002/003).
- [ ] Legible with images blocked (SC-004).
- [ ] Every HTML email has a synced text twin (SC-007).
- [ ] Under-review + password-changed each send exactly once per action; company-for-review template render-tested (SC-008/006).
- [ ] No schema change, no new dependency, no build step.
