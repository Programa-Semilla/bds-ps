namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 035 / data-model.md (D2) — re-keyed from <c>ApplicationId</c> to
/// <c>ItemId</c>: impact relocated from Application down to the line item. The
/// stale <c>Impact</c> entity + <c>ImpactId</c> back-reference are gone (dead
/// code, SC-003). Held by <see cref="Item"/> via its
/// <c>ImpactParameterValues</c> collection.
/// </summary>
public class ImpactParameterValue
{
    public int Id { get; private set; }
    public int ItemId { get; private set; }
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
