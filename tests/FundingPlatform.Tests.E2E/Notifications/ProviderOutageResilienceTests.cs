using FundingPlatform.Tests.E2E.Fixtures;

namespace FundingPlatform.Tests.E2E.Notifications;

/// <summary>
/// Spec 021 / T071 / US6 / SC-007 — provider-outage resilience. Pauses the
/// smtp4dev sidecar via Docker, fires three workflow events, resumes the
/// sidecar, asserts the outbox rows reach <c>Status=Done</c> within 2 minutes
/// with exactly one captured email each.
///
/// <para>
/// The Docker pause/unpause path requires the Aspire fixture to expose the
/// container id; the unit + integration test surface
/// (<see cref="DeadLetterPathTests"/> + <c>IdempotencyDoubleProcessTests</c>)
/// covers the FR-021 backoff math + FR-022 dead-letter semantics directly.
/// The full live test is deferred to T086 where the Aspire stack is up and
/// `docker pause`/`docker unpause` is wired against the smtp4dev resource id.
/// </para>
/// </summary>
public class ProviderOutageResilienceTests : AuthenticatedTestBase
{
    [Test]
    public void Sidecar_outage_then_recovery_loses_no_emails_and_creates_no_duplicates()
    {
        Assert.Ignore(
            "Spec 021 / T071 — Docker pause/unpause E2E deferred to T086. " +
            "FR-021 backoff + FR-022 dead-letter semantics covered by unit " +
            "(EmailDispatchWorkerTests) + integration (DeadLetterPathTests).");
    }
}
