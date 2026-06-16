namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 035 (evolved 2026-06-16, data-model.md D13) — re-keyed to
/// <c>ApplicationImpactId</c>: impact data collection lives at the application
/// level (one-or-more declared impacts). Held by <see cref="ApplicationImpact"/>
/// via its parameter-values collection. (Pre-035 keyed by <c>ApplicationId</c>;
/// the superseded per-item 035 design keyed by <c>ItemId</c>.)
/// </summary>
public class ImpactParameterValue
{
    public int Id { get; private set; }
    public int ApplicationImpactId { get; private set; }
    public int ImpactTemplateParameterId { get; private set; }
    public string? Value { get; private set; }

    public ImpactTemplateParameter ImpactTemplateParameter { get; private set; } = null!;

    private ImpactParameterValue() { }

    public ImpactParameterValue(int impactTemplateParameterId, string? value)
    {
        ImpactTemplateParameterId = impactTemplateParameterId;
        Value = value;
    }
}
