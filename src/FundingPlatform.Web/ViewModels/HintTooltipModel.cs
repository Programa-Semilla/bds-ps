using System;

namespace FundingPlatform.Web.ViewModels;

/// <summary>
/// Spec 021 / FR-020 / T158 — payload for `_HintTooltip.cshtml`.
/// </summary>
/// <param name="ContainerType">Optional — declaring type of the property to
/// reflect. Either this + <paramref name="PropertyName"/>, or
/// <paramref name="ResourceKeyOverride"/>, must be provided.</param>
/// <param name="PropertyName">Property name on <paramref name="ContainerType"/>
/// decorated with <c>[Hint("...")]</c>.</param>
/// <param name="ResourceKeyOverride">Optional explicit resource key, used
/// when the consumer wants to bypass reflection (e.g. a non-decorated input
/// or a one-off hint).</param>
public sealed record HintTooltipModel(
    Type? ContainerType = null,
    string? PropertyName = null,
    string? ResourceKeyOverride = null);
