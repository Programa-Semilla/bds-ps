CREATE TABLE [dbo].[Districts]
(
    [Id]        INT           IDENTITY(1,1) NOT NULL,
    [CantonId]  INT           NOT NULL,
    [Code]      CHAR(8)       NOT NULL,          -- 'PP_CC_DD' — extends the cantón 'PP_CC' scheme.
    [Name]      NVARCHAR(80)  NOT NULL,

    CONSTRAINT [PK_Districts] PRIMARY KEY CLUSTERED ([Id]),
    -- Spec 025 / data-model.md — composite code = province (2) + '_' + cantón index (2)
    -- + '_' + distrito index (2), e.g. San José/Carmen = "01_01_01".
    CONSTRAINT [UX_Districts_Code] UNIQUE ([Code]),
    CONSTRAINT [FK_Districts_Cantons]
        FOREIGN KEY ([CantonId]) REFERENCES [dbo].[Cantons] ([Id]) ON DELETE NO ACTION
);
GO

-- Covers the cascade query "list distritos for the selected cantón".
CREATE NONCLUSTERED INDEX [IX_Districts_CantonId]
    ON [dbo].[Districts] ([CantonId]);
GO
