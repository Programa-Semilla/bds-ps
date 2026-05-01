namespace FundingPlatform.Domain.Enums;

/// <summary>
/// Lifecycle status of a Supplier in the centralized supplier catalog (spec 013).
/// Backed by TINYINT in SQL.
/// </summary>
public enum SupplierVerificationStatus : byte
{
    /// <summary>
    /// Applicant-created and not yet submitted with an application. Visible only to creator.
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Submitted alongside an application; awaiting admin verification. Visible to creator and admin.
    /// </summary>
    PendingReview = 1,

    /// <summary>
    /// Admin-verified and reusable across all applicants.
    /// </summary>
    Verified = 2,

    /// <summary>
    /// Admin-rejected. Cannot be used for new quotations; surfaces a banner on existing applications.
    /// </summary>
    Rejected = 3,
}
