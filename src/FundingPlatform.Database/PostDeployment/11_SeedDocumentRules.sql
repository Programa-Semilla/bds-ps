/*
    Post-Deployment include: 11_SeedDocumentRules.sql
    Spec 047 / D5 — seed the global-default required-document rule set (CategoryId IS NULL) so a
    line whose Category has no rule falls back to a sensible default: Bank Receipt + Invoice +
    Signed Acceptance = Required (Credit Note / Refund Receipt / Other = not required, hence not
    seeded — RequiredTypes() filters IsRequired).

    Idempotent: guarded on the existence of the global-default set. Children are inserted under the
    resolved set id via SCOPE_IDENTITY() only when the set row is freshly created.
*/

IF NOT EXISTS (SELECT 1 FROM [dbo].[DocumentRuleSets] WHERE [CategoryId] IS NULL)
BEGIN
    INSERT INTO [dbo].[DocumentRuleSets] ([CategoryId]) VALUES (NULL);

    DECLARE @SetId INT = CAST(SCOPE_IDENTITY() AS INT);

    INSERT INTO [dbo].[DocumentRuleItems] ([DocumentRuleSetId], [EvidenceType], [IsRequired])
    VALUES
        (@SetId, CAST(0 AS TINYINT), 1),  -- EvidenceType.BankReceipt
        (@SetId, CAST(1 AS TINYINT), 1),  -- EvidenceType.Invoice
        (@SetId, CAST(2 AS TINYINT), 1);  -- EvidenceType.SignedAcceptance
END
GO
