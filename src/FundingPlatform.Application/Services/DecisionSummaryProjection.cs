using System.Globalization;
using FundingPlatform.Application.DTOs;
using FundingPlatform.Domain.Enums;
using AppEntity = FundingPlatform.Domain.Entities.Application;
using QuotationEntity = FundingPlatform.Domain.Entities.Quotation;

namespace FundingPlatform.Application.Services;

/// <summary>
/// Spec 027 / US4 — pure mapping from the loaded Application aggregate to the
/// shared <see cref="DecisionSummaryLineDto"/> list. The CRC conversion note is
/// lifted verbatim from <c>FundingAgreementController.BuildConversionNote</c>
/// (spec 015 multi-currency display) so every surface formats it identically.
/// </summary>
public sealed class DecisionSummaryProjection : IDecisionSummaryProjection
{
    // Spec 027 / US4 — fixed es-CR formatting so the note reads identically on
    // every surface regardless of the request culture (the consistency this
    // story exists to guarantee). Default culture is es-CR (CLAUDE.md).
    private static readonly CultureInfo EsCr = CultureInfo.GetCultureInfo("es-CR");

    public IReadOnlyList<DecisionSummaryLineDto> Project(AppEntity application)
    {
        ArgumentNullException.ThrowIfNull(application);

        // Latest applicant response (highest cycle) carries the per-item decision.
        var latestResponse = application.ApplicantResponses
            .OrderByDescending(r => r.CycleNumber)
            .FirstOrDefault();

        // Order: LineCode (ordinal, nulls last) then Id.
        var orderedItems = application.Items
            .OrderBy(i => i.LineCode is null)
            .ThenBy(i => i.LineCode, StringComparer.Ordinal)
            .ThenBy(i => i.Id);

        var lines = new List<DecisionSummaryLineDto>();
        foreach (var item in orderedItems)
        {
            var quotations = item.Quotations.Select(MapQuotation).ToList();

            string? approvedSupplierName = null;
            DecisionSummaryQuotationView? approvedAmount = null;
            if (item.ReviewStatus == ItemReviewStatus.Approved && item.SelectedSupplierId is int supplierId)
            {
                var selected = item.Quotations.FirstOrDefault(q => q.SupplierId == supplierId);
                if (selected is not null)
                {
                    approvedSupplierName = selected.Supplier?.Name;
                    approvedAmount = MapQuotation(selected);
                }
            }

            string? applicantDecision = null;
            var itemResponse = latestResponse?.ItemResponses.FirstOrDefault(ir => ir.ItemId == item.Id);
            if (itemResponse is not null)
            {
                applicantDecision = itemResponse.Decision switch
                {
                    ItemResponseDecision.Accept => "Aceptado",
                    ItemResponseDecision.Reject => "Rechazado",
                    _ => null,
                };
            }

            lines.Add(new DecisionSummaryLineDto(
                LineCode: item.LineCode,
                ProductName: item.ProductName,
                CategoryName: item.Category?.Name ?? string.Empty,
                TechnicalSpecifications: item.TechnicalSpecifications,
                ReviewStatus: item.ReviewStatus,
                ReviewComment: item.ReviewComment,
                ApprovedSupplierName: approvedSupplierName,
                ApprovedAmount: approvedAmount,
                Quotations: quotations,
                ApplicantDecision: applicantDecision));
        }

        return lines;
    }

    private static DecisionSummaryQuotationView MapQuotation(QuotationEntity q) =>
        new(
            SupplierName: q.Supplier?.Name ?? string.Empty,
            Amount: q.Price,
            Currency: q.Currency,
            ConvertedCrcAmount: q.ConvertedCrcAmount,
            CurrencyConversionNote: BuildConversionNote(q));

    private static string? BuildConversionNote(QuotationEntity q)
    {
        if (q.Currency == "CRC" || q.Snapshot is null) return null;
        var rate = q.Snapshot.RateValue.ToString("N6", EsCr);
        var rateType = q.Snapshot.RateType switch
        {
            RateType.Buy => "Compra",
            RateType.Sell => "Venta",
            _ => q.Snapshot.RateType.ToString(),
        };
        var effective = q.Snapshot.EffectiveAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"Conversión: 1 {q.Currency} = ₡{rate} (Tipo {rateType}, vigente desde {effective})";
    }
}
