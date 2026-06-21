using FundingPlatform.Application.Regulatory;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Tests.Unit.Application;

/// <summary>
/// Spec 043 / FR-007 + FR-010 — the block + warning messages must enumerate EVERY stale
/// provider + field (not just the first), and name the field + last-reviewed date.
/// </summary>
[TestFixture]
public class RegulatoryFreshnessCopyTests
{
    private static readonly DateTime Reviewed = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    [Test]
    public void BuildBlockMessage_ListsEveryStaleProviderAndField()
    {
        var findings = new List<StaleRegulatoryFinding>
        {
            new(1, "Proveedor Uno", RegulatoryField.Hacienda, null),
            new(1, "Proveedor Uno", RegulatoryField.Ccss, Reviewed),
            new(2, "Proveedor Dos", RegulatoryField.Sicop, null),
        };

        var msg = RegulatoryFreshnessCopy.BuildBlockMessage(findings);

        Assert.That(msg, Does.StartWith(RegulatoryFreshnessCopy.BlockHeading));
        // FR-007 — all three findings enumerated (provider + field + last-reviewed).
        Assert.That(msg, Does.Contain("Proveedor Uno"));
        Assert.That(msg, Does.Contain("Proveedor Dos"));
        Assert.That(msg, Does.Contain("Hacienda"));
        Assert.That(msg, Does.Contain("CCSS / Caja"));
        Assert.That(msg, Does.Contain("SICOP"));
        Assert.That(msg, Does.Contain("sin revisar"));               // null timestamp
        Assert.That(msg, Does.Contain("revisado por última vez el 01/03/2026")); // dated
    }

    [Test]
    public void BuildWarningMessage_NamesProvidersAndFields()
    {
        var findings = new List<StaleRegulatoryFinding>
        {
            new(7, "Acme S.A.", RegulatoryField.Hacienda, null),
            new(8, "Beta Ltda.", RegulatoryField.Ccss, null),
        };

        var msg = RegulatoryFreshnessCopy.BuildWarningMessage(findings);

        Assert.That(msg, Does.StartWith(RegulatoryFreshnessCopy.WarningHeading));
        Assert.That(msg, Does.Contain("Acme S.A."));
        Assert.That(msg, Does.Contain("Beta Ltda."));
        Assert.That(msg, Does.Contain("Hacienda"));
        Assert.That(msg, Does.Contain("CCSS / Caja"));
    }

    [Test]
    public void FieldLabel_IsEsCrForEachField()
    {
        Assert.That(RegulatoryFreshnessCopy.FieldLabel(RegulatoryField.Hacienda), Is.EqualTo("Hacienda"));
        Assert.That(RegulatoryFreshnessCopy.FieldLabel(RegulatoryField.Ccss), Is.EqualTo("CCSS / Caja"));
        Assert.That(RegulatoryFreshnessCopy.FieldLabel(RegulatoryField.Sicop), Is.EqualTo("SICOP"));
    }
}
