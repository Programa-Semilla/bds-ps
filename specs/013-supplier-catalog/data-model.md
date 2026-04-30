# Data Model: Centralized Supplier Catalog

**Date:** 2026-04-30
**Spec:** [spec.md](./spec.md)
**Plan:** [plan.md](./plan.md)
**Research:** [research.md](./research.md)

## Domain Model

### `Supplier` (aggregate root, modified)

```csharp
namespace FundingPlatform.Domain.Entities;

public class Supplier
{
    private readonly List<SupplierBranch> _branches = new();

    public int Id { get; private set; }
    public string LegalId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    // Admin-only flags (FR-040, FR-043)
    public bool HasElectronicInvoice { get; private set; }
    public bool IsCompliantCCSS { get; private set; }
    public bool IsCompliantHacienda { get; private set; }
    public bool IsCompliantSICOP { get; private set; }

    // Lifecycle (FR-021, FR-024, FR-035)
    public SupplierVerificationStatus VerificationStatus { get; private set; }
    public int? CreatedByApplicantId { get; private set; }
    public string? VerifiedByUserId { get; private set; }
    public DateTime? VerifiedAt { get; private set; }
    public string? RejectionReason { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyCollection<SupplierBranch> Branches => _branches.AsReadOnly();

    private Supplier() { }

    // Applicant-initiated factory (FR-021): creates a Draft with one default branch.
    public static Supplier CreateDraft(
        string legalId,
        string name,
        int createdByApplicantId,
        SupplierBranch initialDefaultBranch)
    {
        // legalId normalized at the application layer before this factory is called.
        ArgumentException.ThrowIfNullOrWhiteSpace(legalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(initialDefaultBranch);
        if (!initialDefaultBranch.IsDefault)
            throw new ArgumentException("Initial branch must be the default branch.", nameof(initialDefaultBranch));

        var s = new Supplier
        {
            LegalId = legalId,
            Name = name,
            CreatedByApplicantId = createdByApplicantId,
            VerificationStatus = SupplierVerificationStatus.Draft,
            HasElectronicInvoice = false,
            IsCompliantCCSS = false,
            IsCompliantHacienda = false,
            IsCompliantSICOP = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        s._branches.Add(initialDefaultBranch);
        return s;
    }

    // Lifecycle methods (Constitution II: rich domain model)

    public void SubmitForReview()
    {
        if (VerificationStatus != SupplierVerificationStatus.Draft)
            return; // idempotent — non-Draft statuses are already past the submit point
        VerificationStatus = SupplierVerificationStatus.PendingReview;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Verify(string verifiedByUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedByUserId);
        if (VerificationStatus == SupplierVerificationStatus.Draft)
            throw new InvalidOperationException("Cannot verify a Draft supplier; submit for review first.");
        VerificationStatus = SupplierVerificationStatus.Verified;
        VerifiedByUserId = verifiedByUserId;
        VerifiedAt = DateTime.UtcNow;
        RejectionReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reject(string verifiedByUserId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedByUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (VerificationStatus == SupplierVerificationStatus.Draft)
            throw new InvalidOperationException("Cannot reject a Draft supplier.");
        VerificationStatus = SupplierVerificationStatus.Rejected;
        VerifiedByUserId = verifiedByUserId;
        VerifiedAt = DateTime.UtcNow;
        RejectionReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    // Applicant edit while parent application is Draft (FR-022)
    public void RenameByApplicant(string newName)
    {
        if (VerificationStatus != SupplierVerificationStatus.Draft)
            throw new InvalidOperationException("Applicants cannot rename non-Draft suppliers.");
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Name = newName;
        UpdatedAt = DateTime.UtcNow;
    }

    // Admin edits (FR-032, FR-033)
    public void EditByAdmin(
        string newName,
        bool hasElectronicInvoice,
        bool isCompliantCCSS,
        bool isCompliantHacienda,
        bool isCompliantSICOP)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        Name = newName;
        HasElectronicInvoice = hasElectronicInvoice;
        IsCompliantCCSS = isCompliantCCSS;
        IsCompliantHacienda = isCompliantHacienda;
        IsCompliantSICOP = isCompliantSICOP;
        UpdatedAt = DateTime.UtcNow;
    }

    // Branch operations (single source of truth for the "exactly one default" invariant)

    public SupplierBranch AddBranch(
        string branchName,
        string? contactName,
        string? email,
        string? phone,
        string? addressLine,
        string? province,
        string? shippingDetails,
        string? warrantyInfo,
        int? createdByApplicantId,
        bool isDefault = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);
        if (isDefault && _branches.Any(b => b.IsDefault))
            throw new InvalidOperationException("Supplier already has a default branch.");
        var branch = new SupplierBranch(
            branchName, contactName, email, phone, addressLine, province,
            shippingDetails, warrantyInfo, isDefault, createdByApplicantId);
        _branches.Add(branch);
        UpdatedAt = DateTime.UtcNow;
        return branch;
    }

    public void EditBranch(int branchId, /* fields */ string branchName, string? contactName, /* ... */ )
    {
        var branch = _branches.FirstOrDefault(b => b.Id == branchId)
            ?? throw new InvalidOperationException("Branch not found.");
        branch.Edit(branchName, contactName, /* ... */);
        UpdatedAt = DateTime.UtcNow;
    }
}
```

