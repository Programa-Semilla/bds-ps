using FundingPlatform.Application.Services;

namespace FundingPlatform.Tests.Unit.Application;

/// <summary>
/// Spec 027 / US1 (FR-001, FR-002) — the generator attribution must never show
/// a raw user identifier. These cover the post-processing applied on top of
/// <c>IUserStoreReader.GetDisplayNameAsync</c>'s ladder (full name → email → id).
/// </summary>
[TestFixture]
public class GeneratorDisplayNameTests
{
    private const string UserId = "48120df3-9be7-4f93-920a-7b2934106ae8";

    [Test]
    public void FromResolved_FullName_IsUsedVerbatim()
    {
        var result = GeneratorDisplayName.FromResolved("Ana Pérez", UserId);
        Assert.That(result, Is.EqualTo("Ana Pérez"));
    }

    [Test]
    public void FromResolved_EmailWhenNameUnset_IsUsedVerbatim()
    {
        // The reader returns the email when the name is blank; that is a valid
        // human-readable value and must pass through unchanged.
        var result = GeneratorDisplayName.FromResolved("ana@programa-semilla.test", UserId);
        Assert.That(result, Is.EqualTo("ana@programa-semilla.test"));
    }

    [Test]
    public void FromResolved_DeletedAccount_FallsBackToStableLabel_NeverGuid()
    {
        // Deleted account: the reader's ladder falls through to the id itself.
        var result = GeneratorDisplayName.FromResolved(UserId, UserId);

        Assert.That(result, Is.EqualTo(GeneratorDisplayName.DeletedFallback));
        Assert.That(result, Is.Not.EqualTo(UserId));
        Assert.That(IsGuidLike(result), Is.False, "must never surface a raw identifier");
    }

    [Test]
    public void FromResolved_BlankResolution_FallsBackToStableLabel()
    {
        Assert.That(GeneratorDisplayName.FromResolved(null, UserId), Is.EqualTo(GeneratorDisplayName.DeletedFallback));
        Assert.That(GeneratorDisplayName.FromResolved("   ", UserId), Is.EqualTo(GeneratorDisplayName.DeletedFallback));
    }

    [Test]
    public void FromResolved_NoAgreement_IsNull()
    {
        // No GeneratedByUserId → no agreement generated yet → no attribution.
        Assert.That(GeneratorDisplayName.FromResolved(null, null), Is.Null);
        Assert.That(GeneratorDisplayName.FromResolved("anything", ""), Is.Null);
    }

    private static bool IsGuidLike(string? value) => Guid.TryParse(value, out _);
}
