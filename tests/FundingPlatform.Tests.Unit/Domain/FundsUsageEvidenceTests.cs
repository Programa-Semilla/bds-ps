using FundingPlatform.Domain.Enums;
using AppEntity = FundingPlatform.Domain.Entities.Application;
using EvidenceEntity = FundingPlatform.Domain.Entities.FundsUsageEvidence;

namespace FundingPlatform.Tests.Unit.Domain;

[TestFixture]
public class FundsUsageEvidenceTests
{
    private static AppEntity ExecutedApplication()
    {
        var app = new AppEntity(applicantId: 1, groupId: 1, companyName: "Test Company");
        ApplicationResponseTransitionsTests.SetState(app, ApplicationState.AgreementExecuted);
        return app;
    }

    private static EvidenceEntity Create(AppEntity app, string? note = null)
        => EvidenceEntity.CreateForExecutedApplication(
            app, "reviewer-1", "evidence.pdf", "funds-usage-evidence/application/1/1/abc.pdf", 1024, "application/pdf", note);

    [Test]
    public void CreateForExecutedApplication_HappyPath_SetsFields()
    {
        var app = ExecutedApplication();

        var evidence = Create(app, "  una nota  ");

        Assert.Multiple(() =>
        {
            Assert.That(evidence.UploadedByUserId, Is.EqualTo("reviewer-1"));
            Assert.That(evidence.OriginalFileName, Is.EqualTo("evidence.pdf"));
            Assert.That(evidence.FileSize, Is.EqualTo(1024L));
            Assert.That(evidence.ContentType, Is.EqualTo("application/pdf"));
            Assert.That(evidence.Note, Is.EqualTo("una nota")); // trimmed
            Assert.That(evidence.UploadedAt, Is.LessThanOrEqualTo(DateTime.UtcNow.AddSeconds(1)));
        });
    }

    [TestCase(ApplicationState.Draft)]
    [TestCase(ApplicationState.Submitted)]
    [TestCase(ApplicationState.UnderReview)]
    [TestCase(ApplicationState.Resolved)]
    [TestCase(ApplicationState.ResponseFinalized)]
    public void CreateForExecutedApplication_NonExecutedState_Throws(ApplicationState state)
    {
        var app = new AppEntity(applicantId: 1, groupId: 1, companyName: "Test Company");
        ApplicationResponseTransitionsTests.SetState(app, state);

        Assert.Throws<InvalidOperationException>(() => Create(app));
    }

    [Test]
    public void CreateForExecutedApplication_EmptyNote_NormalizesToNull()
    {
        var evidence = Create(ExecutedApplication(), "   ");
        Assert.That(evidence.Note, Is.Null);
    }

    [Test]
    public void CreateForExecutedApplication_NoteExactly250_Allowed()
    {
        var note = new string('a', 250);
        var evidence = Create(ExecutedApplication(), note);
        Assert.That(evidence.Note, Has.Length.EqualTo(250));
    }

    [Test]
    public void CreateForExecutedApplication_NoteOver250_Throws()
    {
        var note = new string('a', 251);
        Assert.Throws<InvalidOperationException>(() => Create(ExecutedApplication(), note));
    }

    [Test]
    public void EditNote_TrimsAndEmptyToNull()
    {
        var evidence = Create(ExecutedApplication(), "original");

        evidence.EditNote("  nuevo  ");
        Assert.That(evidence.Note, Is.EqualTo("nuevo"));

        evidence.EditNote("");
        Assert.That(evidence.Note, Is.Null);
    }

    [Test]
    public void EditNote_Over250_Throws()
    {
        var evidence = Create(ExecutedApplication());
        Assert.Throws<InvalidOperationException>(() => evidence.EditNote(new string('a', 251)));
    }
}