### `SupplierBranch` (entity, new)

```csharp
namespace FundingPlatform.Domain.Entities;

public class SupplierBranch
{
    public int Id { get; private set; }
    public int SupplierId { get; private set; }
    public string BranchName { get; private set; } = string.Empty;
    public string? ContactName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? AddressLine { get; private set; }
    public string? Province { get; private set; }      // free text in v1
    public string? ShippingDetails { get; private set; }
    public string? WarrantyInfo { get; private set; }
    public bool IsDefault { get; private set; }
    public int? CreatedByApplicantId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private SupplierBranch() { }

    internal SupplierBranch(
        string branchName,
        string? contactName,
        string? email,
        string? phone,
        string? addressLine,
        string? province,
        string? shippingDetails,
        string? warrantyInfo,
        bool isDefault,
        int? createdByApplicantId)
    {
        BranchName = branchName;
        ContactName = contactName;
        Email = email;
        Phone = phone;
        AddressLine = addressLine;
        Province = province;
        ShippingDetails = shippingDetails;
        WarrantyInfo = warrantyInfo;
        IsDefault = isDefault;
        CreatedByApplicantId = createdByApplicantId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    internal void Edit(string branchName, string? contactName, /* ... */)
    {
        BranchName = branchName;
        ContactName = contactName;
        // ... etc.
        UpdatedAt = DateTime.UtcNow;
    }
}
```

### `SupplierVerificationStatus` (enum, new)

```csharp
namespace FundingPlatform.Domain.Enums;

public enum SupplierVerificationStatus : byte
{
    Draft = 0,
    PendingReview = 1,
    Verified = 2,
    Rejected = 3,
}
```

## Aggregate Boundary Notes

- `Supplier` is the aggregate root. `SupplierBranch` is part of the aggregate, not a separate root.
- `Quotation` references `SupplierBranch` and (denormalized) `Supplier` via integer FKs only — no navigation properties on `Quotation` carrying the full branch graph (perf reasons; review screens load suppliers via dedicated queries).
- The "exactly one default branch" invariant is enforced inside `Supplier.AddBranch` and at the database level via a filtered unique index.
- The `Quotation.SupplierId == Branch.SupplierId` invariant is enforced at the application layer (`AddSupplierQuotationAsync` writes both from the same entity); a CHECK constraint is not added because it would require a sub-query (forbidden in MS SQL CHECK constraints).

## SQL Schema

### `dbo.Suppliers` (modified)

