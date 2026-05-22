// Spec 021 — see specs/021-feedback-session-may13/tasks.md T093.

using FundingPlatform.Application.Suppliers;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 021 / T093 / FR-009 / FR-012 / FR-014 — EF-backed implementation of
/// <see cref="ICreateSupplierBranchHandler"/>. Validates Province/Cantón
/// consistency (via the domain guard on <see cref="SupplierBranch.SetLocation"/>)
/// and handles both the "add branch to existing supplier" and "create Draft
/// supplier with default branch" flows.
/// </summary>
public sealed class CreateSupplierBranchHandler : ICreateSupplierBranchHandler
{
    private readonly AppDbContext _db;

    public CreateSupplierBranchHandler(AppDbContext db)
    {
        _db = db;
    }

    public async Task<CreateSupplierBranchResult> HandleAsync(
        CreateSupplierBranchCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        var canton = await _db.Cantons
            .FirstOrDefaultAsync(c => c.Id == cmd.CantonId, ct)
            ?? throw new InvalidOperationException(
                $"Canton {cmd.CantonId} not found in catalog.");

        if (canton.ProvinceId != cmd.ProvinceId)
        {
            throw new ArgumentException(
                "Canton.ProvinceId must equal the supplied ProvinceId (FR-014).",
                nameof(cmd));
        }

        Supplier supplier;
        SupplierBranch branch;

        if (cmd.SupplierId is { } sid)
        {
            supplier = await _db.Suppliers
                .Include(s => s.Branches)
                .FirstOrDefaultAsync(s => s.Id == sid, ct)
                ?? throw new InvalidOperationException($"Supplier {sid} not found.");

            branch = supplier.AddBranch(
                branchName: cmd.BranchName,
                contactName: cmd.ContactPersonName,
                email: cmd.Email,
                phone: cmd.Phone,
                addressLine: cmd.AddressLine,
                province: null,
                shippingDetails: null,
                warrantyInfo: null,
                createdByApplicantId: cmd.CurrentApplicantId,
                isDefault: supplier.Branches.Count == 0);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(cmd.LegalId) || string.IsNullOrWhiteSpace(cmd.SupplierName))
            {
                throw new ArgumentException(
                    "LegalId + SupplierName are required when registering a new supplier.",
                    nameof(cmd));
            }

            supplier = Supplier.CreateDraft(
                legalId: cmd.LegalId,
                name: cmd.SupplierName,
                createdByApplicantId: cmd.CurrentApplicantId,
                firstBranchName: cmd.BranchName,
                firstBranchContactName: cmd.ContactPersonName,
                firstBranchEmail: cmd.Email,
                firstBranchPhone: cmd.Phone,
                firstBranchAddressLine: cmd.AddressLine,
                firstBranchProvince: null,
                firstBranchShippingDetails: null,
                firstBranchWarrantyInfo: null);
            _db.Suppliers.Add(supplier);
            branch = supplier.Branches.First();
        }

        branch.SetContactPersonName(cmd.ContactPersonName);
        branch.SetLocation(cmd.ProvinceId, cmd.CantonId, canton);

        await _db.SaveChangesAsync(ct);
        return new CreateSupplierBranchResult(supplier.Id, branch.Id);
    }
}
