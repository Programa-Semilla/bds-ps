using Azure.Storage.Blobs;
using Microsoft.Data.SqlClient;

namespace FundingPlatform.Tests.E2E.Fixtures;

/// <summary>
/// Seeds FundingAgreement / SignedUpload / SigningReviewDecision rows directly via SQL,
/// bypassing the Syncfusion-backed PDF generation path. Used by E2E tests that need a
/// ResponseFinalized-plus-agreement or AgreementExecuted starting state but cannot rely
/// on a Syncfusion license being present in the test environment.
///
/// Spec 014: rows reference a canonical <c>BlobKey</c> in the format
/// <c>{container}/{owner-segment}/{entity-id}/{suffix}.pdf</c>. When a
/// <see cref="BlobServiceClient"/> is provided, a placeholder PDF is uploaded to that
/// key inside Azurite so download paths backed by <c>IObjectStorage</c> resolve.
/// </summary>
public static class FundingAgreementSeeder
{
    private const string GeneratedArtifactsContainer = "generated-artifacts";
    private const string SignedFundingAgreementsContainer = "signed-funding-agreements";

    private static readonly byte[] PlaceholderPdfBytes =
        System.Text.Encoding.UTF8.GetBytes("%PDF-1.4\nseeded placeholder\n%%EOF\n");

    /// <summary>
    /// Spec 043 — marks every supplier selected by the application's items as
    /// regulatory-fresh: all three required statuses set to a favorable value with
    /// <c>LastReviewedAt = now - daysAgo</c>. With the default <paramref name="daysAgo"/>
    /// of 1 the suppliers clear the freshness gate; pass a value &gt; the window
    /// (default 30) to force them stale-by-date (exercising the date-formatted block
    /// message). Statuses: Hacienda <c>AlDia=2</c>, CCSS <c>AlDia=2</c>, SICOP
    /// <c>SinSanciones=2</c>; source <c>Api=2</c>; reviewer left null (no FK needed).
    /// </summary>
    public static async Task SetSelectedSuppliersRegulatoryAsync(
        string connectionString, int applicationId, int daysAgo = 1)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        const string sql = @"
UPDATE s SET
    HaciendaStatus = 2, HaciendaLastReviewedAt = @at, HaciendaLastReviewedSource = 2,
    CcssStatus = 2,     CcssLastReviewedAt = @at,     CcssLastReviewedSource = 2,
    SicopStatus = 2,    SicopLastReviewedAt = @at,    SicopLastReviewedSource = 2,
    UpdatedAt = @now
FROM dbo.Suppliers s
WHERE s.Id IN (
    SELECT i.SelectedSupplierId FROM dbo.Items i
    WHERE i.ApplicationId = @appId AND i.SelectedSupplierId IS NOT NULL);";

        using var cmd = new SqlCommand(sql, conn);
        var now = DateTime.UtcNow;
        cmd.Parameters.AddWithValue("@at", now.AddDays(-daysAgo));
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@appId", applicationId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Spec 043 — convenience: mark the application's selected suppliers fresh
    /// (reviewed today) so the regulatory-freshness gate permits the auditor advance.</summary>
    public static Task SetSelectedSuppliersRegulatoryFreshAsync(string connectionString, int applicationId)
        => SetSelectedSuppliersRegulatoryAsync(connectionString, applicationId, daysAgo: 1);

    /// <summary>
    /// Inserts a FundingAgreement row for the given application (if none exists yet)
    /// and optionally uploads a placeholder PDF to Azurite at the canonical blob key.
    /// Returns the persisted blob key.
    /// </summary>
    public static async Task<string> SeedGeneratedAgreementAsync(
        string connectionString,
        int applicationId,
        string generatedByUserEmail,
        BlobServiceClient? blobs = null)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        // Idempotent: if an agreement already exists for this application, return its key.
        var existingKey = await TryGetAgreementBlobKeyAsync(conn, applicationId);
        if (existingKey is not null) return existingKey;

        var userId = await GetUserIdByEmailAsync(conn, generatedByUserEmail);
        var applicantUserId = await GetApplicantUserIdByApplicationAsync(conn, applicationId);

        var blobKey = BuildBlobKey(
            container: GeneratedArtifactsContainer,
            ownerUserId: applicantUserId,
            entityId: applicationId.ToString());

        await TryUploadPlaceholderAsync(blobs, GeneratedArtifactsContainer, blobKey);