```sql
CREATE TABLE [dbo].[Suppliers]
(
    [Id]                       INT             IDENTITY(1,1) NOT NULL,
    [LegalId]                  NVARCHAR(50)    NOT NULL,
    [Name]                     NVARCHAR(300)   NOT NULL,
    [HasElectronicInvoice]     BIT             NOT NULL CONSTRAINT [DF_Suppliers_HasElectronicInvoice] DEFAULT (0),
    [IsCompliantCCSS]          BIT             NOT NULL CONSTRAINT [DF_Suppliers_IsCompliantCCSS] DEFAULT (0),
    [IsCompliantHacienda]      BIT             NOT NULL CONSTRAINT [DF_Suppliers_IsCompliantHacienda] DEFAULT (0),
    [IsCompliantSICOP]         BIT             NOT NULL CONSTRAINT [DF_Suppliers_IsCompliantSICOP] DEFAULT (0),

    [VerificationStatus]       TINYINT         NOT NULL CONSTRAINT [DF_Suppliers_VerificationStatus] DEFAULT (2),  -- 2 = Verified for migrated rows
    [CreatedByApplicantId]     INT             NULL,
    [VerifiedByUserId]         NVARCHAR(450)   NULL,
    [VerifiedAt]               DATETIME2       NULL,
    [RejectionReason]          NVARCHAR(1000)  NULL,

    [CreatedAt]                DATETIME2       NOT NULL CONSTRAINT [DF_Suppliers_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]                DATETIME2       NOT NULL,

    -- TODO[013-cleanup]: drop these six columns one release after the migration ships.
    [ContactName]              NVARCHAR(200)   NULL,
    [Email]                    NVARCHAR(256)   NULL,
    [Phone]                    NVARCHAR(20)    NULL,
    [Location]                 NVARCHAR(500)   NULL,
    [ShippingDetails]          NVARCHAR(500)   NULL,
    [WarrantyInfo]             NVARCHAR(500)   NULL,

    CONSTRAINT [PK_Suppliers] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UX_Suppliers_LegalId] UNIQUE ([LegalId]),
    CONSTRAINT [FK_Suppliers_Applicants] FOREIGN KEY ([CreatedByApplicantId]) REFERENCES [dbo].[Applicants] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Suppliers_AspNetUsers] FOREIGN KEY ([VerifiedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION,
);

CREATE INDEX [IX_Suppliers_VerificationStatus] ON [dbo].[Suppliers] ([VerificationStatus]);
CREATE INDEX [IX_Suppliers_Name] ON [dbo].[Suppliers] ([Name]);
```

### `dbo.SupplierBranches` (new)

```sql
CREATE TABLE [dbo].[SupplierBranches]
(
    [Id]                     INT             IDENTITY(1,1) NOT NULL,
    [SupplierId]             INT             NOT NULL,
    [BranchName]             NVARCHAR(200)   NOT NULL,
    [ContactName]            NVARCHAR(200)   NULL,
    [Email]                  NVARCHAR(256)   NULL,
    [Phone]                  NVARCHAR(20)    NULL,
    [AddressLine]            NVARCHAR(500)   NULL,
    [Province]               NVARCHAR(100)   NULL,
    [ShippingDetails]        NVARCHAR(500)   NULL,
    [WarrantyInfo]           NVARCHAR(500)   NULL,
    [IsDefault]              BIT             NOT NULL CONSTRAINT [DF_SupplierBranches_IsDefault] DEFAULT (0),
    [CreatedByApplicantId]   INT             NULL,
    [CreatedAt]              DATETIME2       NOT NULL CONSTRAINT [DF_SupplierBranches_CreatedAt] DEFAULT (GETUTCDATE()),
    [UpdatedAt]              DATETIME2       NOT NULL,

    CONSTRAINT [PK_SupplierBranches] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_SupplierBranches_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_SupplierBranches_Applicants] FOREIGN KEY ([CreatedByApplicantId]) REFERENCES [dbo].[Applicants] ([Id]) ON DELETE NO ACTION,
);

CREATE INDEX [IX_SupplierBranches_SupplierId] ON [dbo].[SupplierBranches] ([SupplierId]);
CREATE UNIQUE INDEX [UX_SupplierBranches_DefaultPerSupplier]
    ON [dbo].[SupplierBranches] ([SupplierId])
    WHERE [IsDefault] = 1;
```

