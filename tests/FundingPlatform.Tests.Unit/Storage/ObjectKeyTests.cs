using FundingPlatform.Application.Abstractions.Storage;

namespace FundingPlatform.Tests.Unit.Storage;

[TestFixture]
public class ObjectKeyTests
{
    [Test]
    public void Build_produces_canonical_format_for_applicant_owned_files()
    {
        var key = ObjectKey.Build(
            FileCategory.SignedFundingAgreement,
            "applicants/12345678-aaaa-bbbb-cccc-1234567890ab",
            "98765432-eeee-ffff-aaaa-1234567890ab",
            "abc123def456",
            ".pdf");

        Assert.That(key.Value, Is.EqualTo(
            "signed-funding-agreements/applicants/12345678-aaaa-bbbb-cccc-1234567890ab/98765432-eeee-ffff-aaaa-1234567890ab/abc123def456.pdf"));
    }

    [Test]
    public void Build_lowercases_owner_segment_and_entity_id()
    {
        var key = ObjectKey.Build(
            FileCategory.GeneratedArtifact,
            "Applicants/ABC-123",
            "DEADBEEF",
            "Suffix",
            ".PDF");

        Assert.That(key.OwnerSegment, Is.EqualTo("applicants/abc-123"));
        Assert.That(key.EntityId, Is.EqualTo("deadbeef"));
        Assert.That(key.DeterministicSuffix, Is.EqualTo("suffix"));
        Assert.That(key.Extension, Is.EqualTo(".pdf"));
    }

    [Test]
    public void Build_defaults_extension_to_bin_when_missing()
    {
        var key = ObjectKey.Build(
            FileCategory.ApplicationAttachment,
            "applicants/abc",
            "entity",
            "suffix",
            null);

        Assert.That(key.Extension, Is.EqualTo(".bin"));
        Assert.That(key.Value.EndsWith(".bin", StringComparison.Ordinal), Is.True);
    }

    [Test]
    public void Build_rejects_path_traversal_in_owner_segment()
    {
        Assert.Throws<ArgumentException>(() => ObjectKey.Build(
            FileCategory.SignedFundingAgreement,
            "applicants/../admin",
            "entity",
            "suffix",
            ".pdf"));
    }

    [Test]
    public void Build_rejects_blank_required_fields()
    {
        Assert.Throws<ArgumentException>(() => ObjectKey.Build(
            FileCategory.SignedFundingAgreement,
            "",
            "entity",
            "suffix",
            ".pdf"));

        Assert.Throws<ArgumentException>(() => ObjectKey.Build(
            FileCategory.SignedFundingAgreement,
            "applicants/abc",
            "",
            "suffix",
            ".pdf"));

        Assert.Throws<ArgumentException>(() => ObjectKey.Build(
            FileCategory.SignedFundingAgreement,
            "applicants/abc",
            "entity",
            "",
            ".pdf"));
    }

    [Test]
    public void Build_rejects_keys_over_1024_bytes()
    {
        var huge = new string('a', 2000);
        Assert.Throws<ArgumentException>(() => ObjectKey.Build(
            FileCategory.SignedFundingAgreement,
            "applicants/abc",
            "entity",
            huge,
            ".pdf"));
    }

    [Test]
    public void Parse_round_trips_a_built_key()
    {
        var key = ObjectKey.Build(
            FileCategory.SupplierCatalogImport,
            "admin",
            "batch-2026-05-01",
            "deadbeefcafebabe",
            ".csv");

        var parsed = ObjectKey.Parse(key.Value);

        Assert.That(parsed.Container, Is.EqualTo("supplier-catalog-imports"));
        Assert.That(parsed.OwnerSegment, Is.EqualTo("admin"));
        Assert.That(parsed.EntityId, Is.EqualTo("batch-2026-05-01"));
        Assert.That(parsed.DeterministicSuffix, Is.EqualTo("deadbeefcafebabe"));
        Assert.That(parsed.Extension, Is.EqualTo(".csv"));
        Assert.That(parsed.Value, Is.EqualTo(key.Value));
    }

    [Test]
    public void Parse_rejects_blank_input()
    {
        Assert.Throws<ArgumentException>(() => ObjectKey.Parse(""));
        Assert.Throws<ArgumentException>(() => ObjectKey.Parse("   "));
    }

    [Test]
    public void Parse_rejects_keys_with_path_traversal()
    {
        Assert.Throws<ArgumentException>(() =>
            ObjectKey.Parse("signed-funding-agreements/applicants/../admin/x/y.pdf"));
    }

    [Test]
    public void Parse_rejects_keys_with_uppercase_container()
    {
        Assert.Throws<ArgumentException>(() =>
            ObjectKey.Parse("Signed-Funding-Agreements/applicants/x/y/z.pdf"));
    }

    [Test]
    public void ToString_returns_value()
    {
        var key = ObjectKey.Build(
            FileCategory.SignedFundingAgreement,
            "applicants/abc",
            "entity",
            "suffix",
            ".pdf");
        Assert.That(key.ToString(), Is.EqualTo(key.Value));
    }
}
