-- 014-azure-blob-storage migration helper.
-- Copies the existing absolute-path columns into LegacyPath where LegacyPath IS NULL,
-- so the one-shot migration tool (US4 / FR-024) can scan rows and re-upload files
-- under the canonical Object Key naming convention.
--
-- Idempotent: re-running this script is safe; it only fills LegacyPath when NULL.

PRINT N'[Spec 014] Backfilling LegacyPath columns from existing StoragePath values...';

UPDATE dbo.SignedUploads
SET    LegacyPath = StoragePath
WHERE  LegacyPath IS NULL
   AND StoragePath IS NOT NULL;

UPDATE dbo.FundingAgreements
SET    LegacyPath = StoragePath
WHERE  LegacyPath IS NULL
   AND StoragePath IS NOT NULL;

UPDATE dbo.Documents
SET    LegacyPath = StoragePath
WHERE  LegacyPath IS NULL
   AND StoragePath IS NOT NULL;

PRINT N'[Spec 014] LegacyPath backfill complete.';
