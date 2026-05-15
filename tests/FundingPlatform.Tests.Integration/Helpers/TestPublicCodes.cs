using System.Threading;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Tests.Integration.Helpers;

/// <summary>
/// Spec 021-feedback-session-may13 / FR-008 — `Application.PublicCode` is
/// required at the EF mapping. Production callers route through
/// <c>ApplicationService.CreateApplicationAsync</c> which stamps a generated
/// code before the first save. Test seeders that construct an
/// <c>Application</c> directly must assign one explicitly; this helper hands
/// out unique base32 (A-HJ-NP-Z2-9) codes in the <c>XXXX-XXXX</c> shape.
/// </summary>
internal static class TestPublicCodes
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static int _seed = 0;

    public static PublicCode Next()
    {
        var n = Interlocked.Increment(ref _seed);
        var chars = new char[9];
        for (var i = 0; i < 4; i++)
        {
            chars[i] = Alphabet[(n >> (i * 5)) & 31];
        }
        chars[4] = '-';
        for (var i = 0; i < 4; i++)
        {
            chars[5 + i] = Alphabet[((n + 17) >> (i * 5)) & 31];
        }
        return new PublicCode(new string(chars));
    }
}
