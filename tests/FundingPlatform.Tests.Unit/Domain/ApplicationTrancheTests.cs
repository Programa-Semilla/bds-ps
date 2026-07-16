using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using AppEntity = FundingPlatform.Domain.Entities.Application;

namespace FundingPlatform.Tests.Unit.Domain;

/// <summary>
/// Spec 046 / US1 — aggregate-root invariants for tranche (funding-phase) structure:
/// create/rename/delete/assign, sibling-name uniqueness, the AgreementExecuted freeze, and
/// delete re-parenting member lines to the synthetic default. Exercised through the public
/// <see cref="AppEntity"/> entry points (Constitution II — no anemic leakage), plus a couple of
/// <see cref="Tranche"/> guard checks.
/// </summary>
[TestFixture]
public class ApplicationTrancheTests
{
    private static AppEntity BuildAppWithItems(int itemCount)
    {
        var app = new AppEntity(applicantId: 1, 1, null, companyName: "Test Company");
        for (var i = 0; i < itemCount; i++)
        {
            var item = new Item($"Product-{i + 1}", categoryId: 1);
            typeof(Item).GetProperty("Id")!.SetValue(item, i + 1);
            app.AddItem(item);
        }
        return app;
    }

    /// <summary>Assign the synthetic Ids the DB would normally set, so lookups work in-memory.</summary>
    private static Tranche CreateTrancheWithId(AppEntity app, string name, int id)
    {
        var t = app.CreateTranche(name);
        typeof(Tranche).GetProperty("Id")!.SetValue(t, id);
        return t;
    }

    [Test]
    public void CreateTranche_AssignsSequentialOrdinals()
    {
        var app = BuildAppWithItems(0);

        var t1 = app.CreateTranche("Tramo 1");
        var t2 = app.CreateTranche("Tramo 2");

        Assert.That(t1.Ordinal, Is.EqualTo(1));
        Assert.That(t2.Ordinal, Is.EqualTo(2));
        Assert.That(app.Tranches, Has.Count.EqualTo(2));
    }

