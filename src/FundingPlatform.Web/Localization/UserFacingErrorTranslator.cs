using FundingPlatform.Application.Errors;

namespace FundingPlatform.Web.Localization;

/// <summary>
/// Spec 012 / FR-014 — translates Application-layer
/// <see cref="UserFacingErrorCode"/> values into the Spanish (es-CR) string
/// surfaced to the user via TempData / ModelState.
///
/// <para>
/// Lives in the Web layer so the Application layer can stay English (NFR-001).
/// Strings are hard-coded inline here — NFR-003 forbids
/// <c>IStringLocalizer</c> / <c>.resx</c> machinery; the static-class approach
/// matches the existing convention from <c>AdminErrorMessages</c>.
/// </para>
/// </summary>
public interface IUserFacingErrorTranslator
{
    /// <summary>Render <paramref name="error"/> as the Spanish text shown to
    /// the user. Never returns null or empty; falls back to a generic
    /// "operación no se pudo completar" string for unmapped codes.</summary>
    string Translate(UserFacingError error);

    /// <summary>Convenience overload for when only a code is on hand (no Detail).</summary>
    string Translate(UserFacingErrorCode code);

    /// <summary>
    /// Spec 021 / FR-014 — render the funding-agreement panel
    /// <c>DisabledReason</c> in es-CR. The Application layer builds this string
    /// from English domain preconditions (<c>CanGenerate/CanRegenerateFundingAgreement</c>),
    /// which must not reach the applicant surface (NFR-001 + acompañamiento copy
    /// pivot — no "financiamiento"). Returns null for null/blank input; falls
    /// back to a generic Spanish phrase for any unmapped domain string.
    /// </summary>
    string? TranslateAgreementDisabledReason(string? englishReason);
}

/// <inheritdoc />
public sealed class UserFacingErrorTranslator : IUserFacingErrorTranslator
{
    public string Translate(UserFacingError error) => error.Code switch
    {
        // Spec 039 / FR-019 — incorporate the (data, not English copy) provider name
        // from Detail into the es-CR block message naming the offending provider.
        UserFacingErrorCode.SupplierCcssSinInscripcion when !string.IsNullOrWhiteSpace(error.Detail) =>
            $"No se puede aprobar el ítem: el proveedor «{error.Detail}» no está inscrito en la CCSS.",
        // Spec 043 / FR-007 — the Detail is the data-driven es-CR block message
        // (provider + field + last-reviewed) built by RegulatoryFreshnessCopy.
        UserFacingErrorCode.RegulatoryDataStale when !string.IsNullOrWhiteSpace(error.Detail) =>
            error.Detail!,
        _ => Translate(error.Code),
    };

