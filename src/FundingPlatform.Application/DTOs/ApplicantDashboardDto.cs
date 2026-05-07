namespace FundingPlatform.Application.DTOs;

/// <summary>Spec 011 US1 — applicant home dashboard view model (data-model.md §2).</summary>
public sealed record ApplicantDashboardDto(
    string FirstName,
    KpiSnapshot Kpis,
    AwaitingAction? AwaitingAction,
    IReadOnlyList<ApplicationCardDto> ActiveApplications,
    IReadOnlyList<ActivityEvent> RecentActivity,
    bool HasMoreActivity,
    bool HasMoreApplications);

public sealed record KpiSnapshot(int Active, int InReview, int AwaitingApplicantAction, int Funded);

public sealed record AwaitingAction(
    Guid ApplicationId,
    string ApplicationNumber,
    string ProjectName,
    string SingleSentenceMessage,
    string PrimaryCtaLabel,
    string PrimaryCtaHref);

public sealed record ApplicationCardDto(
    Guid ApplicationId,
    string ApplicationNumber,
    string ProjectName,
    JourneyViewModel JourneyMini,
    string CurrentStageLabel,
    int DaysInCurrentState,
    DateTimeOffset LastActivity,
    ContextualAction PrimaryAction,
    // Spec 015 / T414 — multi-currency surface on the dashboard card.
    // <c>TotalConvertedCrc</c> sums each Item's selected-supplier
    // <c>Quotation.ConvertedCrcAmount</c> in CRC (legacy rows excluded).
    // <c>HasNonCrcQuotation</c> is true when any quotation on the application
    // is non-CRC, so the view can hint at the conversion without per-card
    // re-rendering all currencies.
    decimal? TotalConvertedCrc = null,
    bool HasNonCrcQuotation = false);

public enum ContextualActionStyle { Primary, Secondary }

public sealed record ContextualAction(string Label, string Href, ContextualActionStyle Style);

public sealed record ActivityEvent(
    DateTimeOffset Occurred,
    string Title,
    string? ActorName,
    string DeepLinkHref,
    string IconKey);
