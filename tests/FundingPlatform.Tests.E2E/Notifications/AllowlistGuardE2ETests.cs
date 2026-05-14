using FundingPlatform.Tests.E2E.Fixtures;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 021 / T077 / US7 / SC-004 — full Aspire boot with an explicit
/// <c>Notifications:NonProdAllowlist=[]</c> override; assert
/// <c>MailCapture.ListAsync()</c> returns 0 messages even after a workflow
/// event fires.
///
/// <para>
/// The integration test (<see cref="AllowlistFailClosedTests"/>) covers the
/// fail-closed contract end-to-end through the worker. The live Aspire run
/// is deferred to T086 since it requires overriding AppHost configuration
/// per-test, which the shared fixture does not currently support.
/// </para>
/// </summary>
public class AllowlistGuardE2ETests : AuthenticatedTestBase
{
    [Test]
    public void Empty_allowlist_under_aspire_blocks_every_recipient()
    {
        Assert.Ignore(
            "Spec 021 / T077 — Aspire override of Notifications:NonProdAllowlist " +
            "deferred to T086. SC-004 fail-closed semantics covered by " +
            "AllowlistFailClosedTests + RecipientAllowlistFilterTests.");
    }
}
