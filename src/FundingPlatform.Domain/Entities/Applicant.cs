using FundingPlatform.Domain.Enums;
using FundingPlatform.Domain.ValueObjects;

namespace FundingPlatform.Domain.Entities;

public class Applicant
{
    private readonly List<Application> _applications = [];

    public int Id { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string LegalId { get; private set; } = string.Empty;

    /// <summary>Spec 026 — kind of legal ID stored in <see cref="LegalId"/>. Nullable for legacy / non-applicant-role admin users.</summary>
    public IdentificationType? IdentificationType { get; private set; }

    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public decimal? PerformanceScore { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public IReadOnlyList<Application> Applications => _applications.AsReadOnly();

    private Applicant() { }

    public Applicant(
        string userId,
        string legalId,
        string firstName,
        string lastName,
        string email,
        string? phone,
        decimal? performanceScore,
        IdentificationType? identificationType = null)
    {
        UserId = userId;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Phone = phone;
        PerformanceScore = performanceScore;
        // Spec 026 — when a type is supplied, route the legal ID through the VO so
        // the stored value is canonical. Otherwise store as-is (legacy / typeless).
        if (identificationType is { } type && !string.IsNullOrWhiteSpace(legalId))
        {
            var id = Identification.From(type, legalId);
            LegalId = id.Value;
            IdentificationType = type;
        }
        else
        {
            LegalId = legalId;
            IdentificationType = identificationType;
        }
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spec 026 — sets the legal ID and its type through the <see cref="Identification"/>
    /// value object (canonicalises + validates). Throws <see cref="ArgumentException"/>
    /// on a shape mismatch.
    /// </summary>
    public void SetIdentification(IdentificationType type, string rawValue)
    {
        var id = Identification.From(type, rawValue);
        LegalId = id.Value;
        IdentificationType = type;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateProfile(
        string legalId,
        string firstName,
        string lastName,
        string email,
        string? phone,
        IdentificationType? identificationType = null)
    {
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        Phone = phone;
        // Spec 026 — canonicalise via the VO when a type is supplied; otherwise store raw.
        if (identificationType is { } type && !string.IsNullOrWhiteSpace(legalId))
        {
            var id = Identification.From(type, legalId);
            LegalId = id.Value;
            IdentificationType = type;
        }
        else
        {
            LegalId = legalId;
            IdentificationType = identificationType;
        }
        UpdatedAt = DateTime.UtcNow;
    }
}