    [Test]
    public void CreateTranche_TrimsName()
    {
        var app = BuildAppWithItems(0);
        var t = app.CreateTranche("  Tramo 1  ");
        Assert.That(t.Name, Is.EqualTo("Tramo 1"));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateTranche_RejectsBlankName(string blank)
    {
        var app = BuildAppWithItems(0);
        Assert.That(() => app.CreateTranche(blank), Throws.ArgumentException);
    }

    [Test]
    public void CreateTranche_RejectsNameOver60Chars()
    {
        var app = BuildAppWithItems(0);
        Assert.That(() => app.CreateTranche(new string('A', 61)), Throws.ArgumentException);
    }

    [Test]
    public void CreateTranche_RejectsDuplicateNameCaseInsensitive()
    {
        var app = BuildAppWithItems(0);
        app.CreateTranche("Tramo 1");

        Assert.That(() => app.CreateTranche("tramo 1"),
            Throws.InvalidOperationException.With.Message.Contains("already exists"));
    }

    [Test]
    public void RenameTranche_ChangesName()
    {
        var app = BuildAppWithItems(0);
        var t = CreateTrancheWithId(app, "Tramo 1", 10);

        app.RenameTranche(10, "Fase inicial");

        Assert.That(t.Name, Is.EqualTo("Fase inicial"));
    }

    [Test]
    public void RenameTranche_RejectsDuplicateSiblingName()
    {
        var app = BuildAppWithItems(0);
        CreateTrancheWithId(app, "Tramo 1", 10);
        CreateTrancheWithId(app, "Tramo 2", 20);

        Assert.That(() => app.RenameTranche(20, "Tramo 1"),
            Throws.InvalidOperationException);
    }

    [Test]
    public void RenameTranche_AllowsRenamingToOwnName()
    {
        var app = BuildAppWithItems(0);
        CreateTrancheWithId(app, "Tramo 1", 10);

        Assert.That(() => app.RenameTranche(10, "Tramo 1"), Throws.Nothing);
    }

    [Test]
    public void AssignItemToTranche_SetsMembership()
    {
        var app = BuildAppWithItems(2);
        CreateTrancheWithId(app, "Tramo 1", 10);

        app.AssignItemToTranche(itemId: 1, trancheId: 10);

        Assert.That(app.Items.Single(i => i.Id == 1).TrancheId, Is.EqualTo(10));
        Assert.That(app.Items.Single(i => i.Id == 2).TrancheId, Is.Null);
    }

    [Test]
    public void AssignItemToTranche_NullUnassigns()
    {
        var app = BuildAppWithItems(1);
        CreateTrancheWithId(app, "Tramo 1", 10);
        app.AssignItemToTranche(1, 10);

        app.AssignItemToTranche(1, null);

        Assert.That(app.Items[0].TrancheId, Is.Null);
    }

    [Test]
    public void AssignItemToTranche_RejectsForeignTranche()
    {
        var app = BuildAppWithItems(1);
        Assert.That(() => app.AssignItemToTranche(1, trancheId: 999),
            Throws.InvalidOperationException.With.Message.Contains("999"));
    }

    [Test]
    public void AssignItemToTranche_RejectsForeignItem()
    {
        var app = BuildAppWithItems(1);
        CreateTrancheWithId(app, "Tramo 1", 10);
        Assert.That(() => app.AssignItemToTranche(itemId: 999, trancheId: 10),
            Throws.InvalidOperationException.With.Message.Contains("999"));
    }

    [Test]
    public void DeleteTranche_ReparentsMemberLinesToSynthetic()
    {
        var app = BuildAppWithItems(2);
        CreateTrancheWithId(app, "Tramo 1", 10);
        app.AssignItemToTranche(1, 10);
        app.AssignItemToTranche(2, 10);

        app.DeleteTranche(10);

        Assert.That(app.Tranches, Is.Empty);
        Assert.That(app.Items.Select(i => i.TrancheId), Is.All.Null);
    }

    [Test]
    public void DeleteTranche_RejectsForeignTranche()
    {
        var app = BuildAppWithItems(0);
        Assert.That(() => app.DeleteTranche(999),
            Throws.InvalidOperationException.With.Message.Contains("999"));
    }

    // ---------- Freeze at AgreementExecuted (D4) ----------

    [Test]
    public void CreateTranche_FrozenAfterExecution()
    {
        var app = BuildAppWithItems(0);
        ApplicationResponseTransitionsTests.SetState(app, ApplicationState.AgreementExecuted);

        Assert.That(() => app.CreateTranche("Tramo 1"),
            Throws.InvalidOperationException.With.Message.Contains("frozen"));
    }

    [Test]
    public void RenameTranche_FrozenAfterExecution()
    {
        var app = BuildAppWithItems(0);
        CreateTrancheWithId(app, "Tramo 1", 10);
        ApplicationResponseTransitionsTests.SetState(app, ApplicationState.AgreementExecuted);

        Assert.That(() => app.RenameTranche(10, "X"), Throws.InvalidOperationException);
    }

    [Test]
    public void DeleteTranche_FrozenAfterExecution()
    {
        var app = BuildAppWithItems(0);
        CreateTrancheWithId(app, "Tramo 1", 10);
        ApplicationResponseTransitionsTests.SetState(app, ApplicationState.AgreementExecuted);

        Assert.That(() => app.DeleteTranche(10), Throws.InvalidOperationException);
    }

    [Test]
    public void AssignItemToTranche_FrozenAfterExecution()
    {
        var app = BuildAppWithItems(1);
        CreateTrancheWithId(app, "Tramo 1", 10);
        ApplicationResponseTransitionsTests.SetState(app, ApplicationState.AgreementExecuted);

        Assert.That(() => app.AssignItemToTranche(1, 10), Throws.InvalidOperationException);
    }

    [Test]
    public void FreezeThrow_CarriesTrancheFrozenDiscriminator()
    {
        var app = BuildAppWithItems(0);
        ApplicationResponseTransitionsTests.SetState(app, ApplicationState.AgreementExecuted);

        try
        {
            app.CreateTranche("Tramo 1");
            Assert.Fail("Expected the freeze guard to throw.");
        }
        catch (InvalidOperationException ex)
        {
            Assert.That(ex.Data[AppEntity.TrancheFrozenKey], Is.EqualTo(true));
        }
    }

    // ---------- Item commit invariants (via the aggregate; commit is NOT frozen post-execution) ----------

    [Test]
    public void CommitLine_IsIdempotent()
    {
        var app = BuildAppWithItems(1);

        app.CommitLine(1);
        Assert.That(app.Items[0].CommitState, Is.EqualTo(ItemCommitState.Committed));

        app.CommitLine(1);
        Assert.That(app.Items[0].CommitState, Is.EqualTo(ItemCommitState.Committed));
    }

    [Test]
    public void UncommitLine_ResetsToUncommitted()
    {
        var app = BuildAppWithItems(1);
        app.CommitLine(1);

        app.UncommitLine(1);

        Assert.That(app.Items[0].CommitState, Is.EqualTo(ItemCommitState.Uncommitted));
    }

    [Test]
    public void CommitLine_NotFrozenAfterExecution()
    {
        var app = BuildAppWithItems(1);
        ApplicationResponseTransitionsTests.SetState(app, ApplicationState.AgreementExecuted);

        // Commit is post-execution operator work — deliberately allowed while the tranche structure is frozen.
        Assert.That(() => app.CommitLine(1), Throws.Nothing);
        Assert.That(app.Items[0].CommitState, Is.EqualTo(ItemCommitState.Committed));
    }
}
