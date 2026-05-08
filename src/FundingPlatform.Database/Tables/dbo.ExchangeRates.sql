-- Spec 015 — administrator-published reference rates. Immutable once snapshotted
-- by a Quotation. Buy direction is what the MVP applies; Sell is captured for
-- audit only (data-model.md). The unique index on (Source, Target, EffectiveAt)
-- enforces FR-007 (no duplicate timestamps for the same pair).
CREATE TABLE [dbo].[ExchangeRates]
(
    [Id]                    UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_ExchangeRates_Id] DEFAULT (NEWSEQUENTIALID()),
    [SourceCurrencyCode]    CHAR(3)          NOT NULL,
    [TargetCurrencyCode]    CHAR(3)          NOT NULL,
    [BuyRate]               DECIMAL(18, 6)   NOT NULL,
    [SellRate]              DECIMAL(18, 6)   NOT NULL,
    [EffectiveAtUtc]        DATETIME2(3)     NOT NULL,
    [CreatedByUserId]       NVARCHAR(450)    NOT NULL,
    [CreatedAtUtc]          DATETIME2(3)     NOT NULL CONSTRAINT [DF_ExchangeRates_CreatedAtUtc] DEFAULT (SYSUTCDATETIME()),
    [IsUsed]                BIT              NOT NULL CONSTRAINT [DF_ExchangeRates_IsUsed] DEFAULT (0),
    [RowVersion]            ROWVERSION       NOT NULL,

    CONSTRAINT [PK_ExchangeRates] PRIMARY KEY CLUSTERED ([Id]),

    CONSTRAINT [CK_ExchangeRates_PositiveBuy]   CHECK ([BuyRate]  > 0),
    CONSTRAINT [CK_ExchangeRates_PositiveSell]  CHECK ([SellRate] > 0),
    CONSTRAINT [CK_ExchangeRates_DistinctPair]  CHECK ([SourceCurrencyCode] <> [TargetCurrencyCode]),

    CONSTRAINT [FK_ExchangeRates_Currencies_Source]
        FOREIGN KEY ([SourceCurrencyCode]) REFERENCES [dbo].[Currencies] ([Code]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ExchangeRates_Currencies_Target]
        FOREIGN KEY ([TargetCurrencyCode]) REFERENCES [dbo].[Currencies] ([Code]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ExchangeRates_AspNetUsers]
        FOREIGN KEY ([CreatedByUserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
);
GO

-- FR-007: at most one rate row per (pair, effectiveAt). Concurrent admin saves
-- collide here as a duplicate-key violation (2627/2601), translated by the
-- repository into a user-facing validation error.
CREATE UNIQUE INDEX [UQ_ExchangeRates_PairAt]
    ON [dbo].[ExchangeRates] ([SourceCurrencyCode], [TargetCurrencyCode], [EffectiveAtUtc]);
GO

-- Latest-rate lookup support (data-model.md Read models).
CREATE INDEX [IX_ExchangeRates_PairEffectiveAtDesc]
    ON [dbo].[ExchangeRates] ([SourceCurrencyCode], [TargetCurrencyCode], [EffectiveAtUtc] DESC);
GO
