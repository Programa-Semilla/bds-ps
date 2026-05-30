namespace FundingPlatform.Web.Models;

public enum AdminUserStatus
{
    Active,
    Disabled,
}

public enum AdminUserRole
{
    Applicant,
    Reviewer,
    // Spec 021 / FR-007 — global-scope supplier-catalog role; sits between Reviewer
    // and Admin in display priority (see SelectPrimaryRole + StatusVisualMap.For).
    SupplierAdmin,
    Admin,
}
