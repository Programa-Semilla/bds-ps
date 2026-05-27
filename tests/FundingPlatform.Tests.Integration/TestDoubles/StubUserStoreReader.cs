using FundingPlatform.Application.Services;

namespace FundingPlatform.Tests.Integration.TestDoubles;

/// <summary>
/// Spec 027 / US1 — context-free <see cref="IUserStoreReader"/> stub for tests
/// that construct <c>FundingAgreementService</c>/<c>SignedUploadService</c> but
/// never exercise the display-name resolution path (e.g. generation/persistence
/// tests). Resolution-sensitive tests use the real <c>UserStoreReader</c>.
/// </summary>
public sealed class StubUserStoreReader : IUserStoreReader
{
    public Task<int> GetActiveUserCountAsync(CancellationToken ct) => Task.FromResult(0);

    // Mirrors the real ladder's terminal fallback (returns the id) so callers
    // that do read it observe the documented graceful-degradation shape.
    public Task<string> GetDisplayNameAsync(string userId, CancellationToken ct)
        => Task.FromResult(userId);
}
