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
/// <c>ImpactTemplateIdsCsv</c> stores the snapshot list as a CSV (rather than a
/// FK collection) so deleting a base ImpactTemplate row does not corrupt the
/// historical snapshot. The <c>Application.SetImpact</c> guard parses the CSV
/// to validate the applicant's pick lives in the snapshot.
/// </summary>
public class ProcessPlantilla
{
    public int Id { get; private set; }
    public int ProcessId { get; private set; }
    public int SourcePlantillaId { get; private set; }

    public int MinimumQuotationsPerItem { get; private set; }
    public long RequiredFieldFlags { get; private set; }
    public string ImpactTemplateIdsCsv { get; private set; } = string.Empty;

    public DateTimeOffset AssignedAt { get; private set; }

    public Process? Process { get; private set; }
    public Plantilla? SourcePlantilla { get; private set; }

    private ProcessPlantilla() { }

    /// <summary>
    /// Snapshot constructor — invoked exclusively from <see cref="Plantilla.AssignTo"/>.
    /// Caller is responsible for asserting the source Plantilla has ≥ 1 attached
    /// ImpactTemplate before building the CSV.
    /// </summary>
    internal ProcessPlantilla(
        int processId,
        int sourcePlantillaId,
        int minimumQuotationsPerItem,
        long requiredFieldFlags,
        IEnumerable<int> impactTemplateIds,
        DateTimeOffset assignedAt)
    {
        ArgumentNullException.ThrowIfNull(impactTemplateIds);
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

        var ids = impactTemplateIds.Distinct().OrderBy(i => i).ToArray();
        if (ids.Length == 0)
        {
            throw new InvalidOperationException(
                "A Plantilla must have ≥ 1 ImpactTemplate attached before it can be assigned to a Process.");
        }

        ProcessId = processId;
        SourcePlantillaId = sourcePlantillaId;
        MinimumQuotationsPerItem = minimumQuotationsPerItem;
        RequiredFieldFlags = requiredFieldFlags;
        ImpactTemplateIdsCsv = string.Join(",", ids);
        AssignedAt = assignedAt;
    }

    /// <summary>
    /// Parses <see cref="ImpactTemplateIdsCsv"/> into the snapshot's
    /// <c>ImpactTemplate.Id</c> set. Used by <c>Application.SetImpact</c> to
    /// validate the applicant's pick.
    /// </summary>
    public IReadOnlyList<int> ImpactTemplateIds()
    {
        if (string.IsNullOrEmpty(ImpactTemplateIdsCsv))
        {
            return Array.Empty<int>();
        }
        return ImpactTemplateIdsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture))
            .ToArray();
    }
}
