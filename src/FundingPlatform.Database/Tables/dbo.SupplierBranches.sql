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
    [UpdatedAt]              DATETIME2       NOT NULL CONSTRAINT [DF_SupplierBranches_UpdatedAt] DEFAULT (GETUTCDATE()),

    CONSTRAINT [PK_SupplierBranches] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_SupplierBranches_Suppliers] FOREIGN KEY ([SupplierId]) REFERENCES [dbo].[Suppliers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_SupplierBranches_Applicants] FOREIGN KEY ([CreatedByApplicantId]) REFERENCES [dbo].[Applicants] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_SupplierBranches_SupplierId] ON [dbo].[SupplierBranches] ([SupplierId]);
GO

-- Spec 013: enforce exactly one default branch per supplier (FR-021).
CREATE UNIQUE INDEX [UX_SupplierBranches_DefaultPerSupplier]
    ON [dbo].[SupplierBranches] ([SupplierId])
    WHERE [IsDefault] = 1;
GO
