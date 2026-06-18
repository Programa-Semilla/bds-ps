namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 039 — why a provider was excluded from the recommendation candidate set.
/// Only CCSS <c>sin inscripción</c> blocks (FR-016); every other status affects
/// scoring only (FR-018). <see cref="None"/> means the provider is eligible.
/// </summary>
public enum SupplierBlockReason : byte
{
    None = 0,

    /// <summary>CCSS status is <c>sin inscripción</c> — a hard block (FR-016 / FR-019).</summary>
    CcssSinInscripcion = 1,
}
