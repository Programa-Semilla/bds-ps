// Spec 037 — see specs/037-applicant-companies/data-model.md (Company aggregate)
// and research.md D1.

namespace FundingPlatform.Domain.Entities;

/// <summary>
/// Spec 037 / FR-001 — admin-managed company (Empresa) owned by exactly one
/// <see cref="Applicant"/> (one applicant → many companies). A single business
/// attribute (<see cref="Name"/>) plus a soft-archive lifecycle
/// (<see cref="ArchivedAt"/>) and audit timestamps + <see cref="RowVersion"/>.
///
/// Per-applicant uniqueness among <b>active</b> companies is enforced by the
/// filtered unique index <c>UX_Companies_ApplicantId_Name</c> (case-insensitive
/// via the column collation) plus an app-level accent-insensitive pre-check in
/// the service (D3). The last-active-company floor (FR-008) is a cross-aggregate
/// rule enforced in the service, not here (D5).
///
/// Mirrors the <see cref="Fund"/> (spec 029) aggregate style; <see cref="Name"/>
/// width is 200 to match the <c>Applications.CompanyName</c> snapshot column so a
/// per-application snapshot never truncates.
/// </summary>
public class Company
{
    public const int MaxNameLength = 200;

    /// <summary>Spec 037 — stable discriminator for the Name-required validation branch.</summary>
    public const string NameRequiredReason = "CompanyNameRequired";
    /// <summary>Spec 037 — stable discriminator for the Name-too-long validation branch.</summary>
    public const string NameTooLongReason = "CompanyNameTooLong";

    public int Id { get; private set; }
    public int ApplicantId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary><c>null</c> ⇔ active. Soft-archive; reversible.</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>True while the company is active (not archived).</summary>
    public bool IsActive => ArchivedAt is null;

    private Company() { }

    /// <summary>
    /// Factory: a new Company is always created active for the given applicant.
    /// Per-applicant active-name uniqueness is enforced by the DB filtered index
    /// + the service pre-check; the entity only validates the name shape.
    /// </summary>
    public Company(int applicantId, string name)
    {
        ApplicantId = applicantId;
        Name = ValidateName(name);
        ArchivedAt = null;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Renames the company. Trims/validates; no-op when equal after trim (the
    /// caller suppresses the audit row in that case). Bumps <see cref="UpdatedAt"/>.
    /// </summary>
    public void Rename(string newName)
    {
        var trimmed = ValidateName(newName);
        if (string.Equals(trimmed, Name, StringComparison.Ordinal))
        {
            return;
        }
        Name = trimmed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Soft-archives the company. Idempotent (no-op if already archived). The
    /// last-active-company floor is enforced in the service (D5), not here.
    /// </summary>
    public void Archive()
    {
        if (ArchivedAt is not null)
        {
            return;
        }
        ArchivedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Clears the archive flag. Idempotent. The active-name-collision check on
    /// unarchive is enforced in the service.
    /// </summary>
    public void Unarchive()
    {
        if (ArchivedAt is null)
        {
            return;
        }
        ArchivedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    private static string ValidateName(string name)
    {
        if (name is null)
        {
            throw Reason("Company name is required.", nameof(name), NameRequiredReason);
        }
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw Reason("Company name is required.", nameof(name), NameRequiredReason);
        }
        if (trimmed.Length > MaxNameLength)
        {
            throw Reason(
                $"Company name must be {MaxNameLength} characters or fewer.", nameof(name), NameTooLongReason);
        }
        return trimmed;
    }

    private static ArgumentException Reason(string message, string paramName, string reason)
    {
        var ex = new ArgumentException(message, paramName);
        ex.Data[Item.ValidationReasonKey] = reason;
        return ex;
    }
}
