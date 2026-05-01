# Permission Matrix Contract: Supplier Catalog

**Date:** 2026-04-30
**Sources:** spec FR-070, research.md R6.

This document maps every route/method on the new and modified controllers to the authorization rules that gate it. Translates FR-070's permission matrix into per-route enforcement.

| Route | Verb | Role gate | Ownership / state gate | Notes |
|---|---|---|---|---|
| `/Application/{appId}/Item/{itemId}/Supplier/Add` | GET | `Applicant` | `application.ApplicantId == currentApplicantId` | Existing `VerifyOwnershipAsync` |
| `/Application/{appId}/Item/{itemId}/Supplier/Search` | GET | `Applicant` | `application.ApplicantId == currentApplicantId` | Lookup applies the visibility filter described in http-routes.md §1 |
| `/Application/{appId}/Item/{itemId}/Supplier/Add` | POST | `Applicant` | as above | All three sub-payloads route through this single endpoint |
| `/Application/{appId}/Item/{itemId}/Supplier/{supplierId}/EditDraft` | POST | `Applicant` | `application.ApplicantId == currentApplicantId && application.Status == Draft && supplier.Status == Draft && supplier.CreatedByApplicantId == currentApplicantId` | Domain `Supplier.RenameByApplicant` is the second guard |
| `/Application/{appId}/Item/{itemId}/Supplier/{supplierId}/Branch/{branchId}/Edit` | POST | `Applicant` | `application.ApplicantId == currentApplicantId && application.Status == Draft && branch.CreatedByApplicantId == currentApplicantId` | FR-014 |
| `/Admin/Suppliers` | GET | `Admin` | none | Default filter `PendingReview` |
| `/Admin/Suppliers/{supplierId}` | GET | `Admin` | none | Detail with branches + referencing applications |
| `/Admin/Suppliers/{supplierId}/Edit` | POST | `Admin` | none | `Supplier.EditByAdmin` invoked |
| `/Admin/Suppliers/{supplierId}/Branch/{branchId}/Edit` | POST | `Admin` | none | `Supplier.EditBranch` invoked |
| `/Admin/Suppliers/{supplierId}/Verify` | POST | `Admin` | `supplier.Status != Draft` (domain-enforced) | Verifier identity = `User.FindFirstValue(ClaimTypes.NameIdentifier)` |
| `/Admin/Suppliers/{supplierId}/Reject` | POST | `Admin` | `supplier.Status != Draft && reason != null/whitespace` | ViewModel `[Required]` validation + domain guard |
| `/Application/{appId}/Submit` (existing) | POST | `Applicant` | unchanged | New side effect: walk owned Drafts, call `SubmitForReview()` |
| `/Application/{appId}/Review/Details` (existing) | GET | `Reviewer` | unchanged | New badges/banners; same auth |

## Cross-cutting rules

- **Reviewer**: read-only on the entire supplier catalog. No edit endpoints exist for the `Reviewer` role. Any attempt to POST to admin routes returns 403 by the role gate alone.
- **Applicant other**: can never see suppliers in `Draft` status (regardless of ownership). Can see `PendingReview` only when they're the creator. Verified suppliers are visible to all applicants. Rejected suppliers return a localized "contact admin" partial on lookup but do not allow new quotation creation.
- **Anonymous**: every controller mentioned here requires authentication; no anonymous access.

## Failure modes

| Failure | Status | Body |
|---|---|---|
| Anonymous request | 401 | Redirect to login |
| Authenticated but wrong role | 403 | ProblemDetails JSON or HTML based on `Accept` header (existing exception filter) |
| Authenticated, correct role, missing ownership | 403 | Same as above |
| Domain guard violation (e.g., `Supplier.Verify` on a Draft) | 400 | ProblemDetails with the domain exception's message |
| Concurrent unique-constraint violation on legal ID | 303 | Redirect per R4 (NOT a failure to the user — they get a banner) |