    public string Translate(UserFacingErrorCode code) => code switch
    {
        // Domain rule rejection — the Detail string is intentionally NOT
        // rendered (it is English from the domain). FR-14 / NFR-001: a
        // generic Spanish phrase wins.
        UserFacingErrorCode.OperationRejected =>
            "La operación no se pudo completar. Inténtelo nuevamente o contacte al soporte.",

        // Application aggregate
        UserFacingErrorCode.ApplicationNotFound =>
            "Solicitud no encontrada.",
        UserFacingErrorCode.ApplicationNotUnderReview =>
            "La solicitud no está en revisión.",
        UserFacingErrorCode.ApplicationItemNotFound =>
            "Ítem no encontrado en la solicitud.",
        UserFacingErrorCode.ApplicationNotOwnedByApplicant =>
            "Usted no es el dueño de esta solicitud.",
        UserFacingErrorCode.SupplierRequiredOnApprove =>
            "Debe seleccionar un proveedor para aprobar el ítem.",
        UserFacingErrorCode.InvalidReviewDecision =>
            "Decisión de revisión no válida.",
        UserFacingErrorCode.ConcurrentApplicationModification =>
            "Otro usuario modificó esta solicitud. Refresque la página e inténtelo nuevamente.",

        // Appeal aggregate
        UserFacingErrorCode.AppealAccessDenied =>
            "Usted no tiene acceso a esta apelación.",
        UserFacingErrorCode.NoOpenAppealForMessage =>
            "No hay una apelación abierta para responder.",
        UserFacingErrorCode.UnknownAppealResolution =>
            "Resolución de apelación no reconocida.",
        UserFacingErrorCode.ConcurrentAppealModification =>
            "Otro usuario modificó esta apelación. Refresque la página e inténtelo nuevamente.",

        // Funding-agreement aggregate
        UserFacingErrorCode.AgreementGenerationPreconditionsNotMet =>
            "No se cumplen las precondiciones del convenio de financiamiento.",
        UserFacingErrorCode.AgreementRegenerationPreconditionsNotMet =>
            "No se cumplen las precondiciones para regenerar el convenio.",
        UserFacingErrorCode.AgreementPdfRenderingFailed =>
            "No se pudo generar el convenio. Inténtelo nuevamente o contacte al soporte.",
        UserFacingErrorCode.AgreementGenerationFailed =>
            "Falló la generación del convenio de financiamiento.",
        UserFacingErrorCode.ConcurrentAgreementModification =>
            "Otro usuario modificó este convenio. Refresque la página e inténtelo nuevamente.",

        // Signed upload (resource not found / authz)
        UserFacingErrorCode.SignedUploadResourceNotFound =>
            "Recurso no encontrado.",
        UserFacingErrorCode.ConcurrentSignedUploadModification =>
            "Otra acción modificó esta carga. Refresque la página e inténtelo nuevamente.",

        // Signed upload (validation)
        UserFacingErrorCode.SignedUploadStaleAgreementVersion =>
            "Descargue nuevamente el convenio más reciente y fírmelo otra vez.",
        UserFacingErrorCode.SignedUploadAlreadyPending =>
            "Ya existe una carga firmada pendiente. Use Reemplazar.",
        UserFacingErrorCode.SignedUploadNoPendingToReplace =>
            "No hay una carga pendiente para reemplazar; use Cargar.",
        UserFacingErrorCode.SignedUploadWrongPendingId =>
            "La carga indicada no es la pendiente actual.",
        UserFacingErrorCode.SignedUploadNoPendingToWithdraw =>
            "No hay una carga pendiente para retirar.",
        UserFacingErrorCode.SignedUploadStalePendingId =>
            "El identificador de la carga pendiente no es válido; refresque la página.",
        UserFacingErrorCode.SignedUploadNoPendingToApprove =>
            "No hay una carga pendiente para aprobar.",
        UserFacingErrorCode.SignedUploadNoPendingToReject =>
            "No hay una carga pendiente para rechazar.",
        UserFacingErrorCode.SignedUploadRejectionCommentRequired =>
            "Se requiere un comentario para rechazar la carga.",

        // Signed upload (intake validation)
        UserFacingErrorCode.SignedUploadUnsupportedContentType =>
            "Solo se aceptan archivos PDF (application/pdf).",
        UserFacingErrorCode.SignedUploadFileEmpty =>
            "El archivo cargado está vacío.",
        UserFacingErrorCode.SignedUploadFileTooLarge =>
            "El archivo excede el tamaño máximo permitido.",
        UserFacingErrorCode.SignedUploadContentUnreadable =>
            "No se pudo leer el contenido del archivo cargado.",
        UserFacingErrorCode.SignedUploadNotAPdf =>
            "El archivo cargado no parece ser un PDF.",
        UserFacingErrorCode.SignedUploadMissingPdfHeader =>
            "El archivo cargado no parece ser un PDF (falta el encabezado %PDF-).",

        // Spec 015 — multi-currency quotes
        UserFacingErrorCode.MissingExchangeRate =>
            "No hay tipo de cambio de referencia configurado. Contacte a un administrador.",
        UserFacingErrorCode.CurrencyDisabled =>
            "La moneda seleccionada está deshabilitada.",
        UserFacingErrorCode.RateImmutableUseSupersede =>
            "Este tipo de cambio ya fue utilizado y no puede modificarse. Publique uno nuevo para reemplazarlo.",
        UserFacingErrorCode.DuplicateRateTimestamp =>
            "Ya existe un tipo de cambio publicado para ese instante.",
        UserFacingErrorCode.FutureDatedRateRejected =>
            "El tipo de cambio no puede tener una fecha de vigencia en el futuro.",

        // Spec 018 — applicant CompanyName invariants (FR-015 / FR-016)
        UserFacingErrorCode.CompanyNameRequired =>
            "Debe ingresar el nombre de la empresa.",
        UserFacingErrorCode.CompanyNameTooLong =>
            "El nombre de la empresa no puede exceder 200 caracteres.",

        // Spec 037 — applicant company selection (FR-018 / FR-019, no disclosure) +
        // admin company management messages (single source of truth; canonical es-CR
        // strings from contracts/interfaces.md). The admin surfaces render these via
        // this same translator so the wording never diverges.
        UserFacingErrorCode.CompanyInvalid =>
            "Debe seleccionar una empresa válida.",
        UserFacingErrorCode.CompanyNameDuplicate =>
            "Ya existe una empresa activa con ese nombre para este solicitante.",
        UserFacingErrorCode.CompanyArchiveLastActive =>
            "No puede archivar la única empresa activa del solicitante.",
        UserFacingErrorCode.CompanyUnarchiveNameCollision =>
            "No se puede reactivar: ya existe una empresa activa con ese nombre.",

        // Spec 018 — reviewer LineCode invariants (FR-012 / FR-013 / FR-014)
        UserFacingErrorCode.LineCodeRequired =>
            "Debe ingresar un código de línea.",
        UserFacingErrorCode.LineCodeTooLong =>
            "El código de línea no puede exceder 16 caracteres.",
        UserFacingErrorCode.LineCodeDuplicate =>
            "Ya existe otro ítem con el mismo código de línea en esta solicitud.",
        UserFacingErrorCode.LineCodeMissingOnApprovedItems =>
            "Falta el código de línea en uno o más ítems aprobados.",

        // Spec 039 / FR-019 — code-only fallback (provider name unavailable).
        UserFacingErrorCode.SupplierCcssSinInscripcion =>
            "No se puede aprobar el ítem: el proveedor no está inscrito en la CCSS.",

        // Spec 043 / FR-007 — code-only fallback (enumerated detail unavailable).
        UserFacingErrorCode.RegulatoryDataStale =>
            FundingPlatform.Application.Regulatory.RegulatoryFreshnessCopy.BlockHeading,

        _ => "La operación no se pudo completar. Inténtelo nuevamente o contacte al soporte.",
    };

    public string? TranslateAgreementDisabledReason(string? englishReason)
    {
        if (string.IsNullOrWhiteSpace(englishReason))
        {
            return null;
        }

        // Closed set of English precondition strings emitted by
        // Application.CanGenerateFundingAgreement / CanRegenerateFundingAgreement.
        // Spanish targets avoid "financiamiento" (applicant-surface copy pivot).
        return englishReason.Trim() switch
        {
            "An appeal is currently open on this application." =>
                "Hay una apelación abierta en esta solicitud.",
            "Review is still in progress." =>
                "La revisión aún está en curso.",
            "Applicant has not yet responded to every approved item." =>
                "Aún no se ha respondido a todos los ítems aprobados.",
            "Nothing to fund: all items were rejected." =>
                "No hay ítems aprobados para incluir en el convenio.",
            "No Funding Agreement exists to regenerate." =>
                "No existe un convenio para regenerar.",
            "Agreement is locked: a signed upload has been submitted." =>
                "El convenio está bloqueado: ya se cargó un documento firmado.",
            _ => "Aún no se cumplen las condiciones para generar el convenio.",
        };
    }
}
