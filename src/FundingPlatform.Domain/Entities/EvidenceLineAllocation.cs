namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 047 / FR-003 (research D2) — the allocation of a portion of an <see cref="Evidence"/>
/// document to one budget-line (<see cref="Item"/>): the M:N join realizing per-line evidence
/// attribution. Mirrors <see cref="DisbursementLineAllocation"/> exactly. Owned by the
/// <see cref="Evidence"/>; an allocation change replaces the row set (no mutators). The
/// allocation-integrity check (Σ ≤ evidence amount) lives in the service; a single row's amount
/// must be positive.
/// </summary>
public sealed class EvidenceLineAllocation
{
    public int Id { get; private set; }
    public int EvidenceId { get; private set; }
    public int ItemId { get; private set; }
    public decimal Amount { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private EvidenceLineAllocation() { }

    /// <summary>Creates an allocation row. <paramref name="amount"/> must be &gt; 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amount"/> ≤ 0.</exception>
    public static EvidenceLineAllocation For(int evidenceId, int itemId, decimal amount)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "Evidence line allocation amount must be greater than zero.");
        }
        return new EvidenceLineAllocation
        {
            EvidenceId = evidenceId,
            ItemId = itemId,
            Amount = amount,
        };
    }
}
