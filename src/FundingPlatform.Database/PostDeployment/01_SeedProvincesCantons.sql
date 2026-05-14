/*
    Post-Deployment include: 01_SeedProvincesCantons.sql
    Spec 021 / FR-014 — Seed the canonical Costa Rica Province + Cantón catalog.

    Canonical source: TSE/INEC división territorial administrativa (vigente tras los
    decretos N.º 41085-MGP del 2018 y N.º 41548-MGP del 2019 que crearon, respectivamente,
    el cantón Río Cuarto (Alajuela, código 02-16) y Monteverde (Puntarenas, código 06-13)).
    Códigos = "PP_CC" donde PP = provincia (01..07) y CC = índice del cantón (01..N) por
    provincia. Idempotente vía MERGE; permite re-ejecución sin duplicar.

    Total = 7 provincias + 84 cantones (incluye los 2 cantones nuevos post-2018).
*/

-- =============================================================================
-- Provinces — 7 rows (INE/TSE codes 01..07)
-- =============================================================================
MERGE INTO [dbo].[Provinces] AS tgt
USING (VALUES
    ('01', N'San José'),
    ('02', N'Alajuela'),
    ('03', N'Cartago'),
    ('04', N'Heredia'),
    ('05', N'Guanacaste'),
    ('06', N'Puntarenas'),
    ('07', N'Limón')
) AS src ([Code], [Name])
ON tgt.[Code] = src.[Code]
WHEN NOT MATCHED THEN
    INSERT ([Code], [Name]) VALUES (src.[Code], src.[Name])
WHEN MATCHED AND tgt.[Name] <> src.[Name] THEN
    UPDATE SET tgt.[Name] = src.[Name];

-- Resolve province ids into local variables (idempotent — same ids each run because
-- the unique Code column is the key).
DECLARE @P_SanJose    INT = (SELECT Id FROM [dbo].[Provinces] WHERE [Code] = '01');
DECLARE @P_Alajuela   INT = (SELECT Id FROM [dbo].[Provinces] WHERE [Code] = '02');
DECLARE @P_Cartago    INT = (SELECT Id FROM [dbo].[Provinces] WHERE [Code] = '03');
DECLARE @P_Heredia    INT = (SELECT Id FROM [dbo].[Provinces] WHERE [Code] = '04');
DECLARE @P_Guanacaste INT = (SELECT Id FROM [dbo].[Provinces] WHERE [Code] = '05');
DECLARE @P_Puntarenas INT = (SELECT Id FROM [dbo].[Provinces] WHERE [Code] = '06');
DECLARE @P_Limon      INT = (SELECT Id FROM [dbo].[Provinces] WHERE [Code] = '07');

