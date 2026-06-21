using FundingPlatform.Application.Regulatory;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 043 — EF implementation of the regulatory-freshness query. Scopes to the
/// distinct suppliers chosen by the application's approved items
/// (<c>Item.SelectedSupplierId</c>, research D2) and flattens each supplier's
/// stale required fields to findings using the configured freshness window.
/// </summary>
public sealed class RegulatoryFreshnessService : IRegulatoryFreshnessService
{
    private readonly AppDbContext _db;
    private readonly IOptions<RegulatoryFreshnessOptions> _options;

    public RegulatoryFreshnessService(AppDbContext db, IOptions<RegulatoryFreshnessOptions> options)
    {
        _db = db;
        _options = options;
    }

    public async Task<IReadOnlyList<StaleRegulatoryFinding>> GetStaleFindingsForApplicationAsync(
        int applicationId, CancellationToken ct)
    {
        // The suppliers the agreement will actually contract with: the distinct
        // SelectedSupplierId set across the application's items (research D2).
        var supplierIds = await _db.Applications
            .Where(a => a.Id == applicationId)
            .SelectMany(a => a.Items)
            .Where(i => i.SelectedSupplierId != null)
            .Select(i => i.SelectedSupplierId!.Value)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (supplierIds.Count == 0)
        {
            return Array.Empty<StaleRegulatoryFinding>();
        }

        var suppliers = await _db.Suppliers
            .Where(s => supplierIds.Contains(s.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var window = _options.Value.FreshnessWindowDays;
        var now = DateTime.UtcNow;

        var findings = new List<StaleRegulatoryFinding>();
        foreach (var supplier in suppliers)
        {
            foreach (var field in supplier.StaleRequiredFields(window, now))
            {
                findings.Add(new StaleRegulatoryFinding(
                    supplier.Id, supplier.Name, field, LastReviewedAt(supplier, field)));
            }
        }

        return findings;
    }

    private static DateTime? LastReviewedAt(Supplier supplier, RegulatoryField field) => field switch
    {
        RegulatoryField.Hacienda => supplier.HaciendaLastReviewedAt,
        RegulatoryField.Ccss => supplier.CcssLastReviewedAt,
        RegulatoryField.Sicop => supplier.SicopLastReviewedAt,
        _ => null,
    };
}
