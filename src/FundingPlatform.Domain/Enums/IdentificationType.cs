using System.ComponentModel.DataAnnotations;

namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 026 — kind of legal identification a person or organisation presents.
/// Backed by TINYINT in SQL (mirrors <see cref="SupplierVerificationStatus"/>).
/// The type↔shape invariant (canonical form + per-type validation) lives in the
/// <c>Identification</c> value object, not here.
/// </summary>
public enum IdentificationType : byte
{
    /// <summary>Cédula de identidad física — natural person, canonical <c>1-2345-6789</c> (9 digits, 1-4-4).</summary>
    [Display(Name = "Cédula física")]
    CedulaFisica = 1,

    /// <summary>Cédula jurídica — organisation, canonical <c>3-101-123456</c> (10 digits, 1-3-6).</summary>
    [Display(Name = "Cédula jurídica")]
    CedulaJuridica = 2,

    /// <summary>DIMEX — foreign resident, plain 11–12 digits (no standard CR hyphenation).</summary>
    [Display(Name = "DIMEX")]
    Dimex = 3,

    /// <summary>NITE — Número de Identificación Tributaria Especial, canonical <c>3-101-123456</c> (10 digits, 1-3-6).</summary>
    [Display(Name = "NITE")]
    Nite = 4,

    /// <summary>Pasaporte — uppercased alphanumeric, up to 20 characters.</summary>
    [Display(Name = "Pasaporte")]
    Pasaporte = 5,
}
