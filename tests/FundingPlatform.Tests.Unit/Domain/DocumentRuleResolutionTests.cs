using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 047 / US2 — the pure required-document rule pieces: the <see cref="DocumentRuleSet"/>
/// full-replace + required-type extraction, and the <see cref="Item.MissingRequiredDocuments"/>
/// helper (category → set fallback + both-source completeness are exercised in integration).
/// </summary>
[TestFixture]
public class DocumentRuleResolutionTests
{
    [Test]
    public void ReplaceItems_KeepsOnlyRequired_InRequiredTypes()
    {
        var set = DocumentRuleSet.Create(categoryId: 7);
        set.ReplaceItems(new[]
        {
            (EvidenceType.BankReceipt, true),
            (EvidenceType.Invoice, true),
            (EvidenceType.SignedAcceptance, false),
        });

        Assert.That(set.RequiredTypes(), Is.EquivalentTo(new[] { EvidenceType.BankReceipt, EvidenceType.Invoice }));
    }

    [Test]
    public void ReplaceItems_IsFullReplace()
    {
        var set = DocumentRuleSet.Create(null);
        set.ReplaceItems(new[] { (EvidenceType.Invoice, true) });
        set.ReplaceItems(new[] { (EvidenceType.SignedAcceptance, true) });

        Assert.That(set.RequiredTypes(), Is.EquivalentTo(new[] { EvidenceType.SignedAcceptance }));
    }

    [Test]
    public void ReplaceItems_CollapsesDuplicateTypes()
    {
        var set = DocumentRuleSet.Create(null);
        set.ReplaceItems(new[] { (EvidenceType.Invoice, false), (EvidenceType.Invoice, true) });
        Assert.That(set.Items, Has.Count.EqualTo(1));
        Assert.That(set.RequiredTypes(), Is.EquivalentTo(new[] { EvidenceType.Invoice }));
    }

    [Test]
    public void MissingRequiredDocuments_ReturnsRequiredNotPresent()
    {
        var required = new[] { EvidenceType.Invoice, EvidenceType.SignedAcceptance, EvidenceType.BankReceipt };
        var present = new[] { EvidenceType.BankReceipt };

        var missing = Item.MissingRequiredDocuments(required, present).ToList();

        Assert.That(missing, Is.EquivalentTo(new[] { EvidenceType.Invoice, EvidenceType.SignedAcceptance }));
    }

    [Test]
    public void MissingRequiredDocuments_AllPresent_IsEmpty()
    {
        var required = new[] { EvidenceType.Invoice };
        var present = new[] { EvidenceType.Invoice, EvidenceType.BankReceipt };
        Assert.That(Item.MissingRequiredDocuments(required, present), Is.Empty);
    }
}
