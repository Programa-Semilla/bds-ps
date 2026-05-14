CREATE TABLE [dbo].[Cantons]
(
    [Id]          INT            IDENTITY(1,1) NOT NULL,
    [ProvinceId]  INT            NOT NULL,
    [Code]        CHAR(5)        NOT NULL,
    [Name]        NVARCHAR(60)   NOT NULL,

    CONSTRAINT [PK_Cantons] PRIMARY KEY CLUSTERED ([Id]),
    -- Spec 021 / data-model.md — composite code = province (2) + '_' + canton index (2),
    -- e.g. San José/Acosta = "01_05".
    CONSTRAINT [UX_Cantons_Code] UNIQUE ([Code]),
    CONSTRAINT [FK_Cantons_Provinces]
        FOREIGN KEY ([ProvinceId]) REFERENCES [dbo].[Provinces] ([Id]) ON DELETE NO ACTION
);
GO

-- Covers the cascade query "list cantones for the selected province".
CREATE NONCLUSTERED INDEX [IX_Cantons_ProvinceId]
    ON [dbo].[Cantons] ([ProvinceId]);
GO
