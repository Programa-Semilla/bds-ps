namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 038 — single source of truth for which enumerated regulatory status
/// counts as "favorable" (the old boolean <c>IsCompliant*</c> notion). Used by
/// the (deferred-redesign) supplier scoring and by entity→DTO mapping so the
/// reviewer surfaces keep a green/neutral compliance signal until slice B
/// reworks scoring against the full enum. An unreviewed (<c>null</c>) status is
/// never favorable.
///
/// <para><b>Lossy by design:</b> only the single favorable value counts; every
/// other reviewed status scores identically to unreviewed. For SICOP this means
/// "sin suscripción" is treated the same as "con sanciones"/"inhabilitación".
/// This is an interim stopgap until slice B redesigns scoring against the full
/// enum — call sites should not infer fine-grained compliance from this flag.</para>
/// </summary>
public static class RegulatoryStatusFavorability
{
    public static bool IsFavorable(this HaciendaStatus? status) => status == HaciendaStatus.AlDia;

    public static bool IsFavorable(this CcssStatus? status) => status == CcssStatus.AlDia;

    public static bool IsFavorable(this SicopStatus? status) => status == SicopStatus.SinSanciones;
}
