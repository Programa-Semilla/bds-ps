using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.AiComparison;
using Microsoft.Extensions.Configuration;

namespace FundingPlatform.Tests.Unit.AiComparison;

public class SchemaValidationTests
{
    private static SchemaValidator BuildValidator()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var catalog = new PromptCatalog(config);
        return new SchemaValidator(catalog);
    }

    [Test]
    public void ValidateCompare_AcceptsHappyPathFixture()
    {
        var validator = BuildValidator();
        var fixturePath = ResolveFixture("canned-compare.json");
        var json = File.ReadAllText(fixturePath);

        Assert.DoesNotThrow(() => validator.ValidateCompare(json));
    }

    [Test]
    public void ValidateCompare_MalformedJson_ThrowsSchemaInvalid()
    {
        var validator = BuildValidator();
        Assert.Throws<AiSchemaInvalidException>(() =>
            validator.ValidateCompare("{ not valid"));
    }

    [Test]
    public void ValidateCompare_MissingItems_ThrowsSchemaInvalid()
    {
        var validator = BuildValidator();
        Assert.Throws<AiSchemaInvalidException>(() =>
            validator.ValidateCompare("{ \"schemaVersion\": \"v1\" }"));
    }

    [Test]
    public void ValidateCompare_AdditionalPropertyViolation_ThrowsSchemaInvalid()
    {
        var validator = BuildValidator();
        const string json = """
        {
          "schemaVersion": "v1",
          "items": [],
          "unexpectedField": "stowaway"
        }
        """;

        Assert.Throws<AiSchemaInvalidException>(() => validator.ValidateCompare(json));
    }

    [Test]
    public void ValidateExtract_AcceptsCannedExtractFixture()
    {
        var validator = BuildValidator();
        var fixturePath = ResolveFixture("canned-extract.json");
        var json = File.ReadAllText(fixturePath);
        Assert.DoesNotThrow(() => validator.ValidateExtract(json));
    }

    private static string ResolveFixture(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "Fixtures", "AiComparison", name);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(name);
    }
}