        const string sql = @"
INSERT INTO dbo.FundingAgreements
    (ApplicationId, FileName, ContentType, Size, BlobKey, GeneratedAtUtc, GeneratedByUserId, GeneratedVersion)
VALUES
    (@appId, @fileName, @contentType, @size, @blobKey, @generatedAt, @userId, 1);
SELECT SCOPE_IDENTITY();";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@appId", applicationId);
        cmd.Parameters.AddWithValue("@fileName", $"FundingAgreement-{applicationId}.pdf");
        cmd.Parameters.AddWithValue("@contentType", "application/pdf");
        cmd.Parameters.AddWithValue("@size", PlaceholderPdfBytes.LongLength);
        cmd.Parameters.AddWithValue("@blobKey", blobKey);
        cmd.Parameters.AddWithValue("@generatedAt", DateTime.UtcNow);
        cmd.Parameters.AddWithValue("@userId", userId);

        await cmd.ExecuteScalarAsync();
        return blobKey;
    }

    /// <summary>
    /// Extends <see cref="SeedGeneratedAgreementAsync"/> by inserting an Approved signed
    /// upload with a decision row and flipping the application state to AgreementExecuted (6).
    /// Returns the signed-upload blob key.
    /// </summary>
    public static async Task<string> SeedExecutedAgreementAsync(
        string connectionString,
        int applicationId,
        string generatedByUserEmail,
        string applicantUserEmail,
        string reviewerUserEmail,
        BlobServiceClient? blobs = null)
    {
        await SeedGeneratedAgreementAsync(connectionString, applicationId, generatedByUserEmail, blobs);

        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        var applicantUserId = await GetUserIdByEmailAsync(conn, applicantUserEmail);
        var reviewerUserId = await GetUserIdByEmailAsync(conn, reviewerUserEmail);
        var agreementId = await GetFundingAgreementIdAsync(conn, applicationId);

        var signedBlobKey = BuildBlobKey(
            container: SignedFundingAgreementsContainer,
            ownerUserId: applicantUserId,
            entityId: agreementId.ToString());

        await TryUploadPlaceholderAsync(blobs, SignedFundingAgreementsContainer, signedBlobKey);

        // Status = 4 (Approved) per SignedUploadStatus enum.
        const string insertUploadSql = @"
INSERT INTO dbo.SignedUploads
    (FundingAgreementId, UploaderUserId, GeneratedVersionAtUpload, FileName, ContentType, Size, BlobKey, UploadedAtUtc, Status)
VALUES
    (@agreementId, @uploaderUserId, 1, @fileName, 'application/pdf', @size, @blobKey, @uploadedAt, 4);
SELECT SCOPE_IDENTITY();";

        int uploadId;
        using (var cmd = new SqlCommand(insertUploadSql, conn))
        {
            cmd.Parameters.AddWithValue("@agreementId", agreementId);
            cmd.Parameters.AddWithValue("@uploaderUserId", applicantUserId);
            cmd.Parameters.AddWithValue("@fileName", $"signed-{applicationId}.pdf");
            cmd.Parameters.AddWithValue("@size", PlaceholderPdfBytes.LongLength);
            cmd.Parameters.AddWithValue("@blobKey", signedBlobKey);
            cmd.Parameters.AddWithValue("@uploadedAt", DateTime.UtcNow);
            var scalar = await cmd.ExecuteScalarAsync();
            uploadId = Convert.ToInt32(scalar);
        }

        const string insertDecisionSql = @"
INSERT INTO dbo.SigningReviewDecisions
    (SignedUploadId, Outcome, ReviewerUserId, Comment, DecidedAtUtc)
VALUES
    (@uploadId, 0, @reviewerUserId, NULL, @decidedAt);";

        using (var cmd = new SqlCommand(insertDecisionSql, conn))
        {
            cmd.Parameters.AddWithValue("@uploadId", uploadId);
            cmd.Parameters.AddWithValue("@reviewerUserId", reviewerUserId);
            cmd.Parameters.AddWithValue("@decidedAt", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync();
        }

        const string updateStateSql = @"
UPDATE dbo.Applications SET State = 6, UpdatedAt = @now WHERE Id = @appId;";

        using (var cmd = new SqlCommand(updateStateSql, conn))
        {
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@appId", applicationId);
            await cmd.ExecuteNonQueryAsync();
        }

        return signedBlobKey;
    }

    /// <summary>
    /// Spec 016 — seeds a Pending signed upload for an application so the
    /// reviewer signing inbox has a row to surface (or not, when scoped out).
    /// Inserts the FundingAgreement row first (idempotent) and then a
    /// SignedUpload with Status = 0 (Pending). Returns the signed-upload's
    /// BlobKey for cleanup.
    /// </summary>
    public static async Task<string> SeedPendingSignedUploadAsync(
        string connectionString,
        int applicationId,
        string generatedByUserEmail,
        string applicantUserEmail,
        BlobServiceClient? blobs = null)
    {
        await SeedGeneratedAgreementAsync(connectionString, applicationId, generatedByUserEmail, blobs);

        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        var applicantUserId = await GetUserIdByEmailAsync(conn, applicantUserEmail);
        var agreementId = await GetFundingAgreementIdAsync(conn, applicationId);

        var signedBlobKey = BuildBlobKey(
            container: SignedFundingAgreementsContainer,
            ownerUserId: applicantUserId,
            entityId: agreementId.ToString());

        await TryUploadPlaceholderAsync(blobs, SignedFundingAgreementsContainer, signedBlobKey);

        // Status = 0 (Pending) per SignedUploadStatus enum.
        const string insertUploadSql = @"
INSERT INTO dbo.SignedUploads
    (FundingAgreementId, UploaderUserId, GeneratedVersionAtUpload, FileName, ContentType, Size, BlobKey, UploadedAtUtc, Status)
VALUES
    (@agreementId, @uploaderUserId, 1, @fileName, 'application/pdf', @size, @blobKey, @uploadedAt, 0);";

        using var cmd = new SqlCommand(insertUploadSql, conn);
        cmd.Parameters.AddWithValue("@agreementId", agreementId);
        cmd.Parameters.AddWithValue("@uploaderUserId", applicantUserId);
        cmd.Parameters.AddWithValue("@fileName", $"signed-pending-{applicationId}.pdf");
        cmd.Parameters.AddWithValue("@size", PlaceholderPdfBytes.LongLength);
        cmd.Parameters.AddWithValue("@blobKey", signedBlobKey);
        cmd.Parameters.AddWithValue("@uploadedAt", DateTime.UtcNow);
        await cmd.ExecuteNonQueryAsync();

        return signedBlobKey;
    }

    /// <summary>
    /// Spec 040 / T021 — flips an application that has reached ResponseFinalized (5) to
    /// PendingAudit (7) so it appears in the auditor inbox, and records a SentToAudit
    /// VersionHistory marker. No FundingAgreement is created (the auditor generates it).
    /// The reviewer-checklist completeness is irrelevant on this path (it is only
    /// evaluated at the live SendToAudit transition, which the seeder bypasses).
    /// </summary>
    public static async Task SeedPendingAuditApplicationAsync(
        string connectionString,
        int applicationId,
        string reviewerUserEmail)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        var reviewerUserId = await GetUserIdByEmailAsync(conn, reviewerUserEmail);

        const string updateSql = @"UPDATE dbo.Applications SET State = 7, UpdatedAt = @now WHERE Id = @appId;";
        using (var cmd = new SqlCommand(updateSql, conn))
        {
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@appId", applicationId);
            await cmd.ExecuteNonQueryAsync();
        }

        const string vhSql = @"
INSERT INTO dbo.VersionHistory (ApplicationId, UserId, Action, Details, Timestamp)
VALUES (@appId, @userId, 'SentToAudit', 'Enviado a auditoría (seed)', @now);";
        using (var cmd = new SqlCommand(vhSql, conn))
        {
            cmd.Parameters.AddWithValue("@appId", applicationId);
            cmd.Parameters.AddWithValue("@userId", reviewerUserId);
            cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Inserts placeholder FundingAgreement rows for every ResponseFinalized application
    /// (state = 5) that does not yet have one. Used by the SC-010-A empty-state test to
    /// neutralize queue rows seeded by sibling test classes (ApplicantResponseTests,
    /// FinalizeReviewTests) that share the same per-fixture SQL container.
    /// </summary>
    public static async Task ClearGenerateAgreementQueueAsync(
        string connectionString,
        BlobServiceClient? blobs = null)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        const string selectSql = @"
SELECT a.Id
FROM dbo.Applications a
LEFT JOIN dbo.FundingAgreements fa ON fa.ApplicationId = a.Id
WHERE a.State = 5 AND fa.Id IS NULL";

        var pendingIds = new List<int>();
        using (var cmd = new SqlCommand(selectSql, conn))
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                pendingIds.Add(reader.GetInt32(0));
            }
        }

        if (pendingIds.Count == 0) return;

        // Pick any existing user as the GeneratedByUserId (the SC-010-A test only
        // asserts the empty-state element, not who generated each placeholder).
        string anyUserId;
        using (var cmd = new SqlCommand("SELECT TOP 1 Id FROM dbo.AspNetUsers", conn))
        {
            var result = await cmd.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("No AspNetUsers row available to attribute placeholder agreements to.");
            anyUserId = (string)result;
        }

        const string insertSql = @"
