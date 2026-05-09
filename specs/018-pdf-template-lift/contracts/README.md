# Phase 1: Interface Contracts — PDF Template Lift

**Spec:** [../spec.md](../spec.md) · **Plan:** [../plan.md](../plan.md) · **Data model:** [../data-model.md](../data-model.md)
**Date:** 2026-05-08

This feature exposes two HTTP form contracts (applicant Create + reviewer ReviewItem) and one internal projection contract (`FundingAgreementService` → `FundingAgreementDocumentViewModel`). No public-API or external machine-readable schema is added.

---

## Contract 1 — `POST /Application/Create` (applicant — capture company name)

**Purpose**: applicant creates a draft Application and supplies the legally-distinct commercial entity name (`Empresa solicitante`) at the same step.

**Request (form-encoded)**:

| Field         | Type   | Required | Constraints                                                       |
|---------------|--------|----------|-------------------------------------------------------------------|
| `CompanyName` | string | yes      | trimmed; non-blank; ≤200 chars                                    |
| `__RequestVerificationToken` | string | yes | ASP.NET anti-forgery |

**Response — 200 (validation error)**:

- Renders `Application/Create.cshtml` with `ModelState` containing the user-facing error message tied to `CompanyName`.
- No row written to `dbo.Applications`.

**Response — 302 (success)**:

- Redirect to `GET /Application/Edit/{id}` with the new draft Application.
- One row written to `dbo.Applications` with `CompanyName` populated.

**Domain rule**: `Application` constructor calls `SetCompanyName(string)` which enforces the field-level invariants. Rule lives on the entity per Constitution II.

---

## Contract 2 — `POST /Review/{id}/ReviewItem` (reviewer — capture line code with decision)

**Purpose**: reviewer records a per-item Approve / Reject / RequestMoreInfo decision and assigns the line code that surfaces in the Funding Agreement PDF tables.

**Request (form-encoded)**:

| Field                | Type    | Required | Constraints                                                       |
|----------------------|---------|----------|-------------------------------------------------------------------|
| `ItemId`             | int     | yes      | must belong to the Application identified by route `{id}`         |
| `Decision`           | string  | yes      | `Approve` \| `Reject` \| `RequestMoreInfo`                        |
| `LineCode`           | string  | yes when Decision is `Approve` or `Reject` | trimmed; non-blank; ≤16 chars; unique within Application |
| `Comment`            | string  | no       | ≤2000 chars (existing constraint)                                 |
| `SelectedSupplierId` | int     | yes when Decision is `Approve` | must reference a supplier with a quotation on this Item    |

**Response — 302 (validation error)**:

- Redirect to `GET /Review/{id}` with `TempData["ErrorMessage"]` containing the user-facing error string. No state mutation.
- Possible error messages (post-translator):
  - "Debe ingresar un código de línea." (LineCode missing or whitespace-only after trim — per FR-012, FR-014)
  - "El código de línea no puede exceder 16 caracteres." (LineCode > 16 after trim — per FR-013)
  - "Ya existe otro ítem con el mismo código de línea en esta solicitud." (duplicate within Application — per FR-013, US2 acceptance scenario 4)

**Response — 302 (success)**:

- Redirect to `GET /Review/{id}` with `TempData["SuccessMessage"] = "Decisión del ítem registrada."`.
- `Items.LineCode` updated; `Items.ReviewStatus` updated; `VersionHistory` row appended.

**Domain rule**: the controller calls `ReviewService.ReviewItemAsync(...)` which composes `Application.AssignLineCodeToItem(itemId, lineCode)` followed by `Item.Approve / Reject / RequestMoreInfo`. Both run inside the same transaction so a duplicate-LineCode error rolls back the decision write. Rules live on the aggregate per Constitution II.

---

## Contract 3 — `GET /FundingAgreement/{applicationId}/Generate` → PDF (funder operator)

**Purpose**: render and download the branded Funding Agreement PDF. **Existing endpoint, no signature change** — only the rendered output changes.

**Request**: route `{applicationId}` + auth required (admin or assigned reviewer).

**Response — 200**: `application/pdf` byte stream of the new branded layout.

**Pre-conditions** (existing, plus one new):

- `Application.CanGenerateFundingAgreement(out errors)` returns `true`.
- **NEW pre-condition**: every Item in the approved set has `LineCode IS NOT NULL`. If any approved item lacks a line code, the controller returns `UserFacingErrorCode.LineCodeMissingOnApprovedItems` ("Falta el código de línea en uno o más ítems aprobados.") rather than rendering. This is a defence-in-depth check; the reviewer flow already guarantees this via Contract 2's "required when Decision ∈ {Approve, Reject}" rule.

**Output contract — text layer**: the rendered PDF MUST contain (verifiable via `pdftotext` or equivalent):

