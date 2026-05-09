# Phase 1: Data Model — PDF Template Lift

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Research:** [research.md](./research.md)
**Date:** 2026-05-08

This feature changes two existing entities (`Application`, `Item`) and removes one options binding (`FunderOptions`). No new entities are introduced.

## Entity Changes

### Application (modified)

**Schema** (`src/FundingPlatform.Database/Tables/dbo.Applications.sql`):

```sql
CREATE TABLE [dbo].[Applications]
(
    [Id]          INT            IDENTITY(1,1) NOT NULL,
    [ApplicantId] INT            NOT NULL,
    [CompanyName] NVARCHAR(200)  NOT NULL,                              -- NEW
    [State]       INT            NOT NULL CONSTRAINT [DF_Applications_State] DEFAULT (0),
    [CreatedAt]   DATETIME2      NOT NULL CONSTRAINT [DF_Applications_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]   DATETIME2      NOT NULL,
    [SubmittedAt] DATETIME2      NULL,
    [RowVersion]  ROWVERSION     NOT NULL,

    CONSTRAINT [PK_Applications] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Applications_Applicants] FOREIGN KEY ([ApplicantId]) REFERENCES [dbo].[Applicants] ([Id]) ON DELETE NO ACTION
);
```

**Domain entity** (`src/FundingPlatform.Domain/Entities/Application.cs`):

- New property: `string CompanyName { get; private set; }` — required, ≤200 chars, trimmed.
- New constructor signature: `public Application(int applicantId, string companyName)` — sets `CompanyName` via `SetCompanyName(...)`.
- New behaviour method:
  ```csharp
  public void SetCompanyName(string companyName)
  // Trim. Throw ArgumentException if null/whitespace/empty after trim.
  // Throw ArgumentException if length > 200 after trim.
  // Persist trimmed value; bump UpdatedAt.
  ```
- New aggregate behaviour: `public void AssignLineCodeToItem(int itemId, string lineCode)` — see "Item" section below.

**EF configuration** (`Infrastructure/Persistence/Configurations/ApplicationConfiguration.cs`):

- Add `b.Property(a => a.CompanyName).IsRequired().HasMaxLength(200);`.

**Validation rules** (per FR-015, FR-016):

- Required (non-blank after trim).
- Maximum length 200 characters after trim.
- Trim leading/trailing whitespace before validation and storage.

**State transitions**: unchanged.

### Item (modified)

**Tension worth flagging upfront**: `LineCode` is reviewer-assigned, so it cannot be known when the applicant first adds the Item. We have two viable column shapes; the second is **adopted**.

- Variant A — `NOT NULL` with empty-string-as-unassigned: simple to type but spreads `""`-is-magic checks across the codebase.
- **Variant B (adopted)** — `NULL` with a filtered unique index (`WHERE LineCode IS NOT NULL`). EF property is `string? LineCode`. "Unassigned" is the standard SQL `NULL` value. No magic-empty-string code path. Uniqueness still enforced for assigned codes.

**Schema** (`src/FundingPlatform.Database/Tables/dbo.Items.sql`) — Variant B:

```sql
CREATE TABLE [dbo].[Items]
(
    [Id]                          INT            IDENTITY(1,1) NOT NULL,
    [ApplicationId]               INT            NOT NULL,
    [LineCode]                    NVARCHAR(16)   NULL,                                                -- NEW (nullable)
    [ProductName]                 NVARCHAR(500)  NOT NULL,
    [CategoryId]                  INT            NOT NULL,
    [TechnicalSpecifications]     NVARCHAR(MAX)  NOT NULL,
    [ReviewStatus]                INT            NOT NULL CONSTRAINT [DF_Items_ReviewStatus] DEFAULT (0),
    [ReviewComment]               NVARCHAR(2000) NULL,
    [SelectedSupplierId]          INT            NULL,
    [IsNotTechnicallyEquivalent]  BIT            NOT NULL CONSTRAINT [DF_Items_IsNotTechnicallyEquivalent] DEFAULT (0),
    [CreatedAt]                   DATETIME2      NOT NULL CONSTRAINT [DF_Items_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]                   DATETIME2      NOT NULL,

    CONSTRAINT [PK_Items] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_Items_Applications] FOREIGN KEY ([ApplicationId]) REFERENCES [dbo].[Applications] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Items_Categories] FOREIGN KEY ([CategoryId]) REFERENCES [dbo].[Categories] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Items_Suppliers_SelectedSupplierId] FOREIGN KEY ([SelectedSupplierId]) REFERENCES [dbo].[Suppliers] ([Id])
);
GO

CREATE UNIQUE INDEX [UX_Items_Application_LineCode]
    ON [dbo].[Items] ([ApplicationId], [LineCode])
    WHERE [LineCode] IS NOT NULL;                                                                     -- NEW (filtered unique)
GO
```