### `dbo.Quotations` (modified)

```sql
CREATE TABLE [dbo].[Quotations]
(
    [Id]                INT           IDENTITY(1,1) NOT NULL,
    [ItemId]            INT           NOT NULL,
    [SupplierId]        INT           NOT NULL,
    [SupplierBranchId]  INT           NOT NULL,                    -- NEW
    [Price]             DECIMAL(18,2) NOT NULL,
    [ValidUntil]        DATE          NOT NULL,
    [DocumentId]        INT           NOT NULL,
    [Currency]          NVARCHAR(3)   NULL,
    [CreatedAt]         DATETIME2     NOT NULL CONSTRAINT [DF_Quotations_CreatedAt] DEFAULT (GETUTCDATE()),

    CONSTRAINT [PK_Quotations] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [UX_Quotations_ItemId_SupplierId] UNIQUE ([ItemId], [SupplierId]),  -- unchanged per R1
    CONSTRAINT [FK_Quotations_Items] FOREIGN KEY ([ItemId]) REFERENCES [dbo].[Items] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_Quotations_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Quotations_SupplierBranches] FOREIGN KEY ([SupplierBranchId]) REFERENCES [dbo].[SupplierBranches] ([Id]) ON DELETE NO ACTION,    -- NEW
    CONSTRAINT [FK_Quotations_Documents] FOREIGN KEY ([DocumentId]) REFERENCES [dbo].[Documents] ([Id]) ON DELETE NO ACTION
);

CREATE INDEX [IX_Quotations_SupplierBranchId] ON [dbo].[Quotations] ([SupplierBranchId]);
```

### Migration script: `PostDeployment/Migrations/013_SupplierCatalog.sql`

