using FundingPlatform.Domain.Enums;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 047 — the admin required-document rule matrix list.</summary>
public sealed class DocumentRuleAdminViewModel
{
    public required IReadOnlyList<DocumentRuleListItemViewModel> Rows { get; init; }
}

public sealed class DocumentRuleListItemViewModel
{
    public required int? CategoryId { get; init; }
    public required string CategoryName { get; init; }
    public required IReadOnlyList<EvidenceType> RequiredTypes { get; init; }
    public bool IsGlobalDefault => CategoryId is null;
}

/// <summary>Spec 047 — one evidence-type row in the create/edit matrix.</summary>
public sealed class DocumentRuleItemViewModel
{
    public EvidenceType Type { get; set; }
    public bool IsRequired { get; set; }
}

/// <summary>Spec 047 — create a required-document rule for a category (or the global default).</summary>
public sealed class CreateDocumentRuleViewModel
{
    /// <summary>Null = the global default.</summary>
    public int? CategoryId { get; set; }
    public List<DocumentRuleItemViewModel> Items { get; set; } = [];
    public IReadOnlyList<(int Id, string Name)> CategoryOptions { get; set; } = [];
}

/// <summary>Spec 047 — edit an existing rule (or the global default).</summary>
public sealed class EditDocumentRuleViewModel
{
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public List<DocumentRuleItemViewModel> Items { get; set; } = [];
}
