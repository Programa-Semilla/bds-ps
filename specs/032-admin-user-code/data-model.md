# Phase 1 Data Model: Admin-only user provisioning + unique applicant User Code

**Feature**: 032-admin-user-code
**Date**: 2026-06-11

This feature adds **one nullable column + one filtered unique index** to the existing `Applicant` aggregate. No new entities, no new tables, no relationship changes.

---

## Modified entity: `Applicant`

`src/FundingPlatform.Domain/Entities/Applicant.cs`

### New property

| Property | Type | Null | Constraint | Notes |
|----------|------|------|------------|-------|
| `UserCode` | `string?` | yes | ≤50 chars; unique among non-null values | Admin-assigned free-text identifier (es-CR "Código de usuario"). Required for the Applicant role at the use-case boundary, not at the column. |

### Behavior changes (Rich Domain Model — Constitution II)

- **Constructor**: add a trailing optional parameter `string? userCode = null`. Trim to null when whitespace-only; reject (`ArgumentException`) when non-null length > 50.
- **`UpdateProfile(...)`**: add a `string? userCode` parameter so an admin edit can set/clear it through the same entity method already used for the other profile fields. Same trim/length guard.
- Uniqueness is **not** an entity invariant (it's a cross-row rule) — it is enforced by the service pre-check + DB filtered index, identical to how `LegalId` uniqueness is handled today.

### Unchanged invariants
`LegalId`, `IdentificationType`, name/email/phone, and the 1:1 `UserId → AspNetUsers.Id` relationship are untouched.

---

## EF Core configuration

`src/FundingPlatform.Infrastructure/Persistence/Configurations/ApplicantConfiguration.cs`

Add, mirroring the dacpac (which remains the source of truth — Constitution IV):

```csharp
builder.Property(a => a.UserCode).HasMaxLength(50);            // nullable: no .IsRequired()
builder.HasIndex(a => a.UserCode)
       .IsUnique()
       .HasDatabaseName("UX_Applicants_UserCode")
       .HasFilter("[UserCode] IS NOT NULL");
```

---

## dacpac schema (source of truth — Constitution IV)

`src/FundingPlatform.Database/Tables/dbo.Applicants.sql`

1. Add the column (nullable → migration-safe on the populated table, **no** post-deploy backfill):

```sql
[UserCode]         NVARCHAR(50)   NULL,
```

2. Add the filtered unique index (same shape as `UX_Appeals_OneOpenPerApplication` / `UX_SignedUploads_OnePending_PerAgreement`):

```sql
GO
CREATE UNIQUE NONCLUSTERED INDEX [UX_Applicants_UserCode]
    ON [dbo].[Applicants] ([UserCode]) WHERE [UserCode] IS NOT NULL;
GO
```

No change to `PostDeployment/` or `FundingPlatform.Database.sqlproj` — the column is nullable with no seed/backfill.

---

## DTO changes (Application layer)

`src/FundingPlatform.Application/Admin/Users/DTOs/`

| DTO | Change |
|-----|--------|
| `CreateUserRequest` | add `string? UserCode` |
| `UpdateUserRequest` | add `string? UserCode` |
| `UserDetailDto` | add `string? UserCode` (so Edit can prefill and detail views can show it) |

The report-row DTOs for the **Applicants report/CSV** gain a `UserCode` field (D6); the Applications/Aging report rows and the reviewer-queue row DTO are **unchanged** (match-only).

---

## View models (Web layer)

| View model | Change |
|------------|--------|
| `AdminUserCreateViewModel` | add `string? UserCode` (`[StringLength(50)]`) |
| `AdminUserEditViewModel` | add `string? UserCode` (`[StringLength(50)]`) |
| `ProfileViewModel` | add `string? UserCode` (init-only; populated from `applicant?.UserCode`) |

`RegisterViewModel` is **deleted** (dead after register removal).

---

## Validation rules (where enforced)

| Rule | Enforced at | Message (es-CR) |
|------|-------------|-----------------|
| ≤50 chars | VM `[StringLength(50)]` + entity guard | default / framework |
| Required when role = Solicitante (blank/whitespace rejected) | `AdminUsersController` Create/Edit ModelState (mirrors LegalId `:177-180`) | "El código de usuario es obligatorio para el rol Solicitante." |
| Unique among assigned codes (common path) | `UserAdministrationService` pre-check (`AnyAsync`, mirrors `LEGAL_ID_IN_USE`); excludes self on update via canonical compare | "El código de usuario ya está en uso." |
| Unique (concurrency backstop) | DB `UX_Applicants_UserCode` → `DbUpdateException` mapped in controller | "El código de usuario ya está en uso." |
| Not shown/validated for non-applicant roles | view JS show/hide + controller skips check when role ≠ Applicant | n/a |

---

## State / lifecycle notes

- **Legacy applicants**: existing rows get `UserCode = NULL` on deploy; valid, excluded from code search, exempt from the required rule until next admin edit (edge cases).
- **Role changed away from Solicitante on edit**: controller skips the required/unique checks; any existing `UserCode` value is **retained** (not cleared) — the `Applicant` row persists with its code.
- **Uniqueness vs. NULL**: many `NULL` rows never collide (filtered index); only assigned values are constrained.