- `Empresa solicitante: <Application.CompanyName>` (from cover page)
- `Representante: <Applicant.LegalName>`
- `Comisión evaluadora:` followed by one line per distinct review-action taker
- Section headings: `Recursos solicitados`, `Resultados comisión`, `Información empresas proveedoras`, `DECLARO BAJO LA FE DEL JURAMENTO`
- The literal text `MARCADOR DE POSICIÓN — NO ES VERSIÓN FINAL` MUST be absent (FR-024 / SC-006).

**Output contract — visual**: matches seed within ±5pt (SC-001), header + footer on every page (FR-001 / FR-002), brand teal palette + Fraunces/Inter typography (FR-004).

---

## Contract 4 — `FundingAgreementDocumentViewModel` (Razor projection contract — internal)

**Purpose**: shape that the projection (`FundingAgreementService`) populates and Razor (`Document.cshtml`) consumes.

**New shape** (replaces the prior funder-block-driven shape):

```csharp
public sealed class FundingAgreementDocumentViewModel
{
    // Cover page (FR-005, FR-006)
    public string CompanyName { get; init; }                // Application.CompanyName
    public string ApplicantRepresentativeName { get; init; }// Applicant.LegalName
    public DateTime GeneratedAtUtc { get; init; }
    public string GenerationDateLong { get; init; }         // es-CR "8 de mayo de 2026" form
    public IReadOnlyList<string> CommissionMembers { get; init; }
        = Array.Empty<string>();                            // distinct ReviewItem actors

    // Localisation
    public string LocaleCode { get; init; } = "es-CR";
    public string CurrencyIsoCode { get; init; } = "CRC";

    // Requested resources (FR-008)
    public IReadOnlyList<RequestedResourceRow> RequestedResources { get; init; }
        = Array.Empty<RequestedResourceRow>();

    // Committee results (FR-009)
    public IReadOnlyList<ApprovedLineRow> ApprovedLines { get; init; }
        = Array.Empty<ApprovedLineRow>();
    public IReadOnlyList<RejectedLineRow> RejectedLines { get; init; }
        = Array.Empty<RejectedLineRow>();
    public string ApprovedSummaryParagraph { get; init; }   // pre-composed "Se aprueban …" sentence
    public decimal ApprovedDisbursementTotal { get; init; }

    // Supplier verification (FR-010)
    public IReadOnlyList<SupplierComplianceRow> SupplierCompliance { get; init; }
        = Array.Empty<SupplierComplianceRow>();
}

public sealed record RequestedResourceRow(
    string LineCode,                // Item.LineCode (Variable column)
    string ProductName,             // Tipo
    string CategoryName,            // Descripción
    decimal Amount,                 // Monto (CRC)
    string SelectedSupplierName,    // Empresa seleccionada
    string? CurrencyConversionNote);// e.g. "($100 × ₡520 = ₡52,000)" — spec 015

public sealed record ApprovedLineRow(
    string AcuerdoLabel,            // e.g. "FI_SBDCR25-002" or short reference
    string LineCode,                // Detalle
    string LineCodeShort,           // Variable
    string ProductName,             // Tipo
    string SelectedSupplierName,    // Empresa proveedora
    decimal Disbursement,           // Desembolso (CRC)
    string? CurrencyConversionNote);

public sealed record RejectedLineRow(
    string AcuerdoLabel,
    string LineCode,
    string LineCodeShort,
    string ProductName,
    string Motivo);                 // ItemResponse rejection reason

public sealed record SupplierComplianceRow(
    DateTime ReviewedAt,            // Fecha de revisión
    string SupplierName,            // Empresa proveedora
    string Hacienda,                // status string from Supplier
    string Ccss,
    string Sicop);
```

**Removed from previous shape** (FR-019..023):

- `FunderOptions Funder` (and `FunderOptions` type)
- `string AgreementReference` (kept on storage / file-name path; not on the document VM)
- `string ApplicantLegalId`
- `string ApplicantEmail`
- `string? ApplicantPhone`
- The previous `Items` / `TotalAmount` / `TotalsByCurrency` shape — replaced by the four typed row collections above.

**Projection rules**:

- `CommissionMembers`: `application.VersionHistory.Where(vh => vh.Action == "ReviewItem").Select(vh => vh.UserId).Distinct()` joined to `ApplicationUser` for display name. Order: alphabetical by display name.
- `ApprovedLines` / `RejectedLines`: derived from `application.Items` filtered by `ReviewStatus`; sorted by `Items.LineCode` lexicographically.
- `RequestedResources`: every `Item` in the Application (both approved + rejected), sorted by `LineCode`.
- `SupplierCompliance`: distinct `Supplier` rows referenced by `ApprovedLines`, sorted by `SupplierName`.
- `ApprovedSummaryParagraph`: composed by `FundingAgreementService` using a private formatter — `"Se aprueban las líneas {csv} por un monto total de ₡{sum}, que serán reembolsadas mediante depósito a la cuenta indicada por el solicitante."`
- `ApprovedDisbursementTotal`: sum of `ApprovedLineRow.Disbursement` (already in CRC after spec-015 conversion).
