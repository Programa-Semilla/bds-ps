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
    public string WelcomeHeadline(string firstName)
        => $"Bienvenido de vuelta, {firstName} — esto es lo que tenemos hoy.";

    public string WelcomeSubhead()
        => "Hemos llevado el registro de todo desde su última visita.";

    public string AwaitingActionDraft(string projectName)
        => $"Su borrador para {projectName} está listo para enviar.";

    public string AwaitingActionSentBack(string projectName)
        => $"Necesitamos algunos detalles más sobre {projectName} antes de decidir.";

    public string AwaitingActionAgreement(string projectName)
        => $"Su convenio de financiamiento para {projectName} está listo para firmar.";

    public string ActionSignAgreement()      => "Firmar convenio";
    public string ActionAddMissingDetails()  => "Agregar los detalles faltantes";
    public string ActionContinueApplication() => "Continuar con la solicitud";
    public string ActionOpenApplication()    => "Abrir solicitud";

    public string EmptyHeroHeadline() => "¿Listo para solicitar financiamiento?";
    public string EmptyHeroSubhead() => "Cuéntenos sobre su proyecto — le acompañamos en el resto del camino.";
    public string EmptyCtaLabel() => "Iniciar una nueva solicitud";

    public string ResourcesHowFundingWorks() => "Cómo funciona el financiamiento";
    public string ResourcesSubmissionTips()  => "Consejos para enviar su solicitud";
    public string ResourcesGetHelp()         => "Obtener ayuda";

    public string TrustHowLongTitle() => "Cuánto tarda";
    public string TrustHowLongBody()  => "La mayoría de las solicitudes recibe una decisión en 3 semanas desde su envío.";
    public string TrustWhatYouNeedTitle() => "Lo que necesitará";
    public string TrustWhatYouNeedBody()  => "Una breve descripción del proyecto, la lista de ítems y una cotización por ítem.";
    public string TrustHowDecisionsTitle() => "Cómo se toman las decisiones";
    public string TrustHowDecisionsBody()  => "Los revisores verifican que la solicitud esté completa, su pertinencia y las cotizaciones — le explicamos la decisión sea cual sea.";
}
