# Contracts: Line-Item Category Templates, Application-Level Impacts with Per-Item Attribution, and Quotation Reuse

**Feature:** 035 | **Phase:** 1 (Design) | **Evolved:** 2026-06-16

This project's "contracts" are the use-case command/service signatures, MVC routes, view models, and the dynamic-field JSON endpoints. Below: what is **added**, **changed**, **removed**. Signatures are the design target, not final code.

> **Evolution (2026-06-16).** Section A (category admin) and Section C (quotation reuse) are **unchanged** (built). Section B (item flow) is reshaped: the item form no longer carries an impact template + values — it carries a **multi-impact attribution** + a **short justification**. New **Section B′** adds the application-level impacts manager. Section D (display) reflects app-level impacts + per-item attribution/justification.

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

## B. Applicant — per-item category fields + impact attribution + justification (CHANGED item flow — D14/D15)

### Commands (`FundingPlatform.Application/Applications/Commands/`)
```csharp
// CHANGED — drop TechnicalSpecifications; category-field payload stays; impact is now ATTRIBUTION + justification
record AddItemCommand(int ApplicationId, string ProductName, int CategoryId,
    Dictionary<int,string?> CategoryFieldValues,           // keyed by CategoryFieldId
    IReadOnlyList<int> ApplicationImpactIds,               // attribution — must be ≥1 at submit; subset of app's declared impacts
    string? ImpactJustification);                          // ≤300 chars; required (non-empty) at submit
record UpdateItemCommand(int ItemId, int ApplicationId, string ProductName, int CategoryId,
    Dictionary<int,string?> CategoryFieldValues,
    IReadOnlyList<int> ApplicationImpactIds,
    string? ImpactJustification);
// RemoveItemCommand unchanged.
// REMOVED: the prior 035 per-item ImpactTemplateId + ImpactParameterValues on these commands.
```

### Service (`ApplicationService` / `IApplicationService`)
```csharp
// CHANGED: AddItemAsync/UpdateItemAsync resolve the Category (+ fields), validate the attribution
//   (every ApplicationImpactId belongs to THIS application's declared impacts), build the
//   CategoryFieldValue list, then call item.SetCategoryFieldValues(...), item.AttributeImpacts(ids),
//   item.SetImpactJustification(text). NO impact-template resolution here anymore.
// REMOVED: the prior 035 per-item SetImpact wiring.
```

