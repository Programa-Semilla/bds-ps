// Spec 026 — render model for the shared _LegalIdField partial (type selector +
// masked identification input). Reusable across Register / admin user create+edit
// / supplier add by passing the parent model's field names so the posted values
// bind to the right properties.

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels;

public sealed class LegalIdFieldModel
{
    /// <summary>Name of the parent model's <see cref="IdentificationType"/> property (binds the selector).</summary>
    public string TypeFieldName { get; init; } = "IdentificationType";

    /// <summary>Name of the parent model's legal-ID string property (binds the masked input).</summary>
    public string ValueFieldName { get; init; } = "LegalId";

    public IdentificationType? SelectedType { get; init; }
    public string? Value { get; init; }
    public IReadOnlyList<IdentificationType> AllowedTypes { get; init; } = Array.Empty<IdentificationType>();

    public string TypeLabel { get; init; } = "Tipo de identificación";
    public string ValueLabel { get; init; } = "Identificación";

    /// <summary>Unique id linking the controller selector to its grouped input on the page.</summary>
    public string GroupId { get; init; } = "legal-id";

    /// <summary>Optional explicit input id (e.g. supplier lookup expects <c>supplier-legal-id-input</c>).</summary>
    public string? InputId { get; init; }

    public string InputCssClass { get; init; } = "form-control";
    public string SelectCssClass { get; init; } = "form-select";
    public string ValidationCssClass { get; init; } = "text-danger";
    public string? ValueAutocomplete { get; init; }

    /// <summary>Client mask name for a type (matches the registry in input-masks.js).</summary>
    public static string MaskName(IdentificationType type) => type switch
    {
        IdentificationType.CedulaFisica => "cedula",
        IdentificationType.CedulaJuridica => "cedula-jur",
        IdentificationType.Dimex => "dimex",
        IdentificationType.Nite => "nite",
        IdentificationType.Pasaporte => "pasaporte",
        _ => "cedula",
    };

    /// <summary>es-CR display label from the enum's <see cref="DisplayAttribute"/>.</summary>
    public static string Label(IdentificationType type)
    {
        var field = typeof(IdentificationType).GetField(type.ToString());
        return field?.GetCustomAttribute<DisplayAttribute>()?.Name ?? type.ToString();
    }
}
