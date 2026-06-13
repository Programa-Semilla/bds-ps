using System.Globalization;
using System.Text;

namespace FundingPlatform.Application.Admin.Users.Batch;

/// <summary>
/// Spec 034 — canonical CSV header contract for the bulk applicant upload. Owns
/// the column names + order shared by the template download, file-level header
/// validation, and column→cell mapping. Header labels are es-CR and match the
/// intake spreadsheet (see contracts/contracts.md).
/// </summary>
public static class BatchUserCsvColumns
{
    public const string Grupo = "Grupo";
    public const string Proceso = "Proceso";
    public const string Fondo = "Fondo";
    public const string Nombre = "Nombre";
    public const string Apellido1 = "Apellido 1";
    public const string Apellido2 = "Apellido 2";
    public const string Email = "Email";
    public const string Telefono = "Teléfono";
    public const string Cedula = "Cédula";
    public const string CodigoUsuario = "Código de usuario";

    /// <summary>Canonical header columns in template order.</summary>
    public static readonly IReadOnlyList<string> Ordered =
    [
        Grupo, Proceso, Fondo, Nombre, Apellido1, Apellido2, Email, Telefono, Cedula, CodigoUsuario,
    ];

    /// <summary>Number of columns (also the cell count of every data row).</summary>
    public const int Count = 10;

    /// <summary>FR-003 — maximum number of data rows (header excluded) per upload.</summary>
    public const int MaxDataRows = 200;

    /// <summary>
    /// True when <paramref name="header"/> matches the canonical template: same
    /// column count and order, compared trim + case/accent-insensitive, tolerating
    /// a leading UTF-8 BOM on the first column (Excel "CSV UTF-8" export).
    /// </summary>
    public static bool HeaderMatches(IReadOnlyList<string>? header)
    {
        if (header is null || header.Count != Ordered.Count)
        {
            return false;
        }
        for (var i = 0; i < Ordered.Count; i++)
        {
            if (!NormalizeKey(header[i]).Equals(NormalizeKey(Ordered[i]), StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Strips a single leading UTF-8 BOM, if present.</summary>
    public static string StripBom(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.TrimStart('﻿');

    /// <summary>
    /// Normalizes a header cell for comparison: strip BOM, trim, remove accents
    /// (NFD decompose + drop combining marks), lowercase (invariant). Mirrors the
    /// spec-031 accent-insensitive es-CR matching done client-side.
    /// </summary>
    internal static string NormalizeKey(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }
        var trimmed = StripBom(raw).Trim();
        var decomposed = trimmed.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }
}
