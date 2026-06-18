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
    // Spec 038 — global-scope provider-compliance role (renamed from SupplierAdmin);
    // sits between Reviewer and Admin in display priority (see SelectPrimaryRole +
    // StatusVisualMap.For). Reachable only on /Admin/Suppliers*.
    Auditor,
    Admin,
}