INSERT INTO dbo.FundingAgreements
    (ApplicationId, FileName, ContentType, Size, BlobKey, GeneratedAtUtc, GeneratedByUserId, GeneratedVersion)
VALUES
    (@appId, @fileName, 'application/pdf', @size, @blobKey, @generatedAt, @userId, 1)";

        foreach (var appId in pendingIds)
        {
            // Owner segment falls back to the placeholder GeneratedBy user id when the
            // applicant user id is missing — this row only exists to neutralize queue
            // visibility, not to be downloaded, so the lookup is best-effort.
            var applicantUserId = await TryGetApplicantUserIdByApplicationAsync(conn, appId)
                ?? anyUserId;

            var blobKey = BuildBlobKey(
                container: GeneratedArtifactsContainer,
                ownerUserId: applicantUserId,
                entityId: appId.ToString());

            await TryUploadPlaceholderAsync(blobs, GeneratedArtifactsContainer, blobKey);

            using var cmd = new SqlCommand(insertSql, conn);
            cmd.Parameters.AddWithValue("@appId", appId);
            cmd.Parameters.AddWithValue("@fileName", $"FundingAgreement-{appId}.pdf");
            cmd.Parameters.AddWithValue("@size", PlaceholderPdfBytes.LongLength);
            cmd.Parameters.AddWithValue("@blobKey", blobKey);
            cmd.Parameters.AddWithValue("@generatedAt", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@userId", anyUserId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Builds a deterministic blob key in the FR-014 canonical shape:
    /// <c>{container}/applicants/{owner-user-id}/{entity-id}/{guid-N}.pdf</c>. All
    /// segments are lower-cased so the result round-trips through <c>ObjectKey.Parse</c>.
    /// </summary>
    private static string BuildBlobKey(string container, string ownerUserId, string entityId)
    {
        var owner = ownerUserId.Trim().ToLowerInvariant();
        var entity = entityId.Trim().ToLowerInvariant();
        var suffix = Guid.NewGuid().ToString("N");
        return $"{container}/applicants/{owner}/{entity}/{suffix}.pdf";
    }

    private static async Task TryUploadPlaceholderAsync(
        BlobServiceClient? blobs,
        string containerName,
        string blobKey)
    {
        if (blobs is null) return;

        var container = blobs.GetBlobContainerClient(containerName);
        await container.CreateIfNotExistsAsync();

        // Strip the leading container segment from the key — Azure SDK addresses blobs
        // by their name within the container, not by the full canonical key string.
        var prefix = containerName + "/";
        var blobName = blobKey.StartsWith(prefix, StringComparison.Ordinal)
            ? blobKey[prefix.Length..]
            : blobKey;

        var blob = container.GetBlobClient(blobName);
        using var stream = new MemoryStream(PlaceholderPdfBytes);
        await blob.UploadAsync(stream, overwrite: true);
    }

    private static async Task<string?> TryGetAgreementBlobKeyAsync(SqlConnection conn, int applicationId)
    {
        using var cmd = new SqlCommand(
            "SELECT BlobKey FROM dbo.FundingAgreements WHERE ApplicationId = @appId", conn);
        cmd.Parameters.AddWithValue("@appId", applicationId);
        var result = await cmd.ExecuteScalarAsync();
        return result is null || result == DBNull.Value ? null : (string)result;
    }

    private static async Task<string> GetUserIdByEmailAsync(SqlConnection conn, string email)
    {
        using var cmd = new SqlCommand(
            "SELECT Id FROM dbo.AspNetUsers WHERE NormalizedEmail = @email", conn);
        cmd.Parameters.AddWithValue("@email", email.ToUpperInvariant());
        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result == DBNull.Value)
            throw new InvalidOperationException($"User not found by email: {email}");
        return (string)result;
    }

    private static async Task<string> GetApplicantUserIdByApplicationAsync(SqlConnection conn, int applicationId)
    {
        var userId = await TryGetApplicantUserIdByApplicationAsync(conn, applicationId);
        if (userId is null)
            throw new InvalidOperationException($"Applicant user id not found for application {applicationId}");
        return userId;
    }

    private static async Task<string?> TryGetApplicantUserIdByApplicationAsync(SqlConnection conn, int applicationId)
    {
        using var cmd = new SqlCommand(
            @"SELECT ap.UserId
              FROM dbo.Applications a
              INNER JOIN dbo.Applicants ap ON ap.Id = a.ApplicantId
              WHERE a.Id = @appId", conn);
        cmd.Parameters.AddWithValue("@appId", applicationId);
        var result = await cmd.ExecuteScalarAsync();
        return result is null || result == DBNull.Value ? null : (string)result;
    }

    private static async Task<int> GetFundingAgreementIdAsync(SqlConnection conn, int applicationId)
    {
        using var cmd = new SqlCommand(
            "SELECT Id FROM dbo.FundingAgreements WHERE ApplicationId = @appId", conn);
        cmd.Parameters.AddWithValue("@appId", applicationId);
        var result = await cmd.ExecuteScalarAsync();
        if (result is null || result == DBNull.Value)
            throw new InvalidOperationException($"FundingAgreement not found for application {applicationId}");
        return Convert.ToInt32(result);
    }
}
