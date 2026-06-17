namespace FundingPlatform.Application.Admin.Users.Batch;

/// <summary>
/// Spec 034 — es-CR per-row skip reasons surfaced in the batch report. These live
/// in the Application layer (not <c>Web.Resources</c>) because the producer is the
/// Infrastructure <c>UserAdministrationService</c>, and dependencies point inward
/// (Infrastructure cannot reference Web). The Web layer only renders these strings.
/// </summary>
public static class BatchUserRowReasons
{
    // Required cells
    public const string MissingNombre = "Falta el nombre.";
    public const string MissingApellido1 = "Falta el primer apellido.";
    public const string MissingGrupo = "Falta el grupo.";
    public const string MissingProceso = "Falta el proceso.";
    public const string MissingFondo = "Falta el fondo.";

    // Email
    public const string EmailBlank = "Falta el correo electrónico.";
    public const string EmailInvalid = "El correo electrónico no es válido.";
    public const string EmailInUse = "El correo ya está en uso.";
    public const string EmailDupInFile = "Correo duplicado en el archivo.";

    // Cédula
    public const string CedulaBlank = "Falta la cédula.";
    // Spec 034 — the type is inferred (cédula física / DIMEX / pasaporte), so the
    // reason is identification-generic rather than física-specific.
    public const string CedulaInvalid = "El número de identificación no es válido o no se pudo determinar su tipo.";
    public const string CedulaInUse = "La cédula ya está en uso.";
    public const string CedulaDupInFile = "Cédula duplicada en el archivo.";

    // Código de usuario
    public const string CodigoBlank = "Falta el código de usuario.";
    public const string CodigoTooLong = "El código de usuario supera los 50 caracteres.";
    public const string CodigoInUse = "El código de usuario ya está en uso.";
    public const string CodigoDupInFile = "Código de usuario duplicado en el archivo.";

    // Empresa (spec 037)
    public const string CompanyNameBlank = "Falta el nombre de la empresa.";
    public const string CompanyNameTooLong = "El nombre de la empresa supera los 200 caracteres.";

    // Grupo / Proceso / Fondo chain (spec 029)
    public const string GroupNotFound = "El grupo indicado no existe.";
    public const string ProcessNotFound = "El proceso indicado no existe.";
    public const string FundNotFound = "El fondo indicado no existe.";
    public const string ChainMismatch = "El grupo no pertenece al proceso o fondo indicado.";

    // Defensive fallback (should not normally occur — chain pre-validated)
    public const string CreateFailed = "No se pudo crear el usuario.";
}
