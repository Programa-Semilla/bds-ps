# Code Review: Batch user creation (034)

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Date:** 2026-06-12
**Reviewer:** Claude (speckit.spex-gates.review-code)

## Compliance Summary

**Overall Score: 100%** (14/14 functional requirements; 6/6 success criteria; all 8 edge cases handled)

- Upload & file-level validation (FR-001–003): 3/3
- Row mapping & normalization (FR-004–006): 3/3
- Row-level validation (FR-007–009): 3/3
- Creation & invitation (FR-010–011): 2/2
- Report (FR-012): 1/1
- Conventions (FR-013–014): 2/2

Tests: Unit 18/0 (`CsvParserTests`, `PhoneNormalizerTests`), Integration 5/0 (`BatchUserCreationTests`), filtered E2E 5/0 (`BatchUserCreateTests`) — all personally executed and green.

No deviations from spec behavior. Two implementation-shape deviations from `tasks.md` (reason-string placement; US3 E2E uses a not-found chain failure rather than a two-chain mismatch) are recorded in [tasks.md → Deviations](tasks.md#deviations-implementation) and below.

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation, focusing on the
high-level questions that need human judgment.

**Changed files:** 7 new Application files (`src/FundingPlatform.Application/Admin/Users/Batch/`), 1 Infrastructure method (`UserAdministrationService`), 1 interface, 1 controller surface + 2 views + 1 view-model + resources (Web), 3 test files (Unit/Integration/E2E) + 1 E2E page object.

### Understanding the changes (8 min)

- Start with [contracts.md](contracts/contracts.md): the CSV header contract, the `CreateUsersBatchAsync` behavior, and the three controller routes. It is the shortest path to the whole feature.
- Then `src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs` (`CreateUsersBatchAsync`): the per-row pipeline — validate → dedupe → resolve chain → `CreateUserAsync`. This is the heart of the feature.
- Then `src/FundingPlatform.Web/Controllers/Admin/AdminUsersController.cs` (`Batch` GET/POST + `BatchTemplate`): file intake, FR-003 file-level rejection, and the invitation pass.
- Question: the pure parsing/normalization lives in Application and the DB-touching orchestration in Infrastructure (mirroring spec 032/033). Does that split read cleanly, or would you expect the CSV→rows mapping to also live in Application rather than the controller?

### Key decisions that need your eyes (12 min)

**Per-row reason strings live in Application, not Web.Resources** (`src/FundingPlatform.Application/Admin/Users/Batch/BatchUserRowReasons.cs`, relates to [FR-013](spec.md))

`tasks.md` T002 put *all* es-CR strings in `AdminUsersResources` (Web), but the Infrastructure service produces the per-row reasons and Infrastructure cannot reference Web (dependencies point inward). So row reasons moved to Application; file-level/page-chrome strings stayed in Web.
- Question: is Application the right home for these es-CR strings, or would you prefer a different seam (e.g. reason *codes* returned by the service, translated in Web)?

**In-file duplicate "first wins" claims the value before chain/create** (`UserAdministrationService.cs`, `CreateUsersBatchAsync`, relates to [FR-008](spec.md#fr-008))

A row that passes field validation registers its email/cédula/código into the "seen" sets *before* chain resolution and creation. So if that first occurrence later fails the chain or `CreateUserAsync`, a subsequent identical row is still reported as an in-file duplicate (the value is "claimed").
- Question: is "first valid-shaped occurrence claims the value" the right semantics, or should only a *successfully created* row claim it (letting a later duplicate retry if the first failed downstream)?

**Name resolution is case-insensitive, accent-sensitive, preloaded in memory** (`UserAdministrationService.cs`, `BuildNameLookup`, relates to [FR-009](spec.md#fr-009))

All Funds/Processes/Groups are loaded once and matched by trimmed name with `OrdinalIgnoreCase`. SQL Server's `Groups.Name` is collation CI+AI (accent-insensitive) while Funds/Processes are CI; the in-memory match is accent-*sensitive*. Seed names carry no accents so resolution is identical, but a real accented Group name typed without accents would resolve in SQL yet not here.
- Question: acceptable for v1 (research [D6](research.md))? Or should the match strip accents (the header matcher already has `NormalizeKey` that does exactly this)?

**FR-009 "ambiguous" collapses to "not found"** (relates to [spec.md US3 scenario 3](spec.md))

Funds/Processes/Groups names are globally unique (unique indexes), so a name resolves to 0 or 1 row — "ambiguous (matches more than one)" cannot occur and is reported as not-found. Documented in research [D6](research.md#d6--groupprocessfund-name-resolution-is-deterministic-fr-009).
- Question: agree that the uniqueness guarantee makes a dedicated "ambiguous" message dead code?

**Template stream + parser both carry a UTF-8 BOM** (`AdminUsersController.BatchTemplate`, `CsvParser`)

The downloaded template is written with a leading BOM (Excel "CSV UTF-8"); the parser strips a leading BOM and `BatchUserCsvColumns.NormalizeKey` strips it from the first header cell. Round-trips cleanly.
- Question: any concern with emitting the BOM by default for operators who open the template in non-Excel tools?

### Areas where I'm less certain (5 min)

- `UserAdministrationService.cs` (`catch (Exception) → CreateFailed`): a per-row `CreateUserAsync` failure is swallowed so one bad row can't abort the batch. On a real SQL `DbUpdateException` the `DbContext` could be left in a faulted state for subsequent rows. EF InMemory doesn't reproduce this, so it's untested under SQL Server. Is the swallow-and-continue acceptable, or should each row create use a fresh scope/context?
- `AdminUsersController.Batch` reads the whole file into a string via `StreamReader` (UTF-8 assumed). A non-UTF-8 export (e.g. Windows-1252 with accented names) could mojibake before the row-level validation sees it. The 1 MiB cap guards memory but not encoding. Spec says CSV only ([D3](research.md#d3--file-level-vs-row-level-validation-boundary)); is UTF-8-only intake an acceptable v1 constraint?
- Phone normalizer splits on `/ , ; |` only (not whitespace), because the test case `"506 8888 1111"` must yield `"88881111"`. Research [D4](research.md#d4--phone-normalization-algorithm-fr-005) mentions "runs of whitespace between digit groups" as a separator, which would contradict that case. I followed the test cases. Is the no-whitespace-split interpretation correct?

### Deviations and risks (5 min)

- **US3 E2E** (`BatchUserCreateTests.ChainMismatch_RowSkipped`): a pure `ChainMismatch` (group under a *different existing* process/fund) needs two seeded chains, which the ephemeral E2E seed lacks. The E2E instead names a real Grupo with a non-existent Fondo (`FundNotFound` — still a chain-integrity rejection). The genuine two-chain `ChainMismatch` is covered by the integration test `WrongChain_RowSkipped`. Question: is integration coverage of the pure mismatch + E2E coverage of a not-found chain sufficient, or do you want a dev seam to seed a second chain for a true-mismatch E2E?
- **No status gate on Fund/Process** (research [D6](research.md)): v1 validates structural coherence only — a row naming an *archived* Fund or *closed* Process still creates, matching single-create. Question: acceptable, or should batch refuse memberships under archived/closed parents?
- No deviations from the layer boundaries, schema (none), or dependency rules in [plan.md](plan.md) were identified.
