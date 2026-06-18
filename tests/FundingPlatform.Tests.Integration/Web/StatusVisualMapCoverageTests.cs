using System.Text.RegularExpressions;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Web.Helpers;
using FundingPlatform.Web.Models;

namespace FundingPlatform.Tests.Integration.Web;

/// <summary>
/// Spec 012 / FR-009 / SC-011 — every domain enum value MUST return a non-empty
/// Spanish DisplayLabel from the StatusVisualMap registry.
///
/// The "Spanish character" gate uses a regex over diacritic letters (áéíóúñ).
/// A small allowlist permits ASCII-only Spanish words (e.g., "Borrador",
/// "Aprobado") that are valid Spanish without diacritics; English regressions
/// would not be on this allowlist.
/// </summary>
[TestFixture]
public class StatusVisualMapCoverageTests
{
    private static readonly Regex SpanishDiacriticPattern = new(
        @"[áéíóúñÁÉÍÓÚÑ]",
        RegexOptions.Compiled);

    /// <summary>
    /// Spanish words that happen to contain no diacritics. Pinned explicitly so
    /// any new entry must be reviewed against the voice guide.
    /// </summary>
    private static readonly HashSet<string> AllowedAsciiSpanishLabels = new(StringComparer.Ordinal)
    {
        "Borrador",
        "Enviada",
        "Resuelta",
        "Pendiente",
        "Aprobado",
        "Rechazado",
        "Aprobada",
        "Rechazada",
        "Reemplazada",
        "Retirada",
        "Abierta",
        "Activo",
        "Inhabilitado",
        "Solicitante",
        "Revisor",
        "Administrador",
        // Spec 021 / FR-007 — SupplierAdmin role display label.
        "Auditor",
        "Respuesta finalizada",
        "Convenio ejecutado",
    };

    [TestCaseSource(nameof(ApplicationStateValues))]
    public void ApplicationState_returns_non_empty_spanish_label(ApplicationState value)
    {
        var visual = StatusVisualMap.For(value);
        AssertSpanishLabel(visual.DisplayLabel, value.ToString());
    }

    [TestCaseSource(nameof(ItemReviewStatusValues))]
    public void ItemReviewStatus_returns_non_empty_spanish_label(ItemReviewStatus value)
    {
        var visual = StatusVisualMap.For(value);
        AssertSpanishLabel(visual.DisplayLabel, value.ToString());
    }

    [TestCaseSource(nameof(AppealStatusValues))]
    public void AppealStatus_returns_non_empty_spanish_label(AppealStatus value)
    {
        var visual = StatusVisualMap.For(value);
        AssertSpanishLabel(visual.DisplayLabel, value.ToString());
    }

    [TestCaseSource(nameof(SignedUploadStatusValues))]
    public void SignedUploadStatus_returns_non_empty_spanish_label(SignedUploadStatus value)
    {
        var visual = StatusVisualMap.For(value);
        AssertSpanishLabel(visual.DisplayLabel, value.ToString());
    }

    [TestCaseSource(nameof(AdminUserStatusValues))]
    public void AdminUserStatus_returns_non_empty_spanish_label(AdminUserStatus value)
    {
        var visual = StatusVisualMap.For(value);
        AssertSpanishLabel(visual.DisplayLabel, value.ToString());
    }

    [TestCaseSource(nameof(AdminUserRoleValues))]
    public void AdminUserRole_returns_non_empty_spanish_label(AdminUserRole value)
    {
        var visual = StatusVisualMap.For(value);
        AssertSpanishLabel(visual.DisplayLabel, value.ToString());
    }

    private static void AssertSpanishLabel(string label, string enumValue)
    {
        Assert.That(
            label,
            Is.Not.Null.And.Not.Empty,
            $"StatusVisualMap returned empty DisplayLabel for {enumValue}");

        var hasDiacritic = SpanishDiacriticPattern.IsMatch(label);
        var isAllowedAscii = AllowedAsciiSpanishLabels.Contains(label);

        Assert.That(
            hasDiacritic || isAllowedAscii,
            Is.True,
            $"DisplayLabel '{label}' for {enumValue} does not look Spanish " +
            $"(no diacritic and not on the allowed ASCII allowlist). " +
            $"This may be an English regression.");
    }

    public static IEnumerable<ApplicationState> ApplicationStateValues =>
        Enum.GetValues<ApplicationState>();

    public static IEnumerable<ItemReviewStatus> ItemReviewStatusValues =>
        Enum.GetValues<ItemReviewStatus>();

    public static IEnumerable<AppealStatus> AppealStatusValues =>
        Enum.GetValues<AppealStatus>();

    public static IEnumerable<SignedUploadStatus> SignedUploadStatusValues =>
        Enum.GetValues<SignedUploadStatus>();

    public static IEnumerable<AdminUserStatus> AdminUserStatusValues =>
        Enum.GetValues<AdminUserStatus>();

    public static IEnumerable<AdminUserRole> AdminUserRoleValues =>
        Enum.GetValues<AdminUserRole>();
}
