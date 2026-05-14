// Spec 021 / FR-020 / T158 — see specs/021-feedback-session-may13/data-model.md and tasks.md.
// Hint scaffolding: the attribute carries a resource-key pointer that the
// Web layer resolves into a Tabler `<span class="form-hint">` tooltip via the
// `_HintTooltip.cshtml` partial. Copy strings are deferred per OQ-8 — this
// PR ships the wire only; populating hint copy and decorating individual
// properties (Item.ProductName, Item.Categoria, …) lands in a follow-up PR.

using System;

namespace FundingPlatform.Domain.Attributes;

/// <summary>
/// Marks a viewmodel/entity property with a resource key pointing to the
/// hint copy that should render next to its input on the form. The key
/// uses dotted notation (e.g. <c>"Item.ProductName.Hint"</c>) and is
/// resolved through the standard resx pipeline at render time.
/// </summary>
/// <remarks>
/// Lives in <c>FundingPlatform.Domain</c> on purpose: viewmodels in
/// <c>FundingPlatform.Web</c> already reference Domain types, and hint
/// metadata is a form-shape concern rather than a Web-layer concern. No
/// Web reference is introduced.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class HintAttribute : Attribute
{
    /// <summary>
    /// Resource key resolved by the Web layer's `IStringLocalizer` against
    /// the spec-021 resx catalog (e.g. <c>"Item.ProductName.Hint"</c>).
    /// </summary>
    public string ResourceKey { get; }

    public HintAttribute(string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            throw new ArgumentException(
                "Hint resource key must not be null or whitespace.",
                nameof(resourceKey));
        }

        ResourceKey = resourceKey;
    }
}
