# Phase 0 Research: Admin-only user provisioning + unique applicant User Code

**Feature**: 032-admin-user-code
**Date**: 2026-06-11

All open threads from the spec/brainstorm are resolved below. No `NEEDS CLARIFICATION` remain.

---

## D1. Where does `UserCode` live — Applicant entity vs. AspNetUsers

**Decision**: On the **`Applicant`** entity (table `dbo.Applicants`), beside `LegalId`.

**Rationale**:
- The four read-side surfaces other than the admin users list (reviewer queue + both report families + applicants CSV) **already** navigate `Applicant` and already match `Applicant.LegalId`. Adding `UserCode` there is a one-line `OR` per block with no new join.
- `Applicant` already denormalizes `Email`, so the reviewer-queue "add email" requirement (FR-013) is also just an `OR a.Applicant.Email`.
- The admin users list (`UserAdministrationService.ListUsersAsync`, over `_userManager.Users`) needs a correlated sub-query into `Applicants` to match `UserCode` — **but it needs that exact join anyway** to satisfy FR-012's "add LegalId". So both new match fields ride the same sub-query.
- It is applicant-scoped by nature: non-applicant users have no `Applicant` row, so the field simply doesn't exist for them — matching the "only asked for Solicitante" requirement at the data level.

**Alternatives considered**:
- *On `AspNetUsers` (like `CodigoPersonal`)*: rejected — would force a join from every Applicant-based read surface (reviewer queue, 3 reports) to `AspNetUsers`, inverting the cost. The admin list would be simpler but it's only one of four surfaces.
- *Reuse `CodigoPersonal`*: rejected by the user in brainstorming (new, separate field).

---

## D2. Uniqueness over a nullable column

**Decision**: Nullable `[UserCode] NVARCHAR(50) NULL` + a **filtered unique index** `WHERE [UserCode] IS NOT NULL`, **plus** an explicit service-layer pre-check mirroring the existing `LegalId` guard.

**Rationale**:
- The dacpac already uses filtered unique indexes in exactly this shape — `UX_Appeals_OneOpenPerApplication ... WHERE [Status] = 0`, `UX_SignedUploads_OnePending_PerAgreement ... WHERE [Status] = 0`, `UX_SupplierBranches_DefaultPerSupplier ... WHERE [IsDefault] = 1`. The `WHERE [UserCode] IS NOT NULL` filter lets any number of code-less applicants coexist (legacy + non-required edge) while enforcing uniqueness among assigned codes (FR-009, edge cases).
- Adding a **nullable** column to the populated `dbo.Applicants` table is migration-safe with no post-deploy backfill — unlike spec 029's `Fund` anchors (which needed `DEFAULT(0)` + `05_Fund029Anchors.sql` because they were `NOT NULL` + FK). So **no PostDeployment script is required** here.
- The service-layer `AnyAsync(a => a.UserCode == code)` pre-check (paralleling the `LEGAL_ID_IN_USE` guard at `UserAdministrationService.cs:212-218` / `:423-432`) gives a clean es-CR validation error on the common path **and works under EF InMemory**, so integration tests can cover the duplicate case directly. The filtered index is the concurrency backstop; a racing insert surfaces as `DbUpdateException` on `UX_Applicants_UserCode`, which the controller maps to the same es-CR message (the spec-030 `UX_Processes_Name` pattern).

