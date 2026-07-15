using Microsoft.Data.SqlClient;

namespace FundingPlatform.Tests.E2E.Fixtures;

/// <summary>
/// Spec 044 — E2E helper that inserts a reception window (ProcessEvent) directly via
/// SQL, relative to real <c>UtcNow</c> (no clock freeze, per research D2). Used by the
/// gating / notice / draft-guard tests to put an isolated Process into a known state
/// (open / upcoming / closed) without driving the admin datetime-local UI.
/// </summary>
public static class ReceptionWindowSeed
{
    public enum WindowState { Open, Upcoming, Closed }

    /// <summary>Inserts a single active reception window on <paramref name="processId"/>
    /// in the requested state, anchored to the current UTC instant.</summary>
    public static async Task SeedAsync(
        string connectionString, int processId, WindowState state, string? name = null)
    {
        var now = DateTimeOffset.UtcNow;
        var (start, end) = state switch
        {
            WindowState.Open => (now.AddDays(-1), now.AddDays(2)),
            WindowState.Upcoming => (now.AddDays(1), now.AddDays(3)),
            WindowState.Closed => (now.AddDays(-3), now.AddDays(-1)),
            _ => (now.AddDays(-1), now.AddDays(1)),
        };

        using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO [dbo].[ProcessEvents]
                ([ProcessId], [EventType], [Name], [StartUtc], [EndUtc],
                 [ControlsSubmissionAvailability], [IsActive], [DisplayOrder], [CreatedAt])
            VALUES
                (@ProcessId, 0, @Name, @StartUtc, @EndUtc, 1, 1, 0, SYSUTCDATETIME());
            """;
        cmd.Parameters.AddWithValue("@ProcessId", processId);
        cmd.Parameters.AddWithValue("@Name", name ?? $"Ventana E2E {state}");
        cmd.Parameters.AddWithValue("@StartUtc", start);
        cmd.Parameters.AddWithValue("@EndUtc", end);
        await cmd.ExecuteNonQueryAsync();
    }
}
