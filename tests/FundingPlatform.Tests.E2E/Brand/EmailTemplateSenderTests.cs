using FundingPlatform.Tests.E2E.Fixtures;

namespace FundingPlatform.Tests.E2E.Brand;

/// <summary>
/// Spec 019 T075 / FR-006 / NFR-005 — Email template sender display.
///
/// At this spec's iteration the project does NOT register an IEmailSender or
/// ship email templates (verified at planning time and via the FINDING-4 deep
/// review pass). Once an email subsystem ships in a later spec, the contract
/// is:
///
///   - Capture an account-confirmation + password-reset email via the
///     AspireFixture SMTP fixture (when one is wired — the harness does not
///     yet expose an in-process IServiceProvider seam, so a HOST-SIDE health
///     endpoint or SMTP capture is the activation path).
///   - Assert sender display = "Programa Semilla / Sistema de Banca para
///     el Desarrollo".
///   - Assert signature block matches.
///   - Assert no inline &lt;img&gt; in body (NFR-005 compatibility).
///   - Assert "Capital Semilla" / "Forge" are absent from sender + subject
///     + body (FR-006 / SC-002).
///
/// Until the email subsystem lands, this test is INTENTIONALLY a static
/// Assert.Ignore — the brand-grep gate (T030) is the standing guard for stale
/// "Capital Semilla" / "Forge" strings in any future template, so the
/// FR-006 / NFR-005 regression cannot sneak past unnoticed even with the
/// SMTP-capture body inactive. Per FINDING-4, the runtime-DI auto-activation
/// path was investigated and ruled out: AspireFixture exposes BaseUrl /
/// ConnectionString / BlobsConnectionString but not the host's
/// IServiceProvider, so a runtime DI probe would require a new fixture
/// surface that doesn't exist yet. When the email subsystem ships, the
/// owning spec MUST also: (a) replace this Assert.Ignore with the
/// SMTP-capture body, OR (b) add a DI-probe seam to AspireFixture and
/// flip this test to an auto-activating runtime check.
/// </summary>
public class EmailTemplateSenderTests : AuthenticatedTestBase
{
    [Test]
    public void EmailInfrastructureDetected_OrSkip()
    {
        Assert.Ignore(
            "No email infrastructure detected — see "
            + "specs/019-programa-semilla-brand/BRAND-PIVOT-SWEEP-CHECKLIST.md "
            + "'Email subsystem (deferred)' row, and the deep-review FINDING-4 "
            + "note in this file's class-level summary on the activation path.");
    }
}
