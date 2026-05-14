// Spec 021 — see specs/021-feedback-session-may13/research.md R-1
// and data-model.md (PublicCode value object).

using System.Security.Cryptography;
using FundingPlatform.Domain.Interfaces;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FundingPlatform.Infrastructure.PublicCodes;

/// <summary>
/// Spec 021 / FR-008 / R-1 — emits a fresh <see cref="PublicCode"/> not
/// currently present in <c>dbo.Applications.PublicCode</c>. Draws 5 bytes of
/// crypto-RNG, encodes to an 8-character Crockford-base32 string (alphabet
/// <c>[A-HJ-NP-Z2-9]</c>; excludes 0/1/I/L/O/U for dictation safety), and
/// splits as 4-4 with a literal hyphen.
///
/// Uniqueness strategy: pre-check via <c>AnyAsync</c> against the existing
/// rows, then return. On the rare race where another insert wins between the
/// check and persist, the caller's <c>SaveChangesAsync</c> raises a
/// <c>DbUpdateException</c> wrapping <c>SqlException 2627</c> (UNIQUE index
/// violation on <c>UX_Applications_PublicCode</c>) — the caller catches that
/// and re-invokes <see cref="GenerateAsync"/>. Inside this method we also
/// retry up to <see cref="MaxAttempts"/> times against the pre-check; on the
/// 4th attempt we log + throw (NEVER user-surfaced per FR-008).
/// </summary>
public sealed class PublicCodeGenerator : IPublicCodeGenerator
{
    /// <summary>R-1 — 3 retries then throw on the 4th attempt.</summary>
    public const int MaxAttempts = 4;

    /// <summary>
    /// Crockford-base32 alphabet excluding 0/1/I/L/O/U. 32 symbols total,
    /// matching the <c>[A-HJ-NP-Z2-9]</c> pattern stamped on the DB CHECK
    /// constraint and the <see cref="PublicCode.Pattern"/> regex.
    /// </summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly AppDbContext _db;
    private readonly ILogger<PublicCodeGenerator> _logger;

    public PublicCodeGenerator(AppDbContext db, ILogger<PublicCodeGenerator> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PublicCode> GenerateAsync(CancellationToken ct = default)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var candidate = NextCandidate();

            // Project to the string column directly via EF.Property so the LINQ
            // translation does not depend on the PublicCode value-object converter
            // (configured in ApplicationConfiguration via HasConversion).
            var candidateValue = candidate.Value;
            var collision = await _db.Applications
                .AsNoTracking()
                .AnyAsync(a => EF.Property<string>(a, "PublicCode") == candidateValue, ct)
                .ConfigureAwait(false);

            if (!collision)
            {
                if (attempt > 1)
                {
                    _logger.LogInformation(
                        "PublicCode collision recovered after {Attempt} attempts (issued {Code}).",
                        attempt, candidate.Value);
                }
                return candidate;
            }

            _logger.LogWarning(
                "PublicCode collision on attempt {Attempt}/{Max} (candidate {Code}).",
                attempt, MaxAttempts, candidate.Value);
        }

        // R-1 — exhaustion is alerted on, never user-surfaced. The thrown
        // exception is caught at the request boundary and mapped to a generic
        // 500 by the global handler.
        _logger.LogError(
            "PublicCode generator exhausted {Max} attempts; aborting (per R-1).",
            MaxAttempts);
        throw new InvalidOperationException(
            $"PublicCodeGenerator exhausted {MaxAttempts} attempts. See logs for collision detail.");
    }

    /// <summary>
    /// Draws 5 cryptographic bytes and renders them as an 8-char Crockford-base32
    /// string split as <c>NNNN-NNNN</c>. 5 bytes = 40 bits exactly = 8 × 5-bit
    /// alphabet indices, so no padding is required.
    /// </summary>
    private static PublicCode NextCandidate()
    {
        Span<byte> entropy = stackalloc byte[5];
        RandomNumberGenerator.Fill(entropy);

        Span<char> chars = stackalloc char[9];
        // Bits 0..39, packed big-endian across 5 bytes; each 5-bit slice is an
        // alphabet index.
        long bits = ((long)entropy[0] << 32)
                  | ((long)entropy[1] << 24)
                  | ((long)entropy[2] << 16)
                  | ((long)entropy[3] << 8)
                  |  (long)entropy[4];

        // 8 indices, each 5 bits, from MSB → LSB.
        chars[0] = Alphabet[(int)((bits >> 35) & 0x1F)];
        chars[1] = Alphabet[(int)((bits >> 30) & 0x1F)];
        chars[2] = Alphabet[(int)((bits >> 25) & 0x1F)];
        chars[3] = Alphabet[(int)((bits >> 20) & 0x1F)];
        chars[4] = '-';
        chars[5] = Alphabet[(int)((bits >> 15) & 0x1F)];
        chars[6] = Alphabet[(int)((bits >> 10) & 0x1F)];
        chars[7] = Alphabet[(int)((bits >>  5) & 0x1F)];
        chars[8] = Alphabet[(int) (bits        & 0x1F)];

        return new PublicCode(new string(chars));
    }
}