**Domain entity** (`src/FundingPlatform.Domain/Entities/Item.cs`):

- New property: `string? LineCode { get; private set; }` — nullable until reviewer assigns; once assigned, trimmed and ≤16 chars.
- Item constructor signature is **unchanged** — applicants continue to add items without supplying a LineCode (`LineCode` defaults to `null`).
- New behaviour method on `Item` (called only via `Application.AssignLineCodeToItem` per Constitution II):
  ```csharp
  internal void AssignLineCode(string lineCode)
  // Trim. Throw ArgumentException if null/whitespace/empty after trim.
  // Throw ArgumentException if length > 16 after trim.
  // Persist trimmed value; bump UpdatedAt.
  ```
- New behaviour method on `Application`:
  ```csharp
  public void AssignLineCodeToItem(int itemId, string lineCode)
  // Find item in this application's _items. Throw if not found.
  // Trim and validate (non-blank, ≤16) — delegates to Item.AssignLineCode for the write.
  // Check uniqueness: if any sibling Item has the same trimmed LineCode (case-sensitive),
  //   throw InvalidOperationException with a duplicate-code message.
  // Then call item.AssignLineCode(trimmed).
  ```

**EF configuration** (`Infrastructure/Persistence/Configurations/ItemConfiguration.cs`):

- Add `b.Property(i => i.LineCode).HasMaxLength(16).IsRequired(false);`.
- Add filtered unique index: `b.HasIndex(i => new { i.ApplicationId, i.LineCode }).IsUnique().HasFilter("[LineCode] IS NOT NULL");`.

**Validation rules** (per FR-012, FR-013, FR-014):

- Required at the moment of the per-item review decision **when** the decision is `Approve` or `Reject` (see R-008 + Contract 2). `RequestMoreInfo` allows a blank LineCode.
- Maximum length 16 characters after trim.
- Free-text (no charset constraint beyond NVARCHAR(16)).
- Unique within Application.
- Trim leading/trailing whitespace before validation and storage.

**State transitions**: existing `Approve`, `Reject`, `RequestMoreInfo`, `FlagNotEquivalent`, `ResetReviewStatus` unchanged. `AssignLineCode` is orthogonal; it can be called multiple times (reviewer re-assigning a code) up until the Application transitions to `Resolved`.

### FundingAgreement / supporting projections (modified)

No schema change. The projection (`FundingAgreementService`) is rewritten to emit a new shape (cover, intro, requested-resources, committee-results, supplier-verification, declaration). See contracts/README.md for the projection contract.

### FunderOptions (deleted)

`src/FundingPlatform.Application/Options/FunderOptions.cs` is deleted (FR-019 → FR-022). No replacement.

## Cross-cutting Validation Surface

| Surface | Application.CompanyName | Item.LineCode |
|---------|------------------------|---------------|
| Domain entity | `Application.SetCompanyName(string)` (required, trimmed, ≤200) | `Application.AssignLineCodeToItem(int, string)` → `Item.AssignLineCode(string)` (required, trimmed, ≤16, unique) |
| EF configuration | `IsRequired().HasMaxLength(200)` | `HasMaxLength(16).IsRequired(false)` + filtered unique index |
| dacpac | `NVARCHAR(200) NOT NULL` | `NVARCHAR(16) NULL` + `UX_Items_Application_LineCode` (WHERE not null) |
| Application command | `CreateApplicationCommand(int applicantId, string companyName)` | `ReviewItemCommand` adds `string LineCode` |
| Web controller | `ApplicationController.Create(CreateApplicationViewModel)` binds `CompanyName` | `ReviewController.ReviewItem(...)` binds `LineCode` |
| User-facing error | `UserFacingErrorCode.CompanyNameRequired`, `CompanyNameTooLong` | `UserFacingErrorCode.LineCodeRequired`, `LineCodeTooLong`, `LineCodeDuplicate` |

All validation is a single hop down the stack: View → controller → command → application service → domain entity. The domain entity is the *only* place the rules are evaluated.

## Out-of-scope data shape changes (explicitly NOT done)

- No changes to `Applicant`, `Supplier`, `SupplierBranch`, `Quotation`, `ItemResponse`, `ApplicantResponse`, `Appeal`, `Currency`, `ExchangeRate`, `Group`, `UserGroupMembership`, `AdminAuditEvent`.
- No new `Tract` / `LineGroup` entity (per spec out-of-scope).
- No DB-backed legal-copy table (sworn declaration text stays hardcoded in Razor per spec assumption).
