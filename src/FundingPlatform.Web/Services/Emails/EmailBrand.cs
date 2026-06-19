namespace FundingPlatform.Web.Services.Emails;

/// <summary>
/// Spec 041 / FR-006 / FR-007 — es-CR source of truth for the brand <b>copy</b>
/// (platform name, sign-off, support email/phone, automatic-message note) consumed
/// by the email layout + partials.
///
/// <para>The platform is referred to as <b>ALIA</b> in body copy, but the brand,
/// logo, and sign-off stay "Programa Semilla" / "Equipo Programa Semilla"
/// (spec 041 Overview + reference copy <c>seeds/emails/Respuestas correo ALIA.txt</c>).</para>
///
/// <para><b>Palette note:</b> the colour constants below document the brand palette
/// and are asserted by the design-system tests, but for email-client reliability the
/// hex values are inlined directly in the Razor chrome (Razor `@`-interpolation inside
/// dense inline-CSS attributes is error-prone). They are NOT a single-edit knob —
/// changing a brand colour means updating the inlined hex in the layout + partials
/// (guarded by the no-`#1d1d1f` test).</para>
/// </summary>
public static class EmailBrand
{
    // --- Naming / copy -----------------------------------------------------
    /// <summary>Platform name used in body copy (FR-007).</summary>
    public const string PlatformName = "ALIA";
    /// <summary>Brand/organization shown in the logo + sign-off block.</summary>
    public const string Organization = "Programa Semilla";
    /// <summary>Sign-off line that replaces the old "Sistema de Banca para el Desarrollo" signature.</summary>
    public const string SignOff = "Equipo Programa Semilla";
    /// <summary>Sign-off closing that precedes <see cref="SignOff"/> (reference copy uses "Atentamente,").</summary>
    public const string SignOffClosing = "Atentamente,";

    // --- Support / footer (FR-006) ----------------------------------------
    public const string SupportEmail = "soporte@programa-semilla.cr";
    /// <summary>Support phone surfaced on every email footer (FR-006).</summary>
    public const string SupportPhone = "+506 4600-1234";
    /// <summary>Automatic-message legal note kept on every email (FR-006).</summary>
    public const string AutomaticMessageNote =
        "Este es un mensaje automático. Por favor, no respondás a este correo.";

    // --- Brand palette (inline-CSS tokens, FR-003) ------------------------
    /// <summary>Primary brand teal — CTA background + headings.</summary>
    public const string PrimaryTeal = "#008a9e";
    /// <summary>Secondary teal — accents / hero gradient.</summary>
    public const string SecondaryTeal = "#42afa8";
    /// <summary>Orange accent.</summary>
    public const string Orange = "#f9a61c";
    /// <summary>Yellow accent.</summary>
    public const string Yellow = "#ffc729";
    /// <summary>Page (outermost body) background — light neutral.</summary>
    public const string PageBackground = "#f4f6f7";
    /// <summary>Card / container background.</summary>
    public const string Surface = "#ffffff";
    /// <summary>Body text color (legible, WCAG AA on white; NOT the old near-black #1d1d1f).</summary>
    public const string Ink = "#243b40";
    /// <summary>Muted/secondary text color.</summary>
    public const string Muted = "#5b6b6e";
    /// <summary>Hairline / border color.</summary>
    public const string Border = "#dbe3e4";
    /// <summary>Detail-card tinted background.</summary>
    public const string CardTint = "#eef6f7";
}
