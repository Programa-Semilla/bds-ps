using FundingPlatform.Application.Abstractions.AiComparison;
using FundingPlatform.Application.AiComparison;

namespace FundingPlatform.Tests.Unit.AiComparison;

public class InputHasherTests
{
    private static InputDescriptor BaseDescriptor() => new(
        ApplicationItemId: 42,
        OrderedSupplierIds: new[] { 1, 2 },
        OrderedBranchIds: new[] { 10, 20 },
        BlobReferences: new[]
        {
            new BlobReference(Guid.Parse("11111111-1111-1111-1111-111111111111"), "hashA"),
            new BlobReference(Guid.Parse("22222222-2222-2222-2222-222222222222"), "hashB"),
        },
        LineState: new[]
        {
            new LineState(100, 1m, 120000m, "CRC", null),
            new LineState(101, 2m, 100m, "USD", Guid.Parse("33333333-3333-3333-3333-333333333333")),
        },
        PromptVersion: "2026-05-11",
        SchemaVersion: "v1");

    [Test]
    public void Compute_Is64LowercaseHex()
    {
        var hash = InputHasher.Compute(BaseDescriptor());
        Assert.That(hash, Has.Length.EqualTo(64));
        Assert.That(hash, Does.Match("^[a-f0-9]{64}$"));
    }

    [Test]
    public void Compute_IsDeterministic_AcrossCalls()
    {
        var h1 = InputHasher.Compute(BaseDescriptor());
        var h2 = InputHasher.Compute(BaseDescriptor());
        Assert.That(h1, Is.EqualTo(h2));
    }

    [Test]
    public void Compute_ChangesWhenAnyFieldMutates()
    {
        var baseline = InputHasher.Compute(BaseDescriptor());

        var withDifferentSchema = BaseDescriptor() with { SchemaVersion = "v2" };
        Assert.That(InputHasher.Compute(withDifferentSchema), Is.Not.EqualTo(baseline));

        var withDifferentPrompt = BaseDescriptor() with { PromptVersion = "2026-06-01" };
        Assert.That(InputHasher.Compute(withDifferentPrompt), Is.Not.EqualTo(baseline));

        var withDifferentSuppliers = BaseDescriptor() with { OrderedSupplierIds = new[] { 1, 2, 3 } };
        Assert.That(InputHasher.Compute(withDifferentSuppliers), Is.Not.EqualTo(baseline));

        var withDifferentBlobs = BaseDescriptor() with
        {
            BlobReferences = new[]
            {
                new BlobReference(Guid.Parse("11111111-1111-1111-1111-111111111111"), "hashA"),
                new BlobReference(Guid.Parse("22222222-2222-2222-2222-222222222222"), "hashX"),
            },
        };
        Assert.That(InputHasher.Compute(withDifferentBlobs), Is.Not.EqualTo(baseline));
    }

    [Test]
    public void Compute_IsSensitiveToDeclaredListOrder()
    {
        var ordered = InputHasher.Compute(BaseDescriptor());
        var reordered = InputHasher.Compute(BaseDescriptor() with
        {
            OrderedSupplierIds = new[] { 2, 1 },
        });
        Assert.That(reordered, Is.Not.EqualTo(ordered));
    }
}
