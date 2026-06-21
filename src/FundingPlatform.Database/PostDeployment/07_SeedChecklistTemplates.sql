/*
    Post-Deployment include: 07_SeedChecklistTemplates.sql
    Spec 040 / D4 / D13 — seed one default, active checklist template that applies to
    Both stages (Reviewer + Auditor), plus a handful of es-CR verification items. This
    guarantees the reviewer send-to-audit and auditor generation gates resolve to a
    non-empty active template out of the box (FR-002 made unambiguous), before any admin
    configures stage-specific templates.

    Idempotent: guarded by template Name. Children are inserted under the resolved
    template id via SCOPE_IDENTITY() (the spec-035 ImpactTemplate seed pattern) only when
    the template row is freshly created.
*/

DECLARE @TemplateName NVARCHAR(200) = N'Lista de verificación predeterminada';

IF NOT EXISTS (SELECT 1 FROM [dbo].[ChecklistTemplates] WHERE [Name] = @TemplateName)
BEGIN
    INSERT INTO [dbo].[ChecklistTemplates]
        ([Name], [Description], [AppliesToStage], [IsActive], [CreatedAtUtc], [CreatedByUserId])
    VALUES
        (@TemplateName,
         N'Lista de verificación predeterminada aplicable a la etapa de revisión y de auditoría.',
         CAST(3 AS TINYINT), -- ChecklistStage.Both
         1,
         GETUTCDATE(),
         N'system');

    DECLARE @TemplateId INT = CAST(SCOPE_IDENTITY() AS INT);

    INSERT INTO [dbo].[ChecklistTemplateItems]
        ([ChecklistTemplateId], [Text], [DisplayOrder], [IsRequired], [IsActive])
    VALUES
        (@TemplateId, N'La documentación de la solicitud está completa y es legible.',          1, 1, 1),
        (@TemplateId, N'Cada ítem cuenta con cotizaciones válidas y vigentes.',                 2, 1, 1),
        (@TemplateId, N'Los proveedores tienen su situación regulatoria al día.',               3, 1, 1),
        (@TemplateId, N'Los montos y las conversiones de moneda son correctos.',                4, 1, 1),
        (@TemplateId, N'Las justificaciones de impacto son coherentes con los ítems aprobados.', 5, 1, 1);
END
GO