**Testing consequence**: duplicate-via-service-precheck → Integration-testable; duplicate-via-index-race and the schema constraint itself → E2E-only (matches spec 030's recorded handling).

---

## D3. "Required for Solicitante" placement

**Decision**: Enforce required-ness at the **Web/controller boundary** (ModelState), mirroring the existing required-`LegalId`-for-Applicant rule at `AdminUsersController.Create` (`:177-180`) and `Edit`. The DB column stays nullable; the domain entity accepts a nullable code.

**Rationale**: This is identical to how `LegalId`-required-for-applicant is already modeled — the `Applicant` row is only created for the Applicant role, and the "required for this role" decision is a presentation/use-case concern, not a column constraint (other roles never get an Applicant row). Keeping it consistent avoids a special-case domain invariant that would contradict the nullable-for-legacy requirement. Whitespace-only is treated as blank (`string.IsNullOrWhiteSpace`).

---

## D4. Admin users list — matching `UserCode`/`LegalId` from an `ApplicationUser` query

**Decision**: Extend the existing `_userManager.Users` predicate with a correlated `_dbContext.Applicants.Any(...)` sub-query.

```csharp
query = query.Where(u =>
    (u.Email != null && u.Email.Contains(term)) ||
    (u.FirstName != null && u.FirstName.Contains(term)) ||
    (u.LastName != null && u.LastName.Contains(term)) ||
    _dbContext.Applicants.Any(a => a.UserId == u.Id &&
        (a.LegalId.Contains(term) ||
         (a.UserCode != null && a.UserCode.Contains(term)))));
```

**Rationale**: `UserAdministrationService` already holds `_dbContext` (it queries `_dbContext.Applicants` elsewhere), so no new dependency. EF Core translates the correlated `Any` to an `EXISTS` sub-query. Keeps the surface's existing `.Contains` style (case-insensitive under the DB's default CI collation, consistent with today's behavior — FR-015).

---

## D5. Reviewer queue + reports — match additions

**Decision**: Append `OR` clauses to the four existing `EF.Functions.Like` blocks:
- Reviewer queue (`ApplicationRepository.GetByStateForReviewerAsync` `:199-208`): add `a.Applicant.UserCode` and `a.Applicant.Email`.
- Reports Aging (`:354-361`) and Applications (`:520-527`): add `a.Applicant.UserCode`.
- Reports Applicants (`:580-587`, direct `Applicant` query): add `a.UserCode`.

**Rationale**: All four already `Include`/navigate `Applicant` and already `Like` on `LegalId`. `EF.Functions.Like(nullableColumn, pattern)` yields `NULL`→non-match for code-less applicants — correct. The Applicants CSV export reuses the Applicants-report query path (`ListApplicantsRequest`), so it inherits the match with no extra change to the search predicate (a column-surfacing change is separate — D6).

---

## D6. Surfacing the `UserCode` value as a column (FR-016, discretionary)

**Decision**:
- **Admin users list**: add a minimal "Código de usuario" column (es-CR), shown as `—` when absent. (The list already renders role/status/groups columns.)
- **Applicants report + CSV export**: add a "Código de usuario" column/field to the row projection and CSV header.
- **Reviewer queue**: **match-only, no new column** — keeps the queue row's micro-timeline layout (spec 011/025) uncluttered; FR-016 is explicitly discretionary and the spec's risk note favors minimalism.

**Rationale**: Surfaces where an operator looks up a person by code (admin list, applicants report/CSV) benefit from seeing it; the reviewer queue is a work-triage surface where the code is a search key, not a column. This is the "minimal, es-CR" reading of FR-016.

---

## D7. Register removal mechanics — 404 with no leftover links

**Decision**:
- Delete the `Register` GET + POST actions from `AccountController` (`:47-99`); delete `Views/Account/Register.cshtml`; delete the now-dead `RegisterViewModel`.
- With no `Register` action, the conventional route `{controller}/{action}` no longer resolves `/Account/Register` → ASP.NET returns **404** natively (FR-002), for both GET and a replayed POST.
- Remove the hero CTA's `asp-action="Register"` in `Views/Home/Index.cshtml` (`:30-33`) → repoint to `asp-action="Login"`; remove the "¿Aún no tienes cuenta? Crea una aquí" block in `Views/Account/Login.cshtml` (`:43-45`).
- `AccountController`'s constructor and `_dbContext`/`_userManager` deps stay — they're used by `Login`, `ForgotPassword`, password-reset, and `BuildProfileViewModelAsync` (FR-005).

**Rationale**: Removing the action (not just hiding the link) is what produces the 404 and guarantees no POST path can create an account. The applicant-creation logic that `Register` performed is already duplicated in `UserAdministrationService.CreateUserAsync` for the Applicant role, so nothing is lost (FR-004).

**Verification note**: grep for any other `asp-action="Register"` / `Url.Action("Register"` / `/Account/Register` reference before finishing, to honor the "no register links remain anywhere" success criterion (SC-001).

---

## D8. es-CR copy

**Decision**: Add consts to the existing static resource classes (no resx):
- `AdminUsersResources`: `UserCodeLabel = "Código de usuario"`, `UserCodeRequired = "El código de usuario es obligatorio para el rol Solicitante."`, `UserCodeInUse = "El código de usuario ya está en uso."`, and update the users-list search placeholder to read e.g. `"Nombre, correo, identificación o código de usuario"`.
- `ReviewerQueueResources`: update the search placeholder from `"Nombre o cédula"` to `"Nombre, cédula o código de usuario"`.

**Rationale**: Matches the established pattern (static C# const classes; `ReviewerQueueResources` already holds "Buscar solicitante"/"Nombre o cédula"). No new English literals; FR-017.

---

## Summary of decisions

| # | Topic | Decision |
|---|-------|----------|
| D1 | Field home | `Applicant.UserCode` (NVARCHAR(50) NULL) |
| D2 | Uniqueness | Filtered unique index `WHERE [UserCode] IS NOT NULL` + service pre-check; no backfill script |
| D3 | Required-for-role | Controller ModelState (mirrors LegalId), column nullable |
| D4 | Admin list match | Correlated `Applicants.Any(...)` sub-query |
| D5 | Queue/report match | Append `Like` ORs on UserCode (+ email on queue) |
| D6 | Column surfacing | Admin list + applicants report/CSV get a column; reviewer queue match-only |
| D7 | Register removal | Delete actions/view/VM → native 404; repoint/remove 2 links |
| D8 | Copy | Consts in `AdminUsersResources` / `ReviewerQueueResources` |
