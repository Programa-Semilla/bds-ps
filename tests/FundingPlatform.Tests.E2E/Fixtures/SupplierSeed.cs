using Microsoft.Data.SqlClient;

namespace FundingPlatform.Tests.E2E.Fixtures;

/// <summary>
/// Spec 038 — raw-SQL seed of a Verified provider (+ one default branch) so the
/// auditor compliance E2E flows have a provider to edit without driving the full
/// applicant supplier-add UI. Mirrors the raw-INSERT pattern used by
/// QuotationEditAfterReturnTests.
/// </summary>
public static class SupplierSeed
{
    public static async Task<int> SeedVerifiedSupplierAsync(
        string connectionString, string legalId, string name)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DECLARE @Sid INT;
            INSERT INTO dbo.Suppliers (LegalId, Name, VerificationStatus, CreatedAt, UpdatedAt)
            VALUES (@LegalId, @Name, 2, SYSUTCDATETIME(), SYSUTCDATETIME());
            SET @Sid = SCOPE_IDENTITY();
            INSERT INTO dbo.SupplierBranches (SupplierId, BranchName, IsDefault, CreatedAt, UpdatedAt)
            VALUES (@Sid, 'Sede principal', 1, SYSUTCDATETIME(), SYSUTCDATETIME());
            SELECT @Sid;";
        cmd.Parameters.AddWithValue("@LegalId", legalId);
        cmd.Parameters.AddWithValue("@Name", name);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    /// <summary>Looks up a supplier id by its (test-unique) Name.</summary>
    public static async Task<int> GetSupplierIdByNameAsync(string connectionString, string name)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 Id FROM dbo.Suppliers WHERE Name = @Name ORDER BY Id DESC;";
        cmd.Parameters.AddWithValue("@Name", name);
        var result = await cmd.ExecuteScalarAsync();
        return result is null ? 0 : Convert.ToInt32(result);
    }
}
