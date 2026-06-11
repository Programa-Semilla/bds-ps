// Spec 031 — shared SQL seed helper for searchable-dropdown E2E.
//
// The ephemeral seed has only 1 Fund / 1 Process / 3 Groups — every cascade-fund
// level is below the searchable threshold (7). To exercise the combobox on a
// data-driven control we need >7 selectable options. Funds are the cheapest lever
// (Name + Description + Active status, no FK dependents), so these tests insert a
// batch of throwaway Funds via SQL and DELETE them in teardown by name prefix.
//
// IMPORTANT: the AspireFixture SQL container is SHARED across every test class in
// the run. Leaving these Funds behind would push other suites' Fund selects over
// the threshold and break their SelectOptionAsync calls — so teardown MUST remove
// them. Deletion is safe because seeded Funds have no Processes referencing them.

using Microsoft.Data.SqlClient;

namespace FundingPlatform.Tests.E2E.Tests.SearchableDropdowns;

internal static class SearchableSeed
{
    /// <summary>
    /// Insert <paramref name="count"/> Active Funds named "<paramref name="prefix"/> N".
    /// Returns the inserted names in order.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> SeedFundsAsync(string connectionString, string prefix, int count)
    {
        var names = new List<string>();
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        for (var i = 1; i <= count; i++)
        {
            var name = $"{prefix} {i:00}";
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "INSERT INTO dbo.Funds (Name, Description, Status) VALUES (@name, @desc, 0);";
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@desc", "spec031 throwaway fund");
            await cmd.ExecuteNonQueryAsync();
            names.Add(name);
        }
        return names;
    }

    /// <summary>Remove every Fund whose Name starts with <paramref name="prefix"/>.</summary>
    internal static async Task RemoveFundsAsync(string connectionString, string prefix)
    {
        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM dbo.Funds WHERE Name LIKE @p;";
        cmd.Parameters.AddWithValue("@p", prefix + "%");
        await cmd.ExecuteNonQueryAsync();
    }
}