-- =============================================================================
-- Cantons — 84 rows.
--   San José (01) — 20
--   Alajuela (02) — 16 (incluye Río Cuarto, decreto N.º 41085-MGP, 2018)
--   Cartago (03) — 8
--   Heredia (04) — 10
--   Guanacaste (05) — 11
--   Puntarenas (06) — 13 (incluye Monteverde, decreto N.º 41548-MGP, 2019)
--   Limón (07) — 6
-- =============================================================================
MERGE INTO [dbo].[Cantons] AS tgt
USING (VALUES
    -- San José
    (@P_SanJose, '01_01', N'San José'),
    (@P_SanJose, '01_02', N'Escazú'),
    (@P_SanJose, '01_03', N'Desamparados'),
    (@P_SanJose, '01_04', N'Puriscal'),
    (@P_SanJose, '01_05', N'Tarrazú'),
    (@P_SanJose, '01_06', N'Aserrí'),
    (@P_SanJose, '01_07', N'Mora'),
    (@P_SanJose, '01_08', N'Goicoechea'),
    (@P_SanJose, '01_09', N'Santa Ana'),
    (@P_SanJose, '01_10', N'Alajuelita'),
    (@P_SanJose, '01_11', N'Vázquez de Coronado'),
    (@P_SanJose, '01_12', N'Acosta'),
    (@P_SanJose, '01_13', N'Tibás'),
    (@P_SanJose, '01_14', N'Moravia'),
    (@P_SanJose, '01_15', N'Montes de Oca'),
    (@P_SanJose, '01_16', N'Turrubares'),
    (@P_SanJose, '01_17', N'Dota'),
    (@P_SanJose, '01_18', N'Curridabat'),
    (@P_SanJose, '01_19', N'Pérez Zeledón'),
    (@P_SanJose, '01_20', N'León Cortés Castro'),
    -- Alajuela
    (@P_Alajuela, '02_01', N'Alajuela'),
    (@P_Alajuela, '02_02', N'San Ramón'),
    (@P_Alajuela, '02_03', N'Grecia'),
    (@P_Alajuela, '02_04', N'San Mateo'),
    (@P_Alajuela, '02_05', N'Atenas'),
    (@P_Alajuela, '02_06', N'Naranjo'),
    (@P_Alajuela, '02_07', N'Palmares'),
    (@P_Alajuela, '02_08', N'Poás'),
    (@P_Alajuela, '02_09', N'Orotina'),
    (@P_Alajuela, '02_10', N'San Carlos'),
    (@P_Alajuela, '02_11', N'Zarcero'),
    (@P_Alajuela, '02_12', N'Sarchí'),
    (@P_Alajuela, '02_13', N'Upala'),
    (@P_Alajuela, '02_14', N'Los Chiles'),
    (@P_Alajuela, '02_15', N'Guatuso'),
    (@P_Alajuela, '02_16', N'Río Cuarto'),
    -- Cartago
    (@P_Cartago, '03_01', N'Cartago'),
    (@P_Cartago, '03_02', N'Paraíso'),
    (@P_Cartago, '03_03', N'La Unión'),
    (@P_Cartago, '03_04', N'Jiménez'),
    (@P_Cartago, '03_05', N'Turrialba'),
    (@P_Cartago, '03_06', N'Alvarado'),
    (@P_Cartago, '03_07', N'Oreamuno'),
    (@P_Cartago, '03_08', N'El Guarco'),
    -- Heredia
    (@P_Heredia, '04_01', N'Heredia'),
    (@P_Heredia, '04_02', N'Barva'),
    (@P_Heredia, '04_03', N'Santo Domingo'),
    (@P_Heredia, '04_04', N'Santa Bárbara'),
    (@P_Heredia, '04_05', N'San Rafael'),
    (@P_Heredia, '04_06', N'San Isidro'),
    (@P_Heredia, '04_07', N'Belén'),
    (@P_Heredia, '04_08', N'Flores'),
    (@P_Heredia, '04_09', N'San Pablo'),
    (@P_Heredia, '04_10', N'Sarapiquí'),
    -- Guanacaste
    (@P_Guanacaste, '05_01', N'Liberia'),
    (@P_Guanacaste, '05_02', N'Nicoya'),
    (@P_Guanacaste, '05_03', N'Santa Cruz'),
    (@P_Guanacaste, '05_04', N'Bagaces'),
    (@P_Guanacaste, '05_05', N'Carrillo'),
    (@P_Guanacaste, '05_06', N'Cañas'),
    (@P_Guanacaste, '05_07', N'Abangares'),
    (@P_Guanacaste, '05_08', N'Tilarán'),
    (@P_Guanacaste, '05_09', N'Nandayure'),
    (@P_Guanacaste, '05_10', N'La Cruz'),
    (@P_Guanacaste, '05_11', N'Hojancha'),
    -- Puntarenas
    (@P_Puntarenas, '06_01', N'Puntarenas'),
    (@P_Puntarenas, '06_02', N'Esparza'),
    (@P_Puntarenas, '06_03', N'Buenos Aires'),
    (@P_Puntarenas, '06_04', N'Montes de Oro'),
    (@P_Puntarenas, '06_05', N'Osa'),
    (@P_Puntarenas, '06_06', N'Quepos'),
    (@P_Puntarenas, '06_07', N'Golfito'),
    (@P_Puntarenas, '06_08', N'Coto Brus'),
    (@P_Puntarenas, '06_09', N'Parrita'),
    (@P_Puntarenas, '06_10', N'Corredores'),
    (@P_Puntarenas, '06_11', N'Garabito'),
    (@P_Puntarenas, '06_12', N'Monteverde'),
    (@P_Puntarenas, '06_13', N'Puerto Jiménez'),
    -- Limón
    (@P_Limon, '07_01', N'Limón'),
    (@P_Limon, '07_02', N'Pococí'),
    (@P_Limon, '07_03', N'Siquirres'),
    (@P_Limon, '07_04', N'Talamanca'),
    (@P_Limon, '07_05', N'Matina'),
    (@P_Limon, '07_06', N'Guácimo')
) AS src ([ProvinceId], [Code], [Name])
ON tgt.[Code] = src.[Code]
WHEN NOT MATCHED THEN
    INSERT ([ProvinceId], [Code], [Name])
    VALUES (src.[ProvinceId], src.[Code], src.[Name])
WHEN MATCHED AND (tgt.[Name] <> src.[Name] OR tgt.[ProvinceId] <> src.[ProvinceId]) THEN
    UPDATE SET tgt.[Name] = src.[Name], tgt.[ProvinceId] = src.[ProvinceId];
GO
