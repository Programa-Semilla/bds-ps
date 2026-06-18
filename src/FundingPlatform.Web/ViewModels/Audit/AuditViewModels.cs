using FundingPlatform.Application.Audit;
using FundingPlatform.Application.DTOs;

namespace FundingPlatform.Web.ViewModels.Audit;

/// <summary>Spec 040 / US1 — the auditor inbox of PendingAudit applications.</summary>
public sealed class AuditInboxViewModel
{
    public IReadOnlyList<AuditInboxRowDto> Rows { get; init; } = [];
    public string? SearchTerm { get; init; }
}

/// <summary>
/// Spec 040 / US1 — the auditor detail surface: a reviewer-equivalent read of the
/// application plus the audit checklist and the audit-stage action flags.
/// </summary>
public sealed class AuditDetailViewModel
{
    public ReviewApplicationDto Application { get; init; } = null!;
    public AuditChecklistView Checklist { get; init; } = null!;
    public bool IsAdmin { get; init; }
    /// <summary>True when generation is permitted now (checklist complete, no agreement yet).</summary>
    public bool CanGenerate => Checklist.AllRequiredCompliant && !Checklist.HasAnyNonCompliant && !Checklist.AgreementExists;
    public bool CanConfirm => Checklist.AgreementExists && !Checklist.AgreementConfirmed;
    public bool CanRelease => Checklist.AgreementExists && Checklist.AgreementConfirmed;
    public bool CanReturn => Checklist.HasAnyNonCompliant;
}

/// <summary>Spec 040 — one posted auditor mark (model-bound from the checklist form).</summary>
public sealed class AuditMarkInput
{
    public int TemplateItemId { get; set; }
    public bool Compliant { get; set; }
    public string? Reason { get; set; }
}
