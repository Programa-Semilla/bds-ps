// Spec 021 — see specs/021-feedback-session-may13/data-model.md (Province catalog).

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 021 / FR-014 — one of Costa Rica's seven provinces. Static catalog
/// seeded from the canonical TSE/MOPT 2020 list via PostDeployment script;
/// not user-mutable in scope 021. Foreign provinces are deliberately not
/// catalogued (OQ-10 — UI blocks non-CR addresses).
/// </summary>
public class Province
{
    private readonly List<Canton> _cantons = [];

    public int Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;

    public IReadOnlyCollection<Canton> Cantons => _cantons.AsReadOnly();

    private Province() { }

    public Province(string code, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Code = code.Trim();
        Name = name.Trim();
    }
}
