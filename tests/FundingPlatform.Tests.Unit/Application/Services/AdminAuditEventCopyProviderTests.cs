using FundingPlatform.Application.Services;
using FundingPlatform.Domain.Entities;

namespace FundingPlatform.Tests.Unit.Application.Services;

/// <summary>
/// Spec 017 / US7 / R5 — voice-guide-compliant es-CR mappings for the four
/// shipped <see cref="AdminAuditEvent"/> action constants. No exclamation
/// marks, no "submit" CTAs, no passive voice.
/// </summary>
[TestFixture]
public class AdminAuditEventCopyProviderTests
{
    private AdminAuditEventCopyProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        _provider = new AdminAuditEventCopyProvider();
    }

    [Test]
    public void Format_GroupCreate_ReturnsEsCrPhrase()
    {
        var phrase = _provider.Format(AdminAuditEvent.ActionGroupCreate, AdminAuditEvent.TargetTypeGroup, payloadJson: null);
        Assert.That(phrase, Is.EqualTo("creó el grupo"));
    }

    [Test]
    public void Format_GroupRename_ReturnsEsCrPhrase()
    {
        var phrase = _provider.Format(AdminAuditEvent.ActionGroupRename, AdminAuditEvent.TargetTypeGroup, payloadJson: null);
        Assert.That(phrase, Is.EqualTo("renombró el grupo"));
    }

    [Test]
    public void Format_GroupDelete_ReturnsEsCrPhrase()
    {
        var phrase = _provider.Format(AdminAuditEvent.ActionGroupDelete, AdminAuditEvent.TargetTypeGroup, payloadJson: null);
        Assert.That(phrase, Is.EqualTo("eliminó el grupo"));
    }

    [Test]
    public void Format_UserMembershipsUpdate_ReturnsEsCrPhrase()
    {
        var phrase = _provider.Format(AdminAuditEvent.ActionUserMembershipsUpdate, AdminAuditEvent.TargetTypeUser, payloadJson: null);
        Assert.That(phrase, Is.EqualTo("actualizó las membresías de"));
    }

    [Test]
    public void Format_UnknownAction_ReturnsGenericFallback()
    {
        var phrase = _provider.Format("supplier.verified", "supplier", payloadJson: null);
        Assert.That(phrase, Is.EqualTo("registró un cambio en"));
    }

    [TestCase(AdminAuditEvent.ActionGroupCreate)]
    [TestCase(AdminAuditEvent.ActionGroupRename)]
    [TestCase(AdminAuditEvent.ActionGroupDelete)]
    [TestCase(AdminAuditEvent.ActionUserMembershipsUpdate)]
    public void Format_AllShippedActions_AreVoiceGuideCompliant(string action)
    {
        var phrase = _provider.Format(action, AdminAuditEvent.TargetTypeGroup, payloadJson: null);
        Assert.That(phrase, Does.Not.Contain("!"), "voice guide: no exclamation marks");
        Assert.That(phrase, Does.Not.Match("[A-Z]{2,}"), "voice guide: no ALL CAPS shouting");
        Assert.That(phrase, Is.Not.Empty);
    }
}
