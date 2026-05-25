// Spec 025 — foundational real-DB checks (T018 + T019) against the AspireFixture's
// seeded SQL Server. The Integration project runs on EF InMemory (no dacpac seed),
// so the distrito-catalog oracle and the /api/districts contract live here, where a
// real SQL Server with the post-deploy seed is available.

using System.Net;
using System.Text.Json;
using FundingPlatform.Tests.E2E.Fixtures;
using Microsoft.Data.SqlClient;

namespace FundingPlatform.Tests.E2E.Tests.Supplier;

[TestFixture]
public class DistrictsCatalogE2E : AuthenticatedTestBase
{
    private async Task<int> CantonIdByCodeAsync(string code)
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT [Id] FROM dbo.Cantons WHERE [Code] = @Code;";
        cmd.Parameters.AddWithValue("@Code", code);
        var result = await cmd.ExecuteScalarAsync();
        Assert.That(result, Is.Not.Null.And.Not.EqualTo(DBNull.Value), $"Cantón {code} must be seeded.");
        return (int)result!;
    }

    // T018 — GET /api/districts?cantonId= contract.
    [Test]
    public async Task DistrictsApi_ReturnsCantonsDistricts_OrderedByName_WithPublicCache()
    {
        var golfitoId = await CantonIdByCodeAsync("06_07");

        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var response = await http.GetAsync($"/api/districts?cantonId={golfitoId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        // Public, hour-long edge cache (legislatively-static catalog).
        var cacheControl = response.Headers.CacheControl?.ToString() ?? string.Empty;
        Assert.That(cacheControl, Does.Contain("public"));
        Assert.That(cacheControl, Does.Contain("max-age=3600"));

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var names = doc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();

        // Golfito has exactly 3 distritos; the endpoint orders by name.
        Assert.That(names, Is.EqualTo(new[] { "Golfito", "Guaycará", "Pavón" }));
        // Each element exposes { id, name }.
        var first = doc.RootElement[0];
        Assert.That(first.GetProperty("id").GetInt32(), Is.GreaterThan(0));
    }

    [Test]
    public async Task DistrictsApi_UnknownCantonId_ReturnsEmptyArray()
    {
        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        var response = await http.GetAsync("/api/districts?cantonId=999999");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = (await response.Content.ReadAsStringAsync()).Trim();
        Assert.That(body, Is.EqualTo("[]"));
    }

    // T019 — the SC-007 seed oracle.
    [Test]
    public async Task DistrictSeed_MatchesAuthoritativeEnumeration()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        async Task<int> ScalarAsync(string sql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // National total = 488.
        Assert.That(await ScalarAsync("SELECT COUNT(*) FROM dbo.Districts"), Is.EqualTo(488),
            "National distrito total must be 488.");

        // Per-province totals (province = first two chars of the distrito Code).
        var targets = new Dictionary<string, int>
        {
            ["01"] = 123, ["02"] = 116, ["03"] = 51, ["04"] = 47,
            ["05"] = 61, ["06"] = 60, ["07"] = 30,
        };
        foreach (var (prov, expected) in targets)
        {
            var got = await ScalarAsync(
                $"SELECT COUNT(*) FROM dbo.Districts WHERE LEFT([Code],2) = '{prov}'");
            Assert.That(got, Is.EqualTo(expected), $"Province {prov} distrito count.");
        }

        // Every one of the 84 cantones has >= 1 distrito.
        Assert.That(await ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Cantons c WHERE NOT EXISTS (SELECT 1 FROM dbo.Districts d WHERE d.CantonId = c.Id)"),
            Is.EqualTo(0), "Every cantón must have at least one distrito.");

        // Load-bearing edge cases.
        async Task<int> CountForCantonCode(string code) => await ScalarAsync(
            $"SELECT COUNT(*) FROM dbo.Districts d JOIN dbo.Cantons c ON c.Id = d.CantonId WHERE c.Code = '{code}'");
        Assert.That(await CountForCantonCode("06_07"), Is.EqualTo(3), "Golfito 06_07 = 3 distritos.");
        Assert.That(await CountForCantonCode("06_12"), Is.EqualTo(1), "Monteverde 06_12 = 1 distrito.");
        Assert.That(await CountForCantonCode("06_13"), Is.EqualTo(1), "Puerto Jiménez 06_13 = 1 distrito.");

        // FK prefix integrity — every distrito's 'PP_CC' prefix equals its cantón Code.
        Assert.That(await ScalarAsync(
            "SELECT COUNT(*) FROM dbo.Districts d JOIN dbo.Cantons c ON c.Id = d.CantonId WHERE LEFT(d.[Code],5) <> c.[Code]"),
            Is.EqualTo(0), "Every distrito Code prefix must match its cantón Code.");
    }
}
