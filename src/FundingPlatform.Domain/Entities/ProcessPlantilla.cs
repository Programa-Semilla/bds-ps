// Spec 021 — see specs/021-feedback-session-may13/data-model.md (ProcessPlantilla snapshot)
// and research.md OQ-1 (one-to-one cardinality with Process).

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 021 / FR-004 — copy-on-assign snapshot of a base <see cref="Plantilla"/>
/// captured at the moment it is assigned to a <see cref="Process"/>. The snapshot
/// payload (minimum quotations, required-field flags, attached ImpactTemplate ids)
/// is immutable after construction: edits to the base Plantilla do not propagate
/// here.
///
/// Per OQ-1 this is a one-to-one relationship (UNIQUE on <c>ProcessId</c> at the
/// DB level); a Process holds at most one ProcessPlantilla. The constructor is
/// <c>internal</c> so only <see cref="Plantilla.AssignTo"/> may build one.
///
/// Spec 035 / D4 — the <c>ImpactTemplateIdsCsv</c> snapshot was dropped: per-item
/// impact selection no longer consults the Plantilla.
/// </summary>
public class ProcessPlantilla
{
    public int Id { get; private set; }
    public int ProcessId { get; private set; }
    public int SourcePlantillaId { get; private set; }

    public int MinimumQuotationsPerItem { get; private set; }
    public long RequiredFieldFlags { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    public Process? Process { get; private set; }
    public Plantilla? SourcePlantilla { get; private set; }

    private ProcessPlantilla() { }

    /// <summary>
    /// Snapshot constructor — invoked exclusively from <see cref="Plantilla.AssignTo"/>.
    /// </summary>
    internal ProcessPlantilla(
        int processId,
        int sourcePlantillaId,
        int minimumQuotationsPerItem,
        long requiredFieldFlags,
        DateTimeOffset assignedAt)
    {
        if (processId <= 0)
        {
            throw new ArgumentException("ProcessId must be a positive integer.", nameof(processId));
        }
        if (sourcePlantillaId <= 0)
        {
            throw new ArgumentException("SourcePlantillaId must be a positive integer.", nameof(sourcePlantillaId));
        }
        if (minimumQuotationsPerItem <= 0)
        {
            throw new ArgumentException(
                "MinimumQuotationsPerItem must be positive.", nameof(minimumQuotationsPerItem));
        }

        ProcessId = processId;
        SourcePlantillaId = sourcePlantillaId;
        MinimumQuotationsPerItem = minimumQuotationsPerItem;
        RequiredFieldFlags = requiredFieldFlags;
        AssignedAt = assignedAt;
    }
}
