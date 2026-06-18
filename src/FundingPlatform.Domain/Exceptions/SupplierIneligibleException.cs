namespace FundingPlatform.Domain.Exceptions;

/// <summary>
/// Spec 039 / FR-019 — thrown by <see cref="Entities.Item.Approve"/> when the
/// reviewer tries to approve an item whose selected provider has CCSS status
/// <c>sin inscripción</c> (a hard block). The invariant lives in the domain so it
/// cannot be bypassed (Constitution II); the Application layer catches this and
/// translates it to the es-CR reviewer message naming the provider.
/// </summary>
public sealed class SupplierIneligibleException : Exception
{
    public string SupplierName { get; }

    public SupplierIneligibleException(string supplierName)
        : base($"Supplier '{supplierName}' is not registered with the CCSS (sin inscripción) and cannot be approved.")
    {
        SupplierName = supplierName;
    }
}
