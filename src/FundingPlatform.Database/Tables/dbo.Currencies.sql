-- Spec 015 — multi-currency quotes.
-- Two-row catalog (CRC, USD) seeded by SeedData.sql post-deploy. CRC is the
-- permanent base currency; the CHECK + filtered unique index pin the invariants.
CREATE TABLE [dbo].[Currencies]
(
    [Code]              CHAR(3)         NOT NULL,
    [Symbol]            NVARCHAR(8)     NOT NULL,
    [DisplayName]       NVARCHAR(64)    NOT NULL,
    [DecimalPrecision]  TINYINT         NOT NULL CONSTRAINT [DF_Currencies_DecimalPrecision] DEFAULT (2),
    [IsEnabled]         BIT             NOT NULL CONSTRAINT [DF_Currencies_IsEnabled] DEFAULT (1),
    [IsBaseCurrency]    BIT             NOT NULL CONSTRAINT [DF_Currencies_IsBaseCurrency] DEFAULT (0),
    [DisplayOrder]      SMALLINT        NOT NULL,
    [RowVersion]        ROWVERSION      NOT NULL,

    CONSTRAINT [PK_Currencies] PRIMARY KEY CLUSTERED ([Code]),
    -- Base currency must always remain enabled (FR-002, FR-003).
    CONSTRAINT [CK_Currencies_BaseAlwaysEnabled]
        CHECK ([IsBaseCurrency] = 0 OR [IsEnabled] = 1)
);
GO

-- Exactly one row may carry IsBaseCurrency = 1 (CRC).
CREATE UNIQUE INDEX [UQ_Currencies_OneBase]
    ON [dbo].[Currencies] ([IsBaseCurrency])
    WHERE [IsBaseCurrency] = 1;
GO
