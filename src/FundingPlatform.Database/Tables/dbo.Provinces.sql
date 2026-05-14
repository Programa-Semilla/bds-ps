CREATE TABLE [dbo].[Provinces]
(
    [Id]    INT            IDENTITY(1,1) NOT NULL,
    [Code]  CHAR(2)        NOT NULL,
    [Name]  NVARCHAR(40)   NOT NULL,

    CONSTRAINT [PK_Provinces] PRIMARY KEY CLUSTERED ([Id]),
    -- Spec 021 / data-model.md — INE/TSE province codes 01..07.
    CONSTRAINT [UX_Provinces_Code] UNIQUE ([Code])
);
GO
