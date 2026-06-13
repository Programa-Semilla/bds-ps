# Contracts: Line-Item Category Templates, Per-Item Impact, and Quotation Reuse

**Feature:** 035 | **Phase:** 1 (Design)

This project's "contracts" are the use-case command/service signatures, MVC routes, view models, and the dynamic-field JSON endpoints. Below: what is **added**, **changed**, **removed**. Signatures are the design target, not final code.

---

## A. Admin — Category field configuration (NET-NEW; mirrors ImpactTemplate admin)

### Application layer (`FundingPlatform.Application/Admin/`)
```csharp
// Commands (mirror Create/UpdateImpactTemplateCommand + ParameterDefinition)
record CategoryFieldDefinition(string Name, string DisplayLabel, string DataType, bool IsRequired, int SortOrder);
record CreateCategoryCommand(string Name, string? Description, List<CategoryFieldDefinition> Fields);
record UpdateCategoryCommand(int Id, string Name, string? Description, bool IsActive, List<CategoryFieldDefinition> Fields);

// AdminService gains (mirror Create/Update/GetAllImpactTemplates):
Task<List<CategoryDto>> GetAllCategoriesAsync();            // includes field count
Task<CategoryDetailDto?> GetCategoryByIdAsync(int id);      // includes fields
Task<int> CreateCategoryAsync(CreateCategoryCommand cmd);
Task UpdateCategoryAsync(UpdateCategoryCommand cmd);        // ClearFields() + re-add (full replace, like impact)
```

### Domain (`ICategoryRepository` gains)
```csharp
Task AddAsync(Category category);
Task UpdateAsync(Category category);
Task SaveChangesAsync();
Task<Category?> GetByIdWithFieldsAsync(int id);
Task<IReadOnlyList<Category>> GetAllAsync();   // active + inactive (admin list)
// existing GetAllActiveAsync / GetByIdAsync retained
```

### Web (`AdminController` gains — mirror ImpactTemplates/CreateTemplate/EditTemplate)
| Action | Route | View / VM |
|---|---|---|
| `Categories()` | GET `/Admin/Categories` | list, `CategoryAdminViewModel` |
| `CreateCategory()` / POST | GET/POST `/Admin/CreateCategory` | `CreateCategoryViewModel` (Name, Description, `List<CategoryFieldDefinitionViewModel>`) |
| `EditCategory(id)` / POST | GET/POST `/Admin/EditCategory` | `EditCategoryViewModel` (+ Id, IsActive) |

Views `Views/Admin/Categories.cshtml`, `CreateCategory.cshtml`, `EditCategory.cshtml` — copy the index-based repeating-row + JS-clone pattern from `CreateTemplate.cshtml`/`EditTemplate.cshtml` (fix the English `"Parameter ${i}"` clone leak → es-CR `"Campo"`). Sidebar link added next to `/Admin/ImpactTemplates` in `_Layout.cshtml`.

**Delete policy:** no hard delete (mirror impact templates) — deactivate via `IsActive`. Hard delete blocked when items reference the category (FR edge case) — surface es-CR if attempted.

---

## B. Applicant — per-item category fields + impact (CHANGED item flow)

### Commands (`FundingPlatform.Application/Applications/Commands/`)
```csharp
// CHANGED — drop TechnicalSpecifications; add per-item category-field + impact payloads
record AddItemCommand(int ApplicationId, string ProductName, int CategoryId,
    Dictionary<int,string?> CategoryFieldValues,           // keyed by CategoryFieldId
    int ImpactTemplateId, Dictionary<int,string?> ImpactParameterValues);  // keyed by ImpactTemplateParameterId
record UpdateItemCommand(int ItemId, int ApplicationId, string ProductName, int CategoryId,
    Dictionary<int,string?> CategoryFieldValues,
    int ImpactTemplateId, Dictionary<int,string?> ImpactParameterValues);
// RemoveItemCommand unchanged.
// REMOVED: SetApplicationImpactCommand (per-application impact gone).
```

### Service (`ApplicationService` / `IApplicationService`)
```csharp
// CHANGED: AddItemAsync/UpdateItemAsync now resolve the Category (+ its fields) and the
// ImpactTemplate (+ params), validate required values per item (all-at-once), build
// CategoryFieldValue + ImpactParameterValue lists, call item.SetCategoryFieldValues(...) +
// item.SetImpact(template, values).  Impact-template lookup = GetAllActive (no Plantilla gate).
// REMOVED: SetApplicationImpactAsync, GetImpactTemplatesAsync's Plantilla-gated variant.
// Per-item impact templates exposed via the existing IImpactTemplateRepository.GetAllActiveAsync.
```

### Web (`ItemController` — route prefix `Application/{appId}/Item`)
| Action | Route | Change |
|---|---|---|
| `Add` GET/POST | `Add` | VM `AddItemViewModel`: drop `TechnicalSpecifications`; add `CategoryFields` (descriptors + values), `ImpactTemplates` (active list), `ImpactParameters` (descriptors + values). Category select drives dynamic fields. |
| `Edit` GET/POST | `{itemId}/Edit` | same shape via `EditItemViewModel` |
| `Delete` POST | `{itemId}/Delete` | unchanged |

