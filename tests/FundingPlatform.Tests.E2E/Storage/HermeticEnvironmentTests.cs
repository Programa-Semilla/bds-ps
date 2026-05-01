namespace FundingPlatform.Tests.E2E.Storage;

/// <summary>
/// Spec 014 / T039 / US3 — confirms the E2E suite runs without any Azure
/// cloud credentials in scope. The presence of an <c>AZURE_STORAGE_*</c>
/// environment variable would let the SDK pivot to a real Azure account on
/// failure, defeating the hermetic-by-default guarantee.
///
/// Less-strict variables like <c>AZURE_CONFIG_DIR</c> (Azure CLI cache) are
/// tolerated — they don't carry credentials. We only fail on the names that
/// the Azure SDK / DefaultAzureCredential treat as actionable.
/// </summary>
[TestFixture]
[Category("Storage014")]
public class HermeticEnvironmentTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        // DefaultAzureCredential / connection-string bearing.
        "AZURE_STORAGE_",
        // Service-principal flow.
        "AZURE_CLIENT_SECRET",
        "AZURE_CLIENT_CERTIFICATE_PATH",
        // Federated workload identity flow.
        "AZURE_FEDERATED_TOKEN",
    ];

    [Test]
    public void No_real_Azure_credentials_are_in_scope()
    {
        var leaked = new List<string>();
        foreach (var key in Environment.GetEnvironmentVariables().Keys)
        {
            var name = key?.ToString();
            if (string.IsNullOrEmpty(name)) continue;
            foreach (var prefix in ForbiddenPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    leaked.Add(name);
                    break;
                }
            }
        }

        Assert.That(leaked, Is.Empty,
            $"FR-008/SC-007 — the hermetic E2E suite must run with no Azure credentials. Leaked: {string.Join(", ", leaked)}");
    }
}
