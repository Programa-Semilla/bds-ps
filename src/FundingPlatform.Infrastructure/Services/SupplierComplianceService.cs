// Spec 038 — see specs/038-auditor-provider-compliance/contracts/interfaces.md
// (ISupplierComplianceService) and research.md D7/D8/D15.

using System.Text.Json;
using FundingPlatform.Application.Abstractions;
using FundingPlatform.Application.Suppliers.Compliance;
using FundingPlatform.Domain.Entities;
using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;
using FundingPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FundingPlatform.Infrastructure.Services;

/// <summary>
/// Spec 038 — implements <see cref="ISupplierComplianceService"/>. Loads the
/// supplier, applies the rich-domain edit, stages one <c>supplier.*</c>
/// <see cref="AdminAuditEvent"/> per returned <see cref="RegulatoryChange"/>, and
/// commits atomically in a single <c>SaveChangesAsync</c>. Optimistic concurrency
/// is enforced by setting the posted <c>RowVersion</c> as the tracked entity's
/// original value; a <see cref="DbUpdateConcurrencyException"/> surfaces an es-CR
/// "recargue" message.
/// </summary>
public sealed class SupplierComplianceService : ISupplierComplianceService
{
    private const string ConcurrencyMessage = "Los datos cambiaron; recargue la página.";
    private const string NotFoundMessage = "Proveedor no encontrado.";
    private const string WarningTooLongMessage = "La nota de advertencia no puede superar los 1000 caracteres.";
    private const string ReviewUnsetMessage = "Defina un estado antes de confirmar la revisión.";

    private readonly AppDbContext _db;
    private readonly IAdminAuditEventWriter _audit;

    public SupplierComplianceService(AppDbContext db, IAdminAuditEventWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<SupplierComplianceResult> EditComplianceAsync(EditSupplierComplianceCommand cmd, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == cmd.SupplierId, ct);
        if (supplier is null)
            return SupplierComplianceResult.Fail(NotFoundMessage);

        // FR-011 — reject arbitrary/out-of-range status codes (default MVC enum
        // binding admits any in-range byte; the UI select only offers valid values).
        if ((cmd.Hacienda is { } h && !Enum.IsDefined(h))
            || (cmd.Ccss is { } c && !Enum.IsDefined(c))
            || (cmd.Sicop is { } sc && !Enum.IsDefined(sc)))
            return SupplierComplianceResult.Fail("Estado regulatorio inválido.");

        // Validate the warning note up front for a clean es-CR message (the domain
        // also guards as a backstop).
        if (cmd.HasWarning && cmd.WarningNote is { } note && note.Trim().Length > Supplier.WarningNoteMaxLength)
            return SupplierComplianceResult.Fail(WarningTooLongMessage);

        // The provider name rides on the same Detail form but is intentionally
        // out of the regulatory audit scope (spec 038 audits regulatory/PME/warning
        // changes only); the name edit persists without an AdminAuditEvent.
        var nameChanged = !string.IsNullOrWhiteSpace(cmd.Name)
            && !string.Equals(cmd.Name.Trim(), supplier.Name, StringComparison.Ordinal);
        if (nameChanged)
            supplier.EditByAdmin(cmd.Name);

        IReadOnlyList<RegulatoryChange> changes;
        try
        {
            changes = supplier.ApplyRegulatoryEdit(
                cmd.Hacienda, cmd.Ccss, cmd.Sicop, cmd.IsPmeOrPyme,
                cmd.HasWarning, cmd.WarningNote, cmd.ActorUserId, DateTime.UtcNow);
        }
        catch (ArgumentException ex) when (ex.ParamName == "warningNote")
        {
            // Domain backstop for the note-length guard only; any other
            // ArgumentException is a programmer error and must not be masked as
            // "warning too long" (the up-front check above already validates length).
            return SupplierComplianceResult.Fail(WarningTooLongMessage);
        }

        if (changes.Count == 0 && !nameChanged)
            return SupplierComplianceResult.Success();

        SetOriginalRowVersion(supplier, cmd.RowVersion);

        foreach (var change in changes)
            await _audit.WriteAsync(ActionFor(change), cmd.ActorUserId, PayloadFor(cmd.SupplierId, change), ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SupplierComplianceResult.Fail(ConcurrencyMessage);
        }

        return SupplierComplianceResult.Success();
    }

    public async Task<SupplierComplianceResult> ConfirmReviewedAsync(
        int supplierId, RegulatoryField field, string actorUserId, byte[] rowVersion, CancellationToken ct)
    {
        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == supplierId, ct);
        if (supplier is null)
            return SupplierComplianceResult.Fail(NotFoundMessage);

        RegulatoryChange change;
        try
        {
            change = supplier.ConfirmRegulatoryReviewed(field, actorUserId, DateTime.UtcNow);
        }
        catch (InvalidOperationException)
        {
            return SupplierComplianceResult.Fail(ReviewUnsetMessage);
        }

        SetOriginalRowVersion(supplier, rowVersion);

        await _audit.WriteAsync(
            AdminAuditEvent.SupplierRegulatoryReviewed, actorUserId, PayloadFor(supplierId, change), ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SupplierComplianceResult.Fail(ConcurrencyMessage);
        }

        return SupplierComplianceResult.Success();
    }

    // ---------------------------------------------------------------------

    private void SetOriginalRowVersion(Supplier supplier, byte[]? rowVersion)
    {
        // Only enforce OC when the caller supplied the token (the Detail form posts
        // it as a hidden field); empty token falls back to last-write-wins.
        if (rowVersion is { Length: > 0 })
            _db.Entry(supplier).Property(s => s.RowVersion).OriginalValue = rowVersion;
    }

    private static string ActionFor(RegulatoryChange change) => change.Field switch
    {
        RegulatoryChangeField.Pme => AdminAuditEvent.SupplierPmeChanged,
        RegulatoryChangeField.Warning => AdminAuditEvent.SupplierWarningChanged,
        // Hacienda/Ccss/Sicop changes from ApplyRegulatoryEdit are always Changed;
        // the regulatory_reviewed (no-change) action is written directly by
        // ConfirmReviewedAsync, never through this mapper.
        _ => AdminAuditEvent.SupplierRegulatoryChanged,
    };

    private static string PayloadFor(int supplierId, RegulatoryChange change) =>
        JsonSerializer.Serialize(new
        {
            supplierId,
            field = change.Field.ToString(),
            oldValue = change.OldValue,
            newValue = change.NewValue,
            source = change.Source.ToString(),
            kind = change.Kind.ToString(),
        });
}
