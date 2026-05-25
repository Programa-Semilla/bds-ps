// Spec 026 — see specs/026-input-masks/contracts/identification-validation.md.
// Server-side echo of the client mask: delegates format authority to the domain
// Identification value object. The client mask is never trusted (FR-014).

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Web.Validation;

/// <summary>
/// Validates that a legal-ID string matches the canonical shape of its sibling
/// <see cref="IdentificationType"/> property. Format-only — presence ("type
/// required when value required") is enforced at the controller, mirroring the
/// existing "Cédula obligatoria para Solicitante" check, so all errors surface
/// together (Quality Gate).
///
/// Empty value → valid here (a <c>[Required]</c> attribute owns presence).
/// Missing type → valid here (the controller presence check owns the
/// "Seleccione el tipo de identificación." message).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class IdentificationFormatAttribute : ValidationAttribute
{
    private readonly string _typePropertyName;

    public IdentificationFormatAttribute(string typePropertyName = "IdentificationType")
    {
        _typePropertyName = typePropertyName;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var raw = value as string;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ValidationResult.Success;
        }

        var type = ResolveType(validationContext);
        if (type is null)
        {
            // Presence of the type is the controller's concern; format passes here.
            return ValidationResult.Success;
        }

        if (Identification.IsValid(type.Value, raw))
        {
            return ValidationResult.Success;
        }

        var label = DisplayLabel(type.Value);
        var member = validationContext.MemberName;
        return new ValidationResult(
            $"La identificación no tiene el formato de {label}.",
            member is null ? null : [member]);
    }

    private IdentificationType? ResolveType(ValidationContext context)
    {
        var prop = context.ObjectType.GetProperty(_typePropertyName);
        var resolved = prop?.GetValue(context.ObjectInstance);
        return resolved switch
        {
            IdentificationType t => t,
            _ => null,
        };
    }

    private static string DisplayLabel(IdentificationType type)
    {
        var field = type.GetType().GetField(type.ToString());
        var display = field?.GetCustomAttribute<DisplayAttribute>();
        return display?.Name ?? type.ToString();
    }
}
