using Microsoft.Data.SqlClient;

namespace FundingPlatform.Tests.E2E.Fixtures;

/// <summary>
/// Spec 047 / US3 — E2E helper to make a budget-line closable: seeds one VALIDATED disbursement
/// paying the application's (single) line so the closure gate's "at least one validated payment"
/// requirement (spec.md line 95) and the LinePaid==LineAccepted leg can be satisfied. The matching
/// signed-acceptance is attached through the evidence UI by the test.
/// </summary>
public static class EvidenceClosureSeeder
{
    /// <summary>Inserts a Validated disbursement + its line allocation for the app's first item, and
    /// returns the (itemId, amount) so the test can attach a matching acceptance.</summary>
    public static async Task<int> SeedValidatedPaymentForFirstLineAsync(
        string connectionString, int applicationId, decimal amount, string byUserEmail)
    {
        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();

        var userId = await ScalarStringAsync(conn,
            "SELECT Id FROM dbo.AspNetUsers WHERE Email = @email;", ("@email", byUserEmail))
            ?? throw new InvalidOperationException($"User '{byUserEmail}' not found for closure seed.");

        var itemId = await ScalarIntAsync(conn,
            "SELECT TOP 1 Id FROM dbo.Items WHERE ApplicationId = @appId ORDER BY Id;", ("@appId", applicationId))
            ?? throw new InvalidOperationException($"No item found for application {applicationId}.");

        const string sql = @"
DECLARE @dId INT;
INSERT INTO dbo.Disbursements
    (ApplicationId, PaymentDate, Amount, BankTransactionReference, State, CreatedByUserId, CreatedAtUtc, ValidatedByUserId, ValidatedAtUtc)
VALUES
    (@appId, CAST(SYSUTCDATETIME() AS DATE), @amount, 'TX-CLOSURE-SEED', 2, @userId, SYSUTCDATETIME(), @userId, SYSUTCDATETIME());
SET @dId = SCOPE_IDENTITY();
INSERT INTO dbo.DisbursementLineAllocations (DisbursementId, ItemId, Amount)
VALUES (@dId, @itemId, @amount);";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@appId", applicationId);
        cmd.Parameters.AddWithValue("@amount", amount);
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@itemId", itemId);
        await cmd.ExecuteNonQueryAsync();

        return itemId;
    }

    private static async Task<string?> ScalarStringAsync(SqlConnection conn, string sql, params (string, object)[] ps)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        return (await cmd.ExecuteScalarAsync())?.ToString();
    }

    private static async Task<int?> ScalarIntAsync(SqlConnection conn, string sql, params (string, object)[] ps)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (n, v) in ps) cmd.Parameters.AddWithValue(n, v);
        var r = await cmd.ExecuteScalarAsync();
        return r is null || r is DBNull ? null : Convert.ToInt32(r);
    }
}
