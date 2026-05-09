namespace FundingPlatform.Tests.E2E.Constants;

/// <summary>
/// Spec 012 — Spanish UI text used by E2E selectors and assertions.
///
/// The platform's user-facing copy is es-CR Spanish (formal usted register
/// per voice guide). Tests assert on the rendered visible text verbatim;
/// when the registry-driven copy changes, this file is the single source
/// of truth that keeps POMs and shared fixtures in sync.
///
/// Kept as plain const strings — no localizer, no resx (NFR-003).
/// </summary>
public static class UiCopy
{
    // Brand (spec 019 — pivoted from "Capital Semilla" to "Programa Semilla").
    public const string BrandName = "Programa Semilla";

    // Auth
    public const string Login = "Iniciar sesión";
    public const string Logout = "Cerrar sesión";
    public const string Register = "Crear cuenta";
    public const string ChangePassword = "Cambiar contraseña";

    // Common
    public const string Cancel = "Cancelar";
    public const string SaveChanges = "Guardar cambios";
    public const string Apply = "Aplicar";
    public const string Edit = "Editar";
    public const string Remove = "Eliminar";
    public const string Delete = "Eliminar";
    public const string View = "Ver";
    public const string Open = "Abrir";
    public const string Back = "Atrás";
    public const string Yes = "Sí";
    public const string No = "No";
    public const string DownloadCsv = "Descargar CSV";

    // Application area
    public const string MyApplications = "Mis solicitudes";
    public const string CreateNewApplication = "Crear nueva solicitud";
    public const string CreateDraftApplication = "Crear borrador de solicitud";
    public const string SubmitApplication = "Enviar solicitud";
    public const string AddItem = "Agregar ítem";
    public const string AddSupplier = "Agregar proveedor";
    public const string AddQuotation = "Agregar cotización";
    public const string ApplicationCreatedSuccess = "Solicitud creada con éxito.";
    public const string ApplicationSubmittedSuccess = "Solicitud enviada con éxito.";

    // Item / Impact
    public const string ImpactAssessment = "Evaluación de impacto";
    public const string Impact = "Impacto";
    public const string SaveImpact = "Guardar impacto";
    public const string ItemAdded = "Ítem agregado con éxito.";
    public const string ItemUpdated = "Ítem actualizado con éxito.";
    public const string ImpactSaved = "Evaluación de impacto guardada con éxito.";

    // Supplier / Quotation
    public const string SupplierAndQuotationAdded = "Proveedor y cotización agregados con éxito.";
    public const string QuotationAdded = "Cotización agregada con éxito.";

    // Review
    public const string ReviewQueue = "Cola de revisión";
    public const string Review = "Revisar";
    public const string SendBack = "Devolver";
    public const string FinalizeReview = "Finalizar revisión";
    public const string ConfirmFinalization = "Confirmar finalización";
    public const string Approve = "Aprobar";
    public const string Reject = "Rechazar";
    public const string RequestMoreInfo = "Solicitar más información";
    public const string SaveDecision = "Guardar decisión";
    public const string FlagNotEquivalent = "Marcar como no técnicamente equivalente";
    public const string ClearNotEquivalent = "Eliminar marca de no equivalencia";
    public const string ItemDecisionRecorded = "Decisión del ítem registrada.";
    public const string ApplicationSentBack = "Solicitud devuelta al solicitante.";
    public const string ReviewFinalized = "Revisión finalizada. Solicitud resuelta.";
    public const string Recommended = "Recomendada";

    // Funding Agreement
    public const string FundingAgreement = "Convenio de financiamiento";
    public const string GenerateAgreement = "Generar convenio";
    public const string Regenerate = "Regenerar";
    public const string DownloadAgreement = "Descargar convenio";
    public const string DownloadSignedAgreement = "Descargar convenio firmado";
    public const string UploadSignedAgreement = "Cargar convenio firmado";
    public const string AgreementGenerated = "Convenio de financiamiento generado.";
    public const string AgreementExecuted = "Convenio ejecutado.";

    // Sign Inbox / Decisions
    public const string SigningInbox = "Bandeja de firmas";
    public const string ApproveAction = "Aprobar";
    public const string RejectAction = "Rechazar";
    public const string Replace = "Reemplazar carga pendiente";
    public const string Withdraw = "Retirar carga pendiente";

    // Applicant Response
    public const string Accept = "Aceptar";
    public const string SubmitResponse = "Enviar respuesta";
    public const string OpenAppeal = "Abrir apelación";
    public const string PostMessage = "Publicar mensaje";

    // Admin
    public const string AdminDashboard = "Panel de administración";
    public const string ImpactTemplates = "Plantillas de impacto";
    public const string SystemConfiguration = "Configuración del sistema";
    public const string CreateTemplate = "Crear plantilla";
    public const string CreateNewTemplate = "Crear nueva plantilla";
    public const string ManageTemplates = "Administrar plantillas";
    public const string ManageConfiguration = "Administrar configuración";
    public const string SaveConfiguration = "Guardar configuración";
    public const string AddParameter = "Agregar parámetro";

    // Admin Users
    public const string Users = "Usuarios";
    public const string CreateUser = "Crear usuario";
    public const string Disable = "Inhabilitar";
    public const string Enable = "Habilitar";
    public const string ResetPassword = "Restablecer contraseña";
    public const string UserCreated = "creado.";
    public const string UserUpdated = "actualizado.";
    public const string UserDisabled = "Usuario inhabilitado.";
    public const string UserEnabled = "Usuario habilitado.";

    // Admin Reports
    public const string Reports = "Reportes";
    public const string Applications = "Solicitudes";
    public const string Applicants = "Solicitantes";
    public const string FundedItems = "Ítems financiados";
    public const string Aging = "Antigüedad";

    // Empty states
    public const string NoApplications = "No hay solicitudes pendientes de revisión";
    public const string NoSystemConfigurations = "No se encontraron configuraciones del sistema";
    public const string NoApplicantsMatch = "Ningún solicitante coincide";
    public const string NoApplicationsMatch = "Ninguna solicitud coincide";
    public const string NoFundedItemsMatch = "Ningún ítem financiado coincide";
    public const string NoAgingMatch = "Ninguna solicitud antigua coincide";

    // Status labels (mirror of StatusVisualMap; intentional duplication —
    // the assertion is the contract per data-model.md §5)
    public static class State
    {
        // ApplicationState
        public const string Draft = "Borrador";
        public const string Submitted = "Enviada";
        public const string UnderReview = "En revisión";
        public const string Resolved = "Resuelta";
        public const string AppealOpen = "Apelación abierta";
        public const string ResponseFinalized = "Respuesta finalizada";
        public const string AgreementExecuted = "Convenio ejecutado";

        // ItemReviewStatus
        public const string Pending = "Pendiente";
        public const string Approved = "Aprobado";
        public const string Rejected = "Rechazado";
        public const string NeedsInfo = "Requiere información";

        // AppealStatus
        public const string Open = "Abierta";

        // SignedUploadStatus
        public const string SignedApproved = "Aprobada";
        public const string SignedRejected = "Rechazada";
        public const string Superseded = "Reemplazada";
        public const string Withdrawn = "Retirada";

        // AdminUserStatus / Role
        public const string Active = "Activo";
        public const string Disabled = "Inhabilitado";
        public const string Applicant = "Solicitante";
        public const string Reviewer = "Revisor";
        public const string Admin = "Administrador";
    }
}
