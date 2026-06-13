using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using NUnit.Framework;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class ItemTechnicalEquivalenceTests
{
    [Test]
    public void FlagNotEquivalent_SetsFlagAndRejects_WithoutPersistingEnglishComment()
    {
        var item = new Item("Widget", categoryId: 1);

        item.FlagNotEquivalent();

        Assert.That(item.IsNotTechnicallyEquivalent, Is.True);
        Assert.That(item.ReviewStatus, Is.EqualTo(ItemReviewStatus.Rejected));
        // The not-equivalent state is carried by the boolean flag; the domain must
        // NOT persist a hard-coded English sentence as ReviewComment (it leaked onto
        // the applicant Details page as "Rejected: quotations are not technically
        // equivalent"). The flag drives the localized message in the views.
        Assert.That(item.ReviewComment, Is.Null);
    }

    [Test]
    public void Reject_PreservesReviewerFreeTextComment()
    {
        var item = new Item("Widget", categoryId: 1);

        item.Reject("Falta documentación de respaldo.");

        Assert.That(item.ReviewStatus, Is.EqualTo(ItemReviewStatus.Rejected));
        Assert.That(item.ReviewComment, Is.EqualTo("Falta documentación de respaldo."));
    }
}