**REMOVED from `ApplicationController`:** the `Impact` GET/POST actions, `ImpactTemplateParameters` is **kept** (used by the per-item impact picker), the inline `AddItem`/`RemoveItem` POST + inline form on `Edit.cshtml` (canonical add is `ItemController.Add`, research D8).

### Dynamic-field JSON endpoints
```
GET /Application/{id}/Impact/TemplateParameters/{templateId}   // KEPT — impact param descriptors
GET /Application/{appId}/Item/Category/{categoryId}/Fields      // NEW — category field descriptors
   → [{ id, name, displayLabel, dataType, isRequired }]
```
Both feed one shared client-side renderer (`DataType → input control`: Text→text, Decimal→number step .01, Integer→number step 1, Date→date), extracted from the duplicated switch in `Impact.cshtml`.

---

## C. Applicant — quotation reuse (NEW; no schema change)

### Service (`ApplicationService`)
```csharp
// NEW
Task ReuseQuotationAsync(int applicationId, int itemId, int sourceQuotationId,
    decimal price, string currency, DateOnly validUntil);
// Loads app; finds sourceQuotation (must belong to SAME application, FR-008);
// builds new Quotation(sourceQuotation.SupplierId, sourceQuotation.SupplierBranchId,
//   sourceQuotation.DocumentId, price, validUntil, currency)
//   → SetCurrencyAndAmountAsync(...) → item.AttachQuotation(supplier, branch, quotation).
// NO upload, NO new Document.

// CHANGED — reference-counted blob retention:
//   RemoveQuotationAsync(...) and ReplaceQuotationDocumentAsync(...) delete the blob ONLY when
//   application.CountQuotationsReferencingDocument(documentId) == 0 after the row is detached.

// Reuse candidate query (for the picker):
Task<List<ReusableQuotationDto>> GetReusableQuotationsAsync(int applicationId, int excludeItemId);
//   → { SourceQuotationId, SupplierName, BranchName, DocumentFileName, Currency }
```

### Web (`SupplierController` / quotation add flow — route `Application/{appId}/Item/{itemId}/Supplier`)
- `Add` GET view gains a **"Reutilizar cotización existente"** mode: a picker listing `GetReusableQuotationsAsync`; selecting one switches the form to reuse (hide upload + supplier lookup; show editable Price/Currency/ValidUntil via the existing `_QuoteFields.cshtml` / `IQuoteFieldsModel`). POST dispatches to `ReuseQuotationAsync` when a source is chosen, else the existing add-new path.
- `AddSupplierViewModel` gains `SourceQuotationId? `, `ReusableQuotations`, and makes `QuotationFile` optional when reusing.

---

## D. Cross-surface display (FR-009) — DTO/projection changes

```csharp
// ItemDto.Impact (already exists, currently null) → POPULATED per item.
//   + new: ItemDto.CategoryFields : List<CategoryFieldValueDto(Label, Value)>
// ApplicationDto.Impact → REMOVED.
// ReviewItemDto.ImpactTemplateName/ImpactParameters → fed from per-item data
//   (fix ReviewService.MapToReviewDto duplication); + CategoryFields list.
// ApplicationReviewViewModel: impact moves from a single card to per ReviewItemRow;
//   + per-row category fields.  GetApplicationReviewProjection includes Item.ImpactTemplate
//   + Item.ImpactParameterValues + Item.CategoryFieldValues.ThenInclude(CategoryField).
// FundingAgreementItemRowDto → + CategoryFields + Impact (PDF partial renders a per-line block).
// AI: SupplierAssembler ItemAssembly → + ProductName + CategoryFields (scrubbed via PII regex);
//   impact EXCLUDED (research D6, pending user confirmation).
```

### EF Includes to update
- `ApplicationRepository.GetByIdWithDetailsAsync` / `:74` — move impact includes from Application onto `Items` (`Item.ImpactTemplate`, `Item.ImpactParameterValues.ThenInclude(ImpactTemplateParameter)`, `Item.CategoryFieldValues.ThenInclude(CategoryField)`).
- `GetApplicationReviewProjection` `:53` — same per-item includes.

---

## E. Removed contracts (Plantilla gating — research D4)

```csharp
// IPlantillaService: drop ImpactTemplateIds from Create/EditPlantillaCommand + PlantillaDetail;
//   drop ImpactTemplateCount from PlantillaListRow.
// Plantilla domain: drop ImpactTemplates / AttachImpactTemplate / DetachImpactTemplate;
//   AssignTo drops the zero-template guard + CSV snapshot.
// ProcessPlantilla: drop ImpactTemplateIdsCsv + ImpactTemplateIds().
// AdminPlantilla{Create,Edit}ViewModel: drop ImpactTemplateIds/AvailableImpactTemplates;
//   drop AdminPlantillaImpactTemplateOption; AdminPlantillasController drops LoadImpactTemplateOptionsAsync.
```
