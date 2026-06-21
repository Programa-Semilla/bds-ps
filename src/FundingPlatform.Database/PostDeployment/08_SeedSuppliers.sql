/*
    Post-Deployment include: 08_SeedSuppliers.sql
    Sample suppliers with specific spec-038 regulatory profiles for exercising the
    recommendation algorithm (spec 039) + the CCSS "sin inscripción" hard block:

      1-111-111111 / 2-222-222222 / 3-333-333333 — all regulatories al día, NOT pyme
      5-555-555555 — all regulatories al día, IS pyme
      6-666-666666 — al día except CCSS "sin inscripción" (the per-item approval block)

    Status codes (enum byte values, see Domain/Enums): Hacienda al día = 2 (AlDia);
    CCSS al día = 2 (AlDia), CCSS sin inscripción = 1 (SinInscripcion); SICOP sin
    sanciones = 2 (SinSanciones). RegulatoryReviewSource Manual = 1.

    Each supplier is Verified (VerificationStatus = 2) and gets a default branch anchored
    to a real provincia/cantón/distrito (the first seeded district chain). Idempotent by
    LegalId; safe to re-run on every deploy.
*/

DECLARE @DistrictId INT = (SELECT TOP 1 [Id] FROM [dbo].[Districts] ORDER BY [Id]);
DECLARE @CantonId   INT = (SELECT [CantonId]   FROM [dbo].[Districts] WHERE [Id] = @DistrictId);
DECLARE @ProvinceId INT = (SELECT [ProvinceId] FROM [dbo].[Cantons]   WHERE [Id] = @CantonId);

-- 1. Suppliers (idempotent by LegalId).
INSERT INTO [dbo].[Suppliers]
    ([LegalId], [Name],
     [HaciendaStatus], [HaciendaLastReviewedAt], [HaciendaLastReviewedSource],
     [CcssStatus],     [CcssLastReviewedAt],     [CcssLastReviewedSource],
     [SicopStatus],    [SicopLastReviewedAt],    [SicopLastReviewedSource],
     [IsPmeOrPyme], [VerificationStatus])
SELECT v.LegalId, v.Name,
       v.H, GETUTCDATE(), 1,
       v.C, GETUTCDATE(), 1,
       v.S, GETUTCDATE(), 1,
       v.P, 2
FROM (VALUES
    (N'1-111-111111', N'Proveedora Uno S.A.',           CAST(2 AS TINYINT), CAST(2 AS TINYINT), CAST(2 AS TINYINT), CAST(0 AS BIT)),
    (N'2-222-222222', N'Suministros Dos Ltda.',         CAST(2 AS TINYINT), CAST(2 AS TINYINT), CAST(2 AS TINYINT), CAST(0 AS BIT)),
    (N'3-333-333333', N'Comercial Tres S.A.',           CAST(2 AS TINYINT), CAST(2 AS TINYINT), CAST(2 AS TINYINT), CAST(0 AS BIT)),
    (N'5-555-555555', N'PYME Cinco S.A.',               CAST(2 AS TINYINT), CAST(2 AS TINYINT), CAST(2 AS TINYINT), CAST(1 AS BIT)),
    (N'6-666-666666', N'Seis Sin Inscripción CCSS S.A.', CAST(2 AS TINYINT), CAST(1 AS TINYINT), CAST(2 AS TINYINT), CAST(0 AS BIT))
) AS v(LegalId, Name, H, C, S, P)
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[Suppliers] s WHERE s.[LegalId] = v.LegalId);

-- 2. A default branch per seeded supplier (only when the location catalog is present).
IF @DistrictId IS NOT NULL
BEGIN
    INSERT INTO [dbo].[SupplierBranches]
        ([SupplierId], [BranchName], [ContactPersonName], [Email], [Phone], [AddressLine],
         [ProvinceId], [CantonId], [DistrictId], [IsDefault])
    SELECT s.[Id], N'Sucursal principal', c.ContactName, c.Email, c.Phone, N'Dirección de prueba, 100 m sur',
           @ProvinceId, @CantonId, @DistrictId, 1
    FROM [dbo].[Suppliers] s
    INNER JOIN (VALUES
        (N'1-111-111111', N'Juan Vargas',   N'ventas@uno.test',    N'2222-1111'),
        (N'2-222-222222', N'Marta Soto',    N'info@dos.test',      N'2222-2222'),
        (N'3-333-333333', N'Luis Mora',     N'contacto@tres.test', N'2222-3333'),
        (N'5-555-555555', N'Ana Rojas',     N'pyme@cinco.test',    N'2222-5555'),
        (N'6-666-666666', N'Pedro Jiménez', N'seis@seis.test',     N'2222-6666')
    ) AS c(LegalId, ContactName, Email, Phone) ON c.LegalId = s.[LegalId]
    WHERE NOT EXISTS (SELECT 1 FROM [dbo].[SupplierBranches] b WHERE b.[SupplierId] = s.[Id] AND b.[IsDefault] = 1);
END
GO
