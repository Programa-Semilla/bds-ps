# HTTP Route Contracts: Centralized Supplier Catalog

**Date:** 2026-04-30
**Plan:** [../plan.md](../plan.md)
**Data Model:** [../data-model.md](../data-model.md)

This document is the contract for every HTTP route the feature adds or modifies. It is the source-of-truth for ViewModel shapes, redirects, and response codes — `tasks.md` will translate it into per-route implementation tasks.

All routes are server-rendered MVC actions returning Razor views or 30x redirects. No JSON API endpoints are introduced; the supplier-search-by-legal-ID lookup uses a tiny `application/json` partial described below as the only exception.

---

## 1. Applicant flow

### `SupplierController` (modified, existing controller)

Routed under: `[Route("Application/{appId:int}/Item/{itemId:int}/Supplier")]`, `[Authorize(Roles = "Applicant")]`.

#### `GET /Application/{appId}/Item/{itemId}/Supplier/Add`

**Behavior**: Renders the Add Quotation form. The form is a step-flow (no client-side routing): step 1 is a legal-ID input + "Buscar"; the response (after lookup) renders one of three partial states inline (no full page reload — this is the single concession to client-side scripting per NFR-004's debounce requirement).

**Query string**: `?supplierId=<int>&banner=concurrent` — used by the R4 redirect path. When present, the view auto-runs the lookup against the existing supplier and shows a `concurrent`-style banner ("acaba de ser registrado por otro postulante…").

**ViewModel**: `AddSupplierViewModel` (modified)

```csharp
public class AddSupplierViewModel
{
    public int ApplicationId { get; set; }
    public int ItemId { get; set; }

    // Step 1 — always present
    [Required, MaxLength(50), Display(Name = "...")]
    public string SupplierLegalId { get; set; } = string.Empty;

    // Step 2 — lookup result
    public SupplierLookupResultViewModel? LookupResult { get; set; }
    public string? ConcurrentBanner { get; set; }

    // Step 3a — branch selected
    public int? SelectedBranchId { get; set; }

    // Step 3b — new branch under existing supplier
    public AddBranchInputViewModel? NewBranch { get; set; }

    // Step 3c — brand-new supplier (Draft creation)
    public NewSupplierInputViewModel? NewSupplier { get; set; }

    // Quotation fields (always required at submit time)
    [Required, Range(0.01, double.MaxValue)] public decimal Price { get; set; }
    [Required, StringLength(3, MinimumLength = 3)] public string Currency { get; set; } = string.Empty;
    [Required] public DateOnly ValidUntil { get; set; }
    [Required] public IFormFile? QuotationFile { get; set; }
}
```

`SupplierLookupResultViewModel` (NEW):

```csharp
public class SupplierLookupResultViewModel
{
    public int SupplierId { get; set; }
    public string LegalId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool HasElectronicInvoice { get; set; }
    public bool IsCompliantCCSS { get; set; }
    public bool IsCompliantHacienda { get; set; }
    public bool IsCompliantSICOP { get; set; }
    public SupplierVerificationStatus VerificationStatus { get; set; }   // for the "Pending verification" badge
    public IReadOnlyList<BranchSummary> Branches { get; set; } = Array.Empty<BranchSummary>();
}

public record BranchSummary(int Id, string BranchName, string? ContactName, string? Email, string? Province);
```

`AddBranchInputViewModel` (NEW):

```csharp
public class AddBranchInputViewModel
{
    [Required, MaxLength(200)] public string BranchName { get; set; } = string.Empty;
    [MaxLength(200)] public string? ContactName { get; set; }
    [EmailAddress, MaxLength(256)] public string? Email { get; set; }
    [Phone, MaxLength(20)] public string? Phone { get; set; }
    [MaxLength(500)] public string? AddressLine { get; set; }
    [MaxLength(100)] public string? Province { get; set; }
    [MaxLength(500)] public string? ShippingDetails { get; set; }
    [MaxLength(500)] public string? WarrantyInfo { get; set; }
}
```

`NewSupplierInputViewModel` (NEW):

```csharp
public class NewSupplierInputViewModel
{
    [Required, MaxLength(300)] public string Name { get; set; } = string.Empty;
    [Required] public AddBranchInputViewModel FirstBranch { get; set; } = new();
    // No HasElectronicInvoice. No compliance booleans. (FR-020, FR-040)
}
```

**Response**: 200 with the Razor view. ModelState errors render inline.

#### `GET /Application/{appId}/Item/{itemId}/Supplier/Search?legalId={x}`

**Behavior**: Server-rendered partial (HTML fragment, NOT JSON) returning the lookup result block. The Add page's tiny vanilla-JS hook fetches this URL with `Accept: text/html` after the 250 ms debounce and replaces the lookup-result region in the DOM.

**Authorization**: `[Authorize(Roles = "Applicant")]` + `VerifyOwnershipAsync(appId)`.

**Lookup logic** (FR-001..005, FR-023, R6 ownership):

1. Normalize `legalId` (trim, uppercase).
2. Query the supplier:
   - status `Verified` → return for any applicant.
   - status `PendingReview` AND `CreatedByApplicantId == currentApplicantId` → return (FR-002 creator-only).
   - status `Draft` AND `CreatedByApplicantId == currentApplicantId` AND parent application is in Draft → return (FR-003).
   - status `Rejected` → return a `lookup-rejected` partial with the localized "contact admin" error (FR-004).
   - otherwise (no row, or other-applicant Pending/Draft) → return a `lookup-empty` partial offering the new-supplier form.
3. Render the appropriate partial (`_LookupHit.cshtml`, `_LookupEmpty.cshtml`, or `_LookupRejected.cshtml`).

**Response**: 200 with HTML partial. Empty `legalId` → 400 with empty body.

#### `POST /Application/{appId}/Item/{itemId}/Supplier/Add`

**Behavior**: Single endpoint that handles all three save paths via the populated fields on `AddSupplierViewModel`:

- `SelectedBranchId.HasValue` → existing-branch flow (US1).
- `NewBranch != null && LookupResult != null` → add-new-branch-under-existing-supplier flow (US2).
- `NewSupplier != null` → create-draft-supplier flow (US3).

**Validation**: ModelState is validated; mutually exclusive sub-payloads asserted (controller rejects if more than one sub-payload is non-null). Quotation fields always required.

**Service calls**:

- US1: `ApplicationService.AddQuotationToExistingBranchAsync(appId, itemId, branchId, price, currency, validUntil, fileStream)`.
- US2: `SupplierCatalogService.AddBranchUnderExistingSupplierAsync(supplierId, AddBranchInputViewModel, currentApplicantId)` returning the new branch ID, then `AddQuotationToExistingBranchAsync(...)`.
- US3: `SupplierCatalogService.CreateDraftWithBranchAsync(legalId, name, NewSupplierInputViewModel.FirstBranch, currentApplicantId)`. On `Result.RetryWithExisting`, redirect 303 to `GET Add?supplierId={existingId}&banner=concurrent` (R4). On success, get the default branch and call `AddQuotationToExistingBranchAsync(...)`.

**Response**: 303 redirect to `/Application/{appId}/Details` with `TempData["SuccessMessage"]` set, OR 200 with ModelState errors and the same view.

#### `POST /Application/{appId}/Item/{itemId}/Supplier/{supplierId}/EditDraft`

**Behavior**: Edit the name of a Draft supplier (FR-022). The applicant must be the supplier's creator AND the parent application must be in Draft status.

**ViewModel**: `EditDraftSupplierViewModel { SupplierId, Name }`.

**Authorization**: `[Authorize(Roles = "Applicant")]` + `VerifyOwnershipAsync(appId)` + `SupplierCatalogService.AssertEditableByApplicant(supplierId, currentApplicantId, appId)`.

**Response**: 303 to `/Application/{appId}/Details`.

#### `POST /Application/{appId}/Item/{itemId}/Supplier/{supplierId}/Branch/{branchId}/Edit`

**Behavior**: Edit a branch's contact fields. Permitted only for the branch's `CreatedByApplicantId` while the parent application is `Draft` (FR-014).

**ViewModel**: `EditBranchByApplicantViewModel { /* AddBranchInputViewModel fields */ }`.

**Response**: 303 to `/Application/{appId}/Details`.

---

## 2. Application service modifications

### `ApplicationService.AddSupplierQuotationAsync` — REPLACED

The current method (single command with flat supplier fields) is replaced by three narrower methods:

- `Task<Quotation> AddQuotationToExistingBranchAsync(int appId, int itemId, int branchId, decimal price, string currency, DateOnly validUntil, Stream fileStream, string fileName, string contentType, long fileSize)`
- `Task<int> SupplierCatalogService.AddBranchUnderExistingSupplierAsync(int supplierId, AddBranchInput input, int createdByApplicantId)` (returns new branch ID)
- `Task<Result<int>> SupplierCatalogService.CreateDraftWithBranchAsync(string legalId, string name, AddBranchInput firstBranch, int createdByApplicantId)` (returns new supplier ID, OR `Result.RetryWithExisting(int existingSupplierId)` on UNIQUE-constraint collision per R4)

The `AddQuotationToExistingBranchAsync` method writes both `SupplierId` and `SupplierBranchId` on the new `Quotation` row from the same loaded branch (preserves invariant — see data-model.md).

### `ApplicationService.SubmitAsync` — MODIFIED

Add a new step before the existing submit logic:

```text
1. Load application with all items, quotations, branches, and the suppliers behind those branches.
2. For each Supplier where (Status == Draft && CreatedByApplicantId == application.ApplicantId):
       supplier.SubmitForReview();   // domain method, idempotent
3. Continue with the existing submit transition (Draft -> Submitted etc.).
4. Save inside the same transaction.
```

This is the FR-024 atomic-with-submit guarantee.

### `ReviewService` — MODIFIED

The query that materializes review-screen data now joins `SupplierBranches`:

```csharp
var rows = await _db.Quotations
    .Where(q => q.Item.ApplicationId == appId)
    .Select(q => new
    {
        Quotation = q,
        Supplier = q.Supplier,
        Branch = q.Item.SupplierBranches.FirstOrDefault(b => b.Id == q.SupplierBranchId)
        // ... or Include + projection in repo
    })
    .ToListAsync();
```

Then `SupplierScore.ComputeForItem(rows.Select(r => (r.Quotation, r.Supplier, r.Branch)).ToList())` per R5.

---

## 3. Admin flow

### `Admin/AdminSuppliersController` (NEW)

Routed under: `[Area("Admin")]` (the project uses convention-based admin routing under `Controllers/Admin/`, mirroring `AdminUsersController` and `AdminReportsController`).
Routes: `[Route("Admin/Suppliers")]`. `[Authorize(Roles = "Admin")]`.

#### `GET /Admin/Suppliers`

**Behavior**: Render the Suppliers list with default filter `VerificationStatus = PendingReview`. Filters: status, partial legal ID, partial name, "has incomplete compliance" (any of the four flags is `false`). Pagination via the existing `_PaginationFooter` partial used by spec 010 reports — page size convention reused (default 25 per page).

**ViewModel**: `AdminSupplierListViewModel { Items, Filter, Page, TotalCount, PageSize }`.

#### `GET /Admin/Suppliers/{supplierId:int}`

**Behavior**: Render the supplier detail view: identity fields (read-only labels with inline-edit affordances), all branches (table with edit affordances), and the list of applications currently referencing this supplier (read-only links). FR-031 required filters render at the top; current applicant references render at the bottom.

**ViewModel**: `AdminSupplierDetailViewModel { Supplier, Branches, ReferencingApplications }`.

#### `POST /Admin/Suppliers/{supplierId:int}/Edit`

**Behavior**: Persist edits to the supplier identity (name, e-invoice flag, all three compliance flags). Calls `Supplier.EditByAdmin(...)`. FR-032, FR-033.

**ViewModel**: `AdminEditSupplierViewModel { SupplierId, Name, HasElectronicInvoice, IsCompliantCCSS, IsCompliantHacienda, IsCompliantSICOP }`.

**Response**: 303 to `GET /Admin/Suppliers/{supplierId}`.

#### `POST /Admin/Suppliers/{supplierId:int}/Branch/{branchId:int}/Edit`

**Behavior**: Persist edits to a branch. FR-034. Calls `Supplier.EditBranch(...)`.

**ViewModel**: `AdminEditBranchViewModel` (full branch field set).

#### `POST /Admin/Suppliers/{supplierId:int}/Verify`

**Behavior**: Transition `PendingReview → Verified` or `Rejected → Verified`. Calls `Supplier.Verify(currentAdminUserId)`. FR-035.

**Authorization check**: parses `currentAdminUserId` from claims; refuses if the current user is not in the `Admin` role.

**Response**: 303 to `GET /Admin/Suppliers/{supplierId}` with `TempData["SuccessMessage"]`.

#### `POST /Admin/Suppliers/{supplierId:int}/Reject`

**Behavior**: Transition `PendingReview → Rejected` or `Verified → Rejected`. Requires non-empty `RejectionReason` in the form post. Calls `Supplier.Reject(currentAdminUserId, reason)`. FR-035.

**ViewModel**: `AdminRejectSupplierViewModel { SupplierId, Reason }` with `[Required, MaxLength(1000)]` on `Reason`.

**Response**: 303 to `GET /Admin/Suppliers/{supplierId}`. ModelState error → 400 (or re-render detail view with error highlighted).

---

## 4. Reviewer-facing changes

No new routes. The existing `ReviewController` views render the new score flags through the modified `SupplierScore` value object:

- `Views/Review/Details.cshtml` — quotation row gains a `_PendingVerificationBadge.cshtml` partial when `score.IsSupplierVerified == false && supplier.VerificationStatus != Rejected`. Renders next to the existing recommendation badge column.
- `Views/Review/Details.cshtml` — banner partial `_RejectedSuppliersBanner.cshtml` shown when at least one quotation references a `Rejected` supplier (FR-052). Banner shows the count.

---

## 5. Validation responses

| Failure | HTTP | Behavior |
|---|---|---|
| Applicant attempts to edit a non-Draft supplier | 403 | Domain `InvalidOperationException` mapped by existing exception filter to ProblemDetails 403 |
| Applicant attempts to view another applicant's PendingReview supplier (lookup) | 200 with `_LookupEmpty.cshtml` | Indistinguishable from "no supplier exists" — no information leak |
| Two applicants race-create the same legal ID | 303 redirect | R4: redirect with `?supplierId=<existing>&banner=concurrent` |
| Reject with empty reason | 200 | ModelState error rendered inline on detail page |
| Admin rejects a Draft supplier (impossible from UI; only via direct API) | 403 / 400 | Domain throws `InvalidOperationException`; mapped to 400 |
| Quotation save with `SupplierBranchId` not belonging to `SupplierId` | impossible by construction | Service writes both atomically from a loaded branch |
| Add quotation against a Rejected supplier | 400 | Service-level guard rejects; UI also blocks (FR-053) |

---

## 6. Localization keys

Added to `Resources/Suppliers.resx`:

| Key | Spanish (es-CR) |
|---|---|
| `LookupRejectedMessage` | El proveedor está rechazado por un administrador. Ponte en contacto con el equipo administrativo si necesitas ayuda. |
| `LookupConcurrentBanner` | Este proveedor acaba de ser registrado por otro postulante. Selecciona una sucursal o agrega una nueva. |
| `BranchPicker_Title` | Selecciona la sucursal del proveedor |
| `BranchPicker_AddNew` | Agregar nueva sucursal |
| `Branch_Default` | Sede principal |
| `PendingVerificationBadge` | Pendiente de verificación |
| `NewSupplierForm_Hint` | El administrador validará la información del proveedor luego de la postulación. |

Added to `Resources/AdminSuppliers.resx`:

| Key | Spanish (es-CR) |
|---|---|
| `Page_Title` | Catálogo de proveedores |
| `FilterStatus_PendingReview` | Pendientes de revisión |
| `FilterStatus_Verified` | Verificados |
| `FilterStatus_Rejected` | Rechazados |
| `FilterStatus_All` | Todos |
| `Verify_Confirm` | ¿Verificar este proveedor? |
| `Reject_RequireReason` | Indica la razón de rechazo. |
| `RejectedSuppliersBanner` | Esta postulación referencia {count} proveedor(es) rechazado(s) por administración. |

(Final wording is the voice-guide reviewer's call per spec 012; these are the working strings.)