```sql
-- 013_SupplierCatalog.sql — idempotent forward-only migration.
--
-- Pre-conditions:
--   - dacpac deploy has already added VerificationStatus, CreatedByApplicantId,
--     VerifiedByUserId, VerifiedAt, RejectionReason on dbo.Suppliers.
--   - dbo.SupplierBranches table is created (empty).
--   - dbo.Quotations.SupplierBranchId column exists but is NULL on existing rows.
--   - Legacy columns (ContactName, Email, Phone, Location, ShippingDetails,
--     WarrantyInfo) still exist on dbo.Suppliers and carry the source data.
--
-- Post-conditions:
--   - Every dbo.Suppliers row: VerificationStatus = Verified (2),
--     VerifiedByUserId = system-admin sentinel, VerifiedAt = SYSUTCDATETIME().
--   - Every dbo.Suppliers row: exactly one matching dbo.SupplierBranches row
--     with IsDefault = 1, BranchName = 'Sede principal', contact fields
--     copied from the legacy Suppliers columns.
--   - Every dbo.Quotations row: non-null SupplierBranchId pointing at the
--     default branch of its supplier.
--   - SupplierId == Branch.SupplierId for every quotation row.

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.Suppliers') AND name = 'ContactName')
   AND NOT EXISTS (SELECT 1 FROM dbo.SupplierBranches)
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @SystemAdminId NVARCHAR(450) = (
            SELECT TOP 1 [Id] FROM [dbo].[AspNetUsers]
            WHERE [IsSystemSentinel] = 1
        );

        IF @SystemAdminId IS NULL
            THROW 50010, 'Migration 013: system admin sentinel not found. Spec 009 must be deployed first.', 1;

        -- 1. Backfill verification on existing Suppliers rows.
        UPDATE [dbo].[Suppliers]
        SET VerificationStatus = 2, -- Verified
            VerifiedByUserId   = @SystemAdminId,
            VerifiedAt         = SYSUTCDATETIME(),
            UpdatedAt          = SYSUTCDATETIME();

        -- 2. Insert one default branch per supplier carrying legacy contact data.
        INSERT INTO [dbo].[SupplierBranches]
            (SupplierId, BranchName, ContactName, Email, Phone,
             AddressLine, Province, ShippingDetails, WarrantyInfo,
             IsDefault, CreatedByApplicantId, CreatedAt, UpdatedAt)
        SELECT s.Id,
               N'Sede principal',
               s.ContactName, s.Email, s.Phone,
               s.Location, NULL, s.ShippingDetails, s.WarrantyInfo,
               1, NULL, SYSUTCDATETIME(), SYSUTCDATETIME()
        FROM [dbo].[Suppliers] s;

        -- 3. Repoint every quotation to its supplier's default branch.
        UPDATE q
        SET q.SupplierBranchId = b.Id
        FROM [dbo].[Quotations] q
        INNER JOIN [dbo].[SupplierBranches] b
                ON b.SupplierId = q.SupplierId
               AND b.IsDefault = 1
        WHERE q.SupplierBranchId IS NULL OR q.SupplierBranchId = 0;

        -- 4. Assertions.
        IF EXISTS (SELECT 1 FROM [dbo].[Quotations] WHERE SupplierBranchId IS NULL)
            THROW 50011, 'Migration 013: at least one Quotation has a NULL SupplierBranchId after backfill.', 1;

        IF EXISTS (
            SELECT 1
            FROM [dbo].[Quotations] q
            INNER JOIN [dbo].[SupplierBranches] b ON q.SupplierBranchId = b.Id
            WHERE q.SupplierId <> b.SupplierId)
            THROW 50012, 'Migration 013: at least one Quotation has SupplierId != Branch.SupplierId.', 1;

        IF EXISTS (
            SELECT s.Id
            FROM [dbo].[Suppliers] s
            LEFT JOIN [dbo].[SupplierBranches] b ON b.SupplierId = s.Id AND b.IsDefault = 1
            WHERE b.Id IS NULL)
            THROW 50013, 'Migration 013: at least one Supplier is missing a default branch.', 1;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        ;THROW;
    END CATCH;
END;
```

The migration is invoked from `SeedData.sql` like the spec 010 currency migration:

```sql
-- in SeedData.sql, after existing seeds
:r .\Migrations\013_SupplierCatalog.sql
```

## EF Core Configuration Sketches

### `SupplierConfiguration` (modified)

```csharp
public void Configure(EntityTypeBuilder<Supplier> builder)
{
    builder.ToTable("Suppliers");
    builder.HasKey(s => s.Id);

    builder.Property(s => s.LegalId).IsRequired().HasMaxLength(50);
    builder.HasIndex(s => s.LegalId).IsUnique().HasDatabaseName("UX_Suppliers_LegalId");

    builder.Property(s => s.Name).IsRequired().HasMaxLength(300);
    builder.Property(s => s.HasElectronicInvoice).IsRequired();
    builder.Property(s => s.IsCompliantCCSS).IsRequired();
    builder.Property(s => s.IsCompliantHacienda).IsRequired();
    builder.Property(s => s.IsCompliantSICOP).IsRequired();

    builder.Property(s => s.VerificationStatus).HasConversion<byte>().IsRequired();
    builder.Property(s => s.CreatedByApplicantId);
    builder.Property(s => s.VerifiedByUserId).HasMaxLength(450);
    builder.Property(s => s.VerifiedAt);
    builder.Property(s => s.RejectionReason).HasMaxLength(1000);

    builder.Property(s => s.CreatedAt).IsRequired();
    builder.Property(s => s.UpdatedAt).IsRequired();

    // The legacy columns are excluded from the EF model entirely so application
    // code cannot read or write them. The dacpac still defines them until the
    // 013-cleanup PR drops them.
    builder.Ignore("ContactName");      // legacy
    builder.Ignore("Email");            // legacy
    builder.Ignore("Phone");            // legacy
    builder.Ignore("Location");         // legacy
    builder.Ignore("ShippingDetails");  // legacy
    builder.Ignore("WarrantyInfo");     // legacy

    builder.HasMany(s => s.Branches)
           .WithOne()
           .HasForeignKey(b => b.SupplierId)
           .OnDelete(DeleteBehavior.Cascade);

    builder.Navigation(s => s.Branches)
           .HasField("_branches")
           .UsePropertyAccessMode(PropertyAccessMode.Field);
}
```

