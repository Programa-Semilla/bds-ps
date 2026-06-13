namespace FundingPlatform.Web.Resources;

/// <summary>
/// Spec 027 / US7 (completes spec 021 FR-020 / OQ-8) — static es-CR hint copy
/// for applicant-facing form fields, keyed by a stable resource key. Values are
/// curated HTML (never user-supplied), so the tooltip renders them as HTML
/// safely. This deliberately replaces an <c>IStringLocalizer</c>/<c>.resx</c>
/// path: the project has no localization registration wired (NFR-003), and copy
/// is delivered via static providers like this one. First-pass copy — the
/// stakeholder refines later.
/// </summary>
public static class HintCopy
{
    private static readonly IReadOnlyDictionary<string, string> Entries =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Registro
            ["Register.Email"] = "Usá un correo vigente: ahí recibirás <strong>notificaciones</strong> y el enlace para restablecer tu contraseña.",
            ["Register.Password"] = "Combiná <strong>mayúsculas</strong>, <strong>minúsculas</strong> y <strong>números</strong>. Cuanto más larga, más segura.",
            ["Register.FirstName"] = "Tu nombre tal como aparece en tu documento de identidad.",
            ["Register.LastName"] = "Tus apellidos tal como aparecen en tu documento de identidad.",
            ["Register.LegalId"] = "Elegí el <strong>tipo de identificación</strong> y digitá el número. El formato se valida automáticamente.",

            // Solicitud
            ["Application.CompanyName"] = "Nombre de la <strong>empresa o emprendimiento</strong> que presenta la solicitud.",

            // Ítems
            ["Item.ProductName"] = "Describí el <strong>bien o servicio</strong> que necesitás (por ejemplo: «Computadora portátil»).",
            ["Item.Category"] = "Elegí la categoría que mejor describe el ítem. Ayuda a la persona revisora a clasificarlo.",

            // Impacto (spec 035 — ahora por ítem)
            ["Application.Impact"] = "Indicá el <strong>impacto esperado</strong> de cada ítem. Completá cada parámetro de la plantilla con datos reales.",

            // Cotización de proveedor
            ["Supplier.LegalId"] = "Cédula jurídica o física de la empresa proveedora. Si ya existe, el sistema la autocompleta.",
            ["Supplier.Price"] = "Monto cotizado, <strong>sin separadores de miles</strong> (por ejemplo: 125000.50).",
            ["Supplier.Currency"] = "Moneda de la cotización. Si no es en colones, se mostrará la conversión a ₡ con el tipo de cambio vigente.",
            ["Supplier.ValidUntil"] = "Fecha hasta la cual la empresa proveedora <strong>mantiene el precio</strong> cotizado.",
        };

    /// <summary>Returns the curated es-CR HTML hint for <paramref name="key"/>, or null when none.</summary>
    public static string? Get(string? key)
        => key is not null && Entries.TryGetValue(key, out var html) ? html : null;
}
