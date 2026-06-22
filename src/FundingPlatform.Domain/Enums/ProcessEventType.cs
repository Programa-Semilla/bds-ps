// Spec 044 — see specs/044-process-reception-windows/data-model.md.

namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Spec 044 / D6 — the kind of a <see cref="FundingPlatform.Domain.Entities.ProcessEvent"/>.
/// Only <see cref="ReceptionWindow"/> carries behavior this slice (it gates
/// submission availability); the other values are reserved (US5, schema-only)
/// so future calendar items need no table reshape. Stored as <c>TINYINT</c> —
/// the EF mapping MUST use <c>HasConversion&lt;byte&gt;()</c>.
/// </summary>
public enum ProcessEventType : byte
{
    ReceptionWindow = 0,
    Informational = 1,
    Deadline = 2,
    Milestone = 3,
}
