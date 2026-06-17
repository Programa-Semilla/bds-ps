using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.Exceptions;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 021 / FR-006 / FR-017 — submit-guard predicate matrix for the
/// stage-aware <see cref="AppEntity.Submit(int, StageKind, DateTimeOffset, DateTimeOffset)"/>
/// overload (plus the legacy <see cref="AppEntity.Submit(int)"/> for back-compat).
/// </summary>
[TestFixture]
public class ApplicationSubmitGuardTests
{
    private static AppEntity NewApp(string companyName = "Sazón Vegetariano")
        => new AppEntity(applicantId: 1, 1, null,companyName: companyName);

    private static Item NewItem(int id = 1, string productName = "Producto A")
    {
        var item = new Item(productName, categoryId: 1);
        typeof(Item).GetProperty("Id")!.SetValue(item, id);
        return item;
    }

    /// <summary>
    /// Pushes a placeholder <see cref="Quotation"/> into the item's private
    /// quotations list via reflection so <see cref="Item.HasMinimumQuotations"/>
    /// is satisfied without dragging in supplier / branch / document entities
    /// (out of scope for a domain unit test).
    /// </summary>
    private static void StuffQuotation(Item item)
    {
        var quotation = new Quotation(
            supplierId: 1,
            supplierBranchId: 1,
            documentId: 1,
            price: 100m,
            validUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            currency: "CRC");

        var quotationsField = typeof(Item).GetField(
            "_quotations",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (List<Quotation>)quotationsField!.GetValue(item)!;
        list.Add(quotation);
    }

    // Spec 035 (evolved 2026-06-16) — impact is declared at the application level; the
    // line item attributes itself to it + carries a justification.
    private static void AttachImpact(AppEntity app, Item item, int impactId = 1, int templateId = 10)
    {
        var existing = app.Impacts.FirstOrDefault(i => i.ImpactTemplateId == templateId);
        ApplicationImpact impact;
        if (existing is null)
        {
            var template = new ImpactTemplate("ImpactA", description: null, isActive: true);
            typeof(ImpactTemplate).GetProperty("Id")!.SetValue(template, templateId);
            impact = app.AddImpact(template, Array.Empty<ImpactParameterValue>());
            typeof(ApplicationImpact).GetProperty("Id")!.SetValue(impact, impactId);
        }
        else
        {
            impact = existing;
        }
        item.AttributeImpacts(new[] { impact.Id });
        item.SetImpactJustification("apoya el empleo");
    }

    // ----- Legacy Submit(int) overload -----------------------------------------------

    [Test]
    public void LegacySubmit_NoItems_Throws()
    {
        var app = NewApp();

        var ex = Assert.Throws<InvalidOperationException>(() => app.Submit(minQuotations: 1));
        Assert.That(ex!.Message, Does.Contain("at least one item"));
    }

    [Test]
    public void LegacySubmit_InsufficientQuotations_Throws()
    {
        var app = NewApp();
        var item = NewItem(1);
        app.AddItem(item);
        // Zero quotations on the item, min = 1.

        var ex = Assert.Throws<InvalidOperationException>(() => app.Submit(minQuotations: 1));
        Assert.That(ex!.Message, Does.Contain("quotation"));
    }

    [Test]
    public void LegacySubmit_WithItemAndQuotations_Succeeds()
    {
        var app = NewApp();
        var item = NewItem(1);
        StuffQuotation(item);
        AttachImpact(app, item);
        app.AddItem(item);

        app.Submit(minQuotations: 1);

        Assert.That(app.State, Is.EqualTo(ApplicationState.Submitted));
        Assert.That(app.SubmittedAt, Is.Not.Null);
    }

    // ----- Stage-aware Submit(int, StageKind, DateTimeOffset, DateTimeOffset) -----

    [Test]
    public void StageSubmit_NoItems_Throws()
    {
        var app = NewApp();
        var stageClosesAt = DateTimeOffset.UtcNow.AddDays(7);

        var ex = Assert.Throws<InvalidOperationException>(
            () => app.Submit(
                minQuotations: 1,
                currentStage: StageKind.Solicitud,
                stageClosesAt: stageClosesAt,
                now: DateTimeOffset.UtcNow));

        Assert.That(ex!.Message, Does.Contain("at least one item"));
    }

    [Test]
    public void StageSubmit_WithoutImpact_Throws()
    {
        var app = NewApp();
        var item = NewItem(1);
        StuffQuotation(item);
        app.AddItem(item);
        var stageClosesAt = DateTimeOffset.UtcNow.AddDays(7);

        var ex = Assert.Throws<InvalidOperationException>(
            () => app.Submit(
                minQuotations: 1,
                currentStage: StageKind.Solicitud,
                stageClosesAt: stageClosesAt,
                now: DateTimeOffset.UtcNow));

        Assert.That(ex!.Message, Does.Contain("impact"));
    }

    [Test]
    public void StageSubmit_WhenWindowClosed_ThrowsStageWindowClosedException()
    {
        var app = NewApp();
        var item = NewItem(1);
        StuffQuotation(item);
        AttachImpact(app, item);
        app.AddItem(item);

        var now = DateTimeOffset.UtcNow;
        var stageClosesAt = now.AddHours(-1); // already closed

        var ex = Assert.Throws<StageWindowClosedException>(
            () => app.Submit(
                minQuotations: 1,
                currentStage: StageKind.Solicitud,
                stageClosesAt: stageClosesAt,
                now: now));

        Assert.That(ex!.Stage, Is.EqualTo(StageKind.Solicitud));
        Assert.That(ex.ClosedAt, Is.EqualTo(stageClosesAt));
        Assert.That(ex.ErrorCode, Is.EqualTo("STAGE_WINDOW_CLOSED"));
    }

    [Test]
    public void StageSubmit_WhenNowExactlyEqualsClosesAt_ThrowsStageWindowClosedException()
    {
        // Boundary: now == stageClosesAt. Implementation uses `now >= stageClosesAt`,
        // so the closing instant itself rejects the submit.
        var app = NewApp();
        var item = NewItem(1);
        StuffQuotation(item);
        AttachImpact(app, item);
        app.AddItem(item);

        var now = DateTimeOffset.UtcNow;

        Assert.Throws<StageWindowClosedException>(
            () => app.Submit(
                minQuotations: 1,
                currentStage: StageKind.Revision,
                stageClosesAt: now,
                now: now));
    }

    [Test]
    public void StageSubmit_HappyPath_TransitionsAndResetsStageState()
    {
        var app = NewApp();
        var item = NewItem(1);
        StuffQuotation(item);
        AttachImpact(app, item);
        app.AddItem(item);
        // Mark a reminder bit so we can prove ResetStageState fires.
        app.MarkReminderSent(0x1);
        Assert.That(app.RemindersSentMask, Is.EqualTo(0x1));

        var now = DateTimeOffset.UtcNow;
        var stageClosesAt = now.AddDays(7);
        var before = now.AddSeconds(-1);

        app.Submit(
            minQuotations: 1,
            currentStage: StageKind.Solicitud,
            stageClosesAt: stageClosesAt,
            now: now);

        var after = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.That(app.State, Is.EqualTo(ApplicationState.Submitted));
        Assert.That(app.SubmittedAt, Is.Not.Null);
        // ResetStageState wipes the reminder mask + stamps StageEnteredAt.
        Assert.That(app.RemindersSentMask, Is.EqualTo(0));
        Assert.That(app.StageEnteredAt, Is.InRange(before, after));
    }

    [Test]
    public void StageSubmit_QuotationShortfall_Throws()
    {
        var app = NewApp();
        var item = NewItem(1);
        // No quotations stuffed — fails minQuotations=2.
        AttachImpact(app, item);
        app.AddItem(item);

        var now = DateTimeOffset.UtcNow;
        var stageClosesAt = now.AddDays(7);

        var ex = Assert.Throws<InvalidOperationException>(
            () => app.Submit(
                minQuotations: 2,
                currentStage: StageKind.Solicitud,
                stageClosesAt: stageClosesAt,
                now: now));

        Assert.That(ex!.Message, Does.Contain("quotation"));
    }
}
