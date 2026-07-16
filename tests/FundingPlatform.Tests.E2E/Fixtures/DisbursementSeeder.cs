using Microsoft.Data.SqlClient;

namespace FundingPlatform.Tests.E2E.Fixtures;

/// <summary>
/// Spec 045 — E2E helper to pre-seed a deterministic Allocation ledger entry so the
/// disbursement scenarios use legible, quickstart-matching figures (₡85,800 / ₡1,000,000)
/// independent of the tiny seeded quotation total. This is exactly the snapshot the first
/// real Record would post, with a controlled amount; the real compute→snapshot equality is
/// covered by the <c>DisbursementLedgerTests</c> integration test.
/// </summary>
public static class DisbursementSeeder
{
    /// <summary>Inserts a single Allocation ledger entry (EntryType=0) for the application,
    /// so <c>RecordAsync</c> and the balance projection use this amount as the ceiling.</summary>
    public static async Task SeedAllocationAsync(
        string connectionString, int applicationId, decimal amount, string byUserEmail)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        var userId = await GetUserIdByEmailAsync(conn, byUserEmail);

        const string sql = @"
IF NOT EXISTS (SELECT 1 FROM dbo.DisbursementLedgerEntries WHERE ApplicationId = @appId AND EntryType = 0)
    INSERT INTO dbo.DisbursementLedgerEntries (ApplicationId, EntryType, Amount, DisbursementId, PostedByUserId, PostedAtUtc)
    VALUES (@appId, 0, @amount, NULL, @userId, SYSUTCDATETIME());";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@appId", applicationId);
        cmd.Parameters.AddWithValue("@amount", amount);
        cmd.Parameters.AddWithValue("@userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<string> GetUserIdByEmailAsync(SqlConnection conn, string email)
    {
        const string sql = "SELECT Id FROM dbo.AspNetUsers WHERE Email = @email;";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@email", email);
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString() ?? throw new InvalidOperationException($"User '{email}' not found for allocation seed.");
    }
}
