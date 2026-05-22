// Spec 021 — see specs/021-feedback-session-may13/tasks.md T143/T144 and
// contracts/public-routes.md (Public landing).

namespace FundingPlatform.Web.ViewModels.Public;

/// <summary>
/// Spec 021 / US7 / T144 / FR-031 — model for the anonymous public landing.
/// Two flags decide whether each slot card renders a download link or the
/// shared *Próximamente* placeholder; sponsor strip and hero CTA copy live
/// directly in the view (sponsor strip is the spec 019 reused partial; hero
/// copy is sourced from `Localization/021.es-CR.resx` keys
/// `Public.Hero.Cta` / `Public.Hero.Button`).
/// </summary>
public sealed record PublicLandingViewModel(
    bool ReglamentoAvailable,
    bool EjemploAvailable);
