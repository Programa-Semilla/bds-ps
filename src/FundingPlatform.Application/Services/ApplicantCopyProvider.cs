namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 011 — applicant-facing voice-guide-compliant copy strings.
/// Centralized so a single voice-guide pass lints them in one place.
/// Spec 012 — translated to formal Costa Rican Spanish (formal usted).
/// See specs/012-es-cr-localization/voice-guide.md.
/// </summary>
public interface IApplicantCopyProvider
{
    string WelcomeHeadline(string firstName);
    string WelcomeSubhead();
    string AwaitingActionDraft(string projectName);
    string AwaitingActionSentBack(string projectName);
    string AwaitingActionAgreement(string projectName);

    // Spec 012 — action-button labels surfaced from the dashboard projection.
    // Localized here (rather than hardcoded in the projection) so the voice
    // guide stays the single source of truth for applicant-facing copy.
    string ActionSignAgreement();
    string ActionAddMissingDetails();
    string ActionContinueApplication();
    string ActionOpenApplication();

    string EmptyHeroHeadline();
    string EmptyHeroSubhead();
    string EmptyCtaLabel();
    string ResourcesHowFundingWorks();
    string ResourcesSubmissionTips();
    string ResourcesGetHelp();
    string TrustHowLongTitle();
    string TrustHowLongBody();
    string TrustWhatYouNeedTitle();
    string TrustWhatYouNeedBody();
    string TrustHowDecisionsTitle();
    string TrustHowDecisionsBody();
}

public sealed class ApplicantCopyProvider : IApplicantCopyProvider
{
    // Spec 021 / US7 / T149 / FR-030 — "Hola, {Nombre}" replaces the legacy
    // "Bienvenido de vuelta, ..." welcome. Mirrors the resx key
    // `Greeting.Pattern` ("Hola, {0}") used elsewhere on applicant-facing
    // surfaces; kept as an inline literal because the project doesn't wire
    // IStringLocalizer into Application services.
    public string WelcomeHeadline(string firstName)
        => $"Hola, {firstName}";

    public string WelcomeSubhead()
        => "Hemos llevado el registro de todo desde su última visita.";

    public string AwaitingActionDraft(string projectName)
        => $"Su borrador para {projectName} está listo para enviar.";

    public string AwaitingActionSentBack(string projectName)
        => $"Necesitamos algunos detalles más sobre {projectName} antes de decidir.";

    // Spec 021 / US7 / T149 / FR-029 — "convenio de financiamiento" is the
    // legal Funding Agreement document name (legal-term carve-out), but this
    // string is rendered on the *applicant-facing* awaiting-action banner where
    // FR-029 mandates removal. The banner is informational, not a label of the
    // legal entity itself, so we drop the term in favour of generic acompañamiento copy.
    public string AwaitingActionAgreement(string projectName)
        => $"Su convenio para {projectName} está listo para firmar.";

    public string ActionSignAgreement()      => "Firmar convenio";
    public string ActionAddMissingDetails()  => "Agregar los detalles faltantes";
    public string ActionContinueApplication() => "Continuar con la solicitud";
    public string ActionOpenApplication()    => "Abrir solicitud";

    // Spec 021 / US7 / T149 / FR-029 — "financiamiento" replaced with
    // "acompañamiento" on applicant-facing copy.
    public string EmptyHeroHeadline() => "¿Listo para solicitar acompañamiento?";
    public string EmptyHeroSubhead() => "Cuéntenos sobre su proyecto — le acompañamos en el resto del camino.";
    public string EmptyCtaLabel() => "Iniciar una nueva solicitud";

    // Spec 021 / US7 / T149 / FR-029 — same sweep.
    public string ResourcesHowFundingWorks() => "Cómo funciona el acompañamiento";
    public string ResourcesSubmissionTips()  => "Consejos para enviar su solicitud";
    public string ResourcesGetHelp()         => "Obtener ayuda";

    public string TrustHowLongTitle() => "Cuánto tarda";
    public string TrustHowLongBody()  => "La mayoría de las solicitudes recibe una decisión en 3 semanas desde su envío.";
    public string TrustWhatYouNeedTitle() => "Lo que necesitará";
    public string TrustWhatYouNeedBody()  => "Una breve descripción del proyecto, la lista de ítems y una cotización por ítem.";
    public string TrustHowDecisionsTitle() => "Cómo se toman las decisiones";
    public string TrustHowDecisionsBody()  => "Los revisores verifican que la solicitud esté completa, su pertinencia y las cotizaciones — le explicamos la decisión sea cual sea.";
}
