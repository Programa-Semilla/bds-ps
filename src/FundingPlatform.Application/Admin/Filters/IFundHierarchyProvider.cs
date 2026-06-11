namespace FundingPlatform.Application.Admin.Filters;

/// <summary>
/// Builds the Fondo → Proceso → Grupo (Fund → Process → Group) tree that powers
/// the cascading drill-down filter shared across the admin filter surfaces
/// (Users, Suppliers, Processes, Reports). One small assembly so a Fund/Process
/// with zero children still appears in the tree.
/// </summary>
public interface IFundHierarchyProvider
{
    /// <param name="includeArchived">
    /// When <c>false</c>, archived Funds are omitted (assignment/new-filter
    /// surfaces). When <c>true</c>, every Fund is included so admins can still
    /// filter/report on archived-Fund history (spec 029 / FR-011).
    /// </param>
    Task<IReadOnlyList<FundHierarchyNode>> GetAsync(bool includeArchived, CancellationToken ct);
}

/// <summary>A Fund (Fondo) node carrying its Processes.</summary>
public sealed record FundHierarchyNode(
    int Id,
    string Name,
    IReadOnlyList<ProcessHierarchyNode> Processes);

/// <summary>A Process (Proceso) node carrying its Groups.</summary>
public sealed record ProcessHierarchyNode(
    int Id,
    string Name,
    IReadOnlyList<GroupHierarchyNode> Groups);

/// <summary>A Group (Grupo) leaf node.</summary>
public sealed record GroupHierarchyNode(int Id, string Name);