### `SupplierBranchConfiguration` (new)

```csharp
public void Configure(EntityTypeBuilder<SupplierBranch> builder)
{
    builder.ToTable("SupplierBranches");
    builder.HasKey(b => b.Id);

    builder.Property(b => b.SupplierId).IsRequired();
    builder.Property(b => b.BranchName).IsRequired().HasMaxLength(200);
    builder.Property(b => b.ContactName).HasMaxLength(200);
    builder.Property(b => b.Email).HasMaxLength(256);
    builder.Property(b => b.Phone).HasMaxLength(20);
    builder.Property(b => b.AddressLine).HasMaxLength(500);
    builder.Property(b => b.Province).HasMaxLength(100);
    builder.Property(b => b.ShippingDetails).HasMaxLength(500);
    builder.Property(b => b.WarrantyInfo).HasMaxLength(500);
    builder.Property(b => b.IsDefault).IsRequired();
    builder.Property(b => b.CreatedByApplicantId);
    builder.Property(b => b.CreatedAt).IsRequired();
    builder.Property(b => b.UpdatedAt).IsRequired();

    builder.HasIndex(b => b.SupplierId);
    builder.HasIndex(b => b.SupplierId)
           .IsUnique()
           .HasFilter("[IsDefault] = 1")
           .HasDatabaseName("UX_SupplierBranches_DefaultPerSupplier");
}
```

### `QuotationConfiguration` (modified)

Add:

```csharp
builder.Property(q => q.SupplierBranchId).IsRequired();
builder.HasIndex(q => q.SupplierBranchId);
```

## Validation Rules Summary

| Rule | Enforced at |
|---|---|
| `LegalId` UNIQUE | DB (UX_Suppliers_LegalId) + caught by `SupplierCatalogService` to map to a Result.RetryWithExisting outcome (R4) |
| Exactly one default branch per supplier | DB (`UX_SupplierBranches_DefaultPerSupplier`) + Domain (`Supplier.AddBranch`) |
| `Quotation.SupplierId == Branch.SupplierId` | Application (`AddSupplierQuotationAsync` writes both atomically); migration script asserts on existing data |
| Status transitions (Draft → Pending only via Submit; Pending/Verified/Rejected → Verified or Rejected only via admin) | Domain (`Supplier.Verify` / `Supplier.Reject` / `Supplier.SubmitForReview`) + Controller-level `[Authorize(Roles = "Admin")]` for the admin transitions |
| Applicant edits forbidden on non-Draft suppliers | Domain (`Supplier.RenameByApplicant` throws) + Controller checks `Supplier.CreatedByApplicantId == currentApplicantId && parentApplication.IsDraft` |
| Admin must provide non-empty `RejectionReason` | Domain (`Supplier.Reject` throws on null/whitespace) + ViewModel validation |

## State Transitions

```text
                      Application.Submit
       Draft ─────────────────────────────→ PendingReview
         │                                       │
         │  (creator deletes draft app: cascade)  │ admin Verify
         ▼                                        ▼
       (deleted)                              Verified ←──┐ (re-verify after rejection)
                                                  │       │
                                                  │ admin │ admin
                                                  │ Reject│ Verify
                                                  ▼       │
                                              Rejected ───┘
```

Notes:
- `Draft → Verified` directly is not a permitted transition; admin must wait for application submission to flip to PendingReview first. (Edge: admin discovers a Draft via direct DB nav — not in v1 UX.)
- Rejected suppliers can be re-verified by admin (R5 lifecycle).
- The reverse `PendingReview → Draft` is NOT supported (FR-025).
