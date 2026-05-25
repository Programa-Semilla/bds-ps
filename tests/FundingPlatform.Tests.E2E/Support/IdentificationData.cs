using System.Security.Cryptography;
using System.Text;

namespace FundingPlatform.Tests.E2E.Support;

/// <summary>
/// Spec 026 — generates valid (and invalid) canonical Costa-Rican identification
/// values for E2E tests. All generators are deterministic in their seed so a test
/// can produce the same value twice (create then look up) and so parallel tests
/// using distinct unique-id seeds get distinct, collision-safe values.
///
/// Canonical forms (mirror Domain.ValueObjects.Identification):
///   - Cédula física  : 1-2345-6789      (9 digits, 1-4-4)
///   - Cédula jurídica: 3-101-123456     (10 digits, 1-3-6)  — also NITE shape
///   - DIMEX          : 11–12 plain digits
///   - Pasaporte      : uppercased alphanumerics, ≤20
/// </summary>
public static class IdentificationData
{
    private static string DigitsFromSeed(string seed, int count)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed ?? string.Empty));
        var sb = new StringBuilder(count);
        var i = 0;
        while (sb.Length < count)
        {
            sb.Append((char)('0' + bytes[i % bytes.Length] % 10));
            i++;
        }
        return sb.ToString(0, count);
    }

    /// <summary>Valid cédula física, e.g. <c>1-2345-6789</c>.</summary>
    public static string CedulaFisica(string seed)
    {
        var d = DigitsFromSeed(seed, 9);
        return $"{d[0]}-{d.Substring(1, 4)}-{d.Substring(5, 4)}";
    }

    /// <summary>Valid cédula jurídica, e.g. <c>3-101-123456</c>. First digit fixed at 3.</summary>
    public static string CedulaJuridica(string seed)
    {
        var d = DigitsFromSeed(seed, 9);
        return $"3-{d.Substring(0, 3)}-{d.Substring(3, 6)}";
    }

    /// <summary>NITE shares the cédula-jurídica canonical shape.</summary>
    public static string Nite(string seed) => CedulaJuridica(seed);

    /// <summary>Valid DIMEX — 12 plain digits.</summary>
    public static string Dimex(string seed) => "1" + DigitsFromSeed(seed, 11);

    /// <summary>Valid passport — uppercased alphanumerics, ≤20.</summary>
    public static string Pasaporte(string seed) => "PA" + DigitsFromSeed(seed, 6);

    /// <summary>A value that fails every numeric mask (letters present, wrong length).</summary>
    public const string InvalidNumeric = "12AB34";
}
