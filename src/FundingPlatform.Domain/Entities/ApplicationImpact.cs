using DomainImpact = FundingPlatform.Domain.ValueObjects.Impact;

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 035 (evolved 2026-06-16, data-model.md D13) — one impact the application
/// declares: a chosen <see cref="ImpactTemplate"/> plus its entered parameter
/// values. An application has one or more. Aggregate-child of
/// <see cref="Application"/>; mutated only through the root
/// (<c>Application.AddImpact</c> / <c>Application.RemoveImpact</c>).
/// </summary>
public class ApplicationImpact
{
    private readonly List<ImpactParameterValue> _parameterValues = [];

    public int Id { get; private set; }
    public int ApplicationId { get; private set; }
    public int ImpactTemplateId { get; private set; }

    public ImpactTemplate ImpactTemplate { get; private set; } = null!;

    public IReadOnlyList<ImpactParameterValue> ParameterValues => _parameterValues.AsReadOnly();

    /// <summary>
    /// Typed projection used by read surfaces. Requires the <see cref="ImpactTemplate"/>
    /// nav to be loaded by the caller (EF Include).
    /// </summary>
    public DomainImpact? Impact =>
        ImpactTemplate is null ? null : new DomainImpact(ImpactTemplate, ParameterValues);

    private ApplicationImpact() { }

    public ApplicationImpact(int impactTemplateId)
    {
        ImpactTemplateId = impactTemplateId;
    }

    /// <summary>Replace-all of this declared impact's parameter values.</summary>
    public void SetValues(IEnumerable<ImpactParameterValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _parameterValues.Clear();
        _parameterValues.AddRange(values);
    }
}