### Web (`ItemController` — route prefix `Application/{appId}/Item`)
| Action | Route | Change |
|---|---|---|
| `Add` GET/POST | `Add` | VM `AddItemViewModel`: drop `TechnicalSpecifications`; keep `CategoryFields`; replace impact-template/params with `DeclaredImpacts` (the app's `ApplicationImpact`s for a multi-select), `SelectedApplicationImpactIds`, and `ImpactJustification` (textarea, maxlength 300). Category select drives dynamic fields. Empty-state when the app has no declared impacts (link to the impacts step). |
| `Edit` GET/POST | `{itemId}/Edit` | same shape via `EditItemViewModel` (pre-checks current attributions) |
| `Delete` POST | `{itemId}/Delete` | unchanged |

### Dynamic-field JSON endpoints
```
GET /Application/{id}/Impact/TemplateParameters/{templateId}   // KEPT — used by the APP-LEVEL impacts manager (Section B′)
GET /Application/{appId}/Item/Category/{categoryId}/Fields      // category field descriptors (unchanged)
   → [{ id, name, displayLabel, dataType, isRequired }]
```
The category-field endpoint feeds the shared client-side renderer (`DataType → input control`: Text→text, Decimal→number step .01, Integer→number step 1, Date→date). The item form's impact part is a **server-rendered multi-select** of the application's declared impacts (no JSON endpoint needed) plus a justification textarea with a live character counter.

---

## B′. Applicant — application-level impacts manager (NEW — D13/D15)

The pre-035 single-impact step is generalized to a one-or-more manager (re-introducing `ApplicationController.Impact` in a multi-impact shape).

### Commands
```csharp
record AddApplicationImpactCommand(int ApplicationId, int ImpactTemplateId,
    Dictionary<int,string?> ParameterValues);   // keyed by ImpactTemplateParameterId; required values validated
record RemoveApplicationImpactCommand(int ApplicationId, int ApplicationImpactId);
```

### Service (`ApplicationService`)
```csharp
Task AddApplicationImpactAsync(AddApplicationImpactCommand cmd);
//   resolves the ImpactTemplate (GetAllActive — no Plantilla gate, D3), validates required param values,
//   builds ImpactParameterValue list, calls application.AddImpact(template, values) (rejects duplicate template).
Task RemoveApplicationImpactAsync(RemoveApplicationImpactCommand cmd);
//   application.RemoveImpact(id) — also strips ItemImpact attributions referencing it (SC-007).
Task<List<ImpactTemplateOptionDto>> GetActiveImpactTemplatesAsync();   // for the "add impact" picker
```

### Web (`ApplicationController`)
| Action | Route | View / VM |
|---|---|---|
| `Impacts` GET | `/Application/{id}/Impacts` | `ApplicationImpactsViewModel` — declared impacts list + an "add impact" form (active-template select → dynamic params via the kept `TemplateParameters` endpoint) |
| `AddImpact` POST | `/Application/{id}/Impacts/Add` | dispatch `AddApplicationImpactCommand`; re-render on validation error |
| `RemoveImpact` POST | `/Application/{id}/Impacts/{applicationImpactId}/Remove` | dispatch `RemoveApplicationImpactCommand` |

Empty-state when no active impact templates exist ("no hay plantillas de impacto activas", D7). The submit gate (`submit-gate.js`) reflects: ≥1 declared impact AND every item attributed + justified + category-complete.

---

## C. Applicant — quotation reuse (NEW; no schema change)

### Service (`ApplicationService`)
```csharp
// NEW
Task ReuseQuotationAsync(int applicationId, int itemId, int sourceQuotationId,
    decimal price, string currency, DateOnly validUntil);
// Loads app; finds sourceQuotation (must belong to SAME application, FR-010);
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

## D. Cross-surface display (FR-011) — DTO/projection changes (D16)

```csharp
// APPLICATION LEVEL (declared impacts, one or more):
//   ApplicationDto.Impacts : List<ApplicationImpactDto(Id, ImpactTemplateName, List<ImpactParameterValueDto(Label,Value)>)>
//     (replaces the single ApplicationDto.Impact removed in prior 035).
// PER ITEM:
//   ItemDto.CategoryFields : List<CategoryFieldValueDto(Label, Value)>            // unchanged
//   ItemDto.AttributedImpactNames : List<string>                                  // from ItemImpact → ApplicationImpact → template name
//   ItemDto.ImpactJustification : string?
//   (ItemDto.Impact — the prior per-item single impact — REMOVED.)
// ReviewItemDto: ImpactTemplateName/ImpactParameters → replaced by AttributedImpactNames + ImpactJustification + CategoryFields.
//   ApplicationReviewViewModel: an application-level Impacts card + per-row attributed-impact names + justification + category fields.
//   GetApplicationReviewProjection includes Application.Impacts.ThenInclude(ip => ip.ParameterValues).ThenInclude(ImpactTemplateParameter)
//   + Application.Impacts.ThenInclude(ImpactTemplate) + Item.ItemImpacts.ThenInclude(ApplicationImpact.ImpactTemplate)
//   + Item.CategoryFieldValues.ThenInclude(CategoryField).
// FundingAgreementItemRowDto → + CategoryFields + AttributedImpactNames + ImpactJustification;
//   the agreement also gains an application-level impacts block (declared impacts + values).
// AI: SupplierAssembler ItemAssembly → + ProductName + CategoryFields + ImpactJustification (all scrubbed via PII regex);
//   raw impact parameter values EXCLUDED (research D16; flip to included only if strict FR-011 is chosen).
```

### EF Includes to update
- `ApplicationRepository.GetByIdWithDetailsAsync` — include `Application.Impacts` (→ `ParameterValues` → `ImpactTemplateParameter`, and → `ImpactTemplate`) and `Item.ItemImpacts` (→ `ApplicationImpact` → `ImpactTemplate`) + `Item.CategoryFieldValues.ThenInclude(CategoryField)`.
- `GetApplicationReviewProjection` — same includes.

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
