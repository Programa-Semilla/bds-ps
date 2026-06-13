# Quickstart: Line-Item Category Templates, Per-Item Impact, and Quotation Reuse

**Feature:** 035 | **Phase:** 1

Manual walkthrough to exercise the feature end-to-end once implemented. Run the stack via Aspire:

```bash
dotnet run --project src/FundingPlatform.AppHost
```

Seed accounts (dev): `demo-admin@programa-semilla.test` / `Demo123!` (admin), `applicant@programa-semilla.test` / `Demo123!` (applicant).

---

## 1. Admin: configure category fields (US1)

1. Sign in as admin → **Administración → Categorías** (`/Admin/Categories`).
2. **Crear categoría**: name e.g. `Equipo`; add fields via **Agregar campo**:
   - `Marca` — Texto — opcional
   - `Modelo` — Texto — requerido
   - `Costo unitario` — Decimal — requerido
   - `Garantía (meses)` — Entero — opcional
3. Reorder, edit a label, remove one field; **Guardar**.
4. Confirm the category and its fields persist (re-open Edit) and render in sort order.

## 2. Applicant: add a line item with category fields + per-item impact (US2)

1. Sign in as applicant → **Crear solicitud**, enter company name + group → draft created → **Editar**.
2. **Agregar línea** → item form:
   - Select **Categoría = Equipo** → the configured fields appear dynamically (input type per data type).
   - Fill product name + the category fields (required ones enforced).
   - Select an **impacto** (any active impact template) → its parameter fields appear → fill required values.
   - Save. (No "Especificaciones técnicas" free-text field — it's gone.)
3. Add a second line item with a different category.
4. Attempt **Revisar y enviar** with a required category field or impact value blank → submission blocked with an es-CR message naming the line + field.
5. Complete all required fields → submission allowed.

## 3. Applicant: reuse a multi-product quotation (US3)

1. On line item A → **Agregar cotización** → choose supplier (legal-id lookup) + branch, enter price/currency/validity, upload the vendor PDF. Save.
2. On line item B → **Agregar cotización** → choose **Reutilizar cotización existente** → pick A's quotation:
   - Supplier + branch + document are pre-filled (no re-upload).
   - Enter B's **own** price (different from A). Save.
3. Edit B's price → confirm A's price is unchanged.
4. Confirm the reuse picker shows **only** quotations from this application.
5. Delete line item A (the one that uploaded the PDF) → confirm B still downloads the shared document. Remove B's quotation → the document/blob is now gone (last reference).

## 4. Verify display everywhere (US4)

For an application with category values + per-item impact:
1. **Applicant Details** (`/Application/Details/{id}`) → each line shows its category field label/values + its impact.
2. **Applicant Review** (`/Applications/{publicCode}/Review`) → per-line category values + impact.
3. Submit → sign in as reviewer (`reviewer@programa-semilla.test`) → **reviewer detail** → each item shows its category values + impact.
4. Generate the **funding-agreement PDF** → each line item block includes category values + impact.
5. (AI quote-comparison) the per-item comparison context includes the product + category descriptors (impact excluded — research D6).

## 5. Teardown verification (SC-003)

```bash
# Should return ZERO results across src/ (excluding specs/ docs):
grep -rIn "TechnicalSpecifications" src/
grep -rIn "ImpactTemplateIdsCsv\|PlantillaImpactTemplates\|AttachImpactTemplate" src/
grep -rIn "Application\.\(SetImpact\|Impact\b\|ImpactTemplateId\)" src/   # app-level impact gone
```

Admin **Plantillas** create/edit pages no longer show an impact-template picker; min-quotations + required-field flags remain.

---

## Automated gates (delivery bar = filtered E2E)

```bash
dotnet test tests/FundingPlatform.Tests.Unit          # domain: Item.SetImpact/SetCategoryFieldValues/ChangeCategory, Application.Validate, CountQuotationsReferencingDocument
dotnet test tests/FundingPlatform.Tests.Integration   # category CRUD, reference-counted retention, per-item impact persistence (real DB)
# Filtered E2E for the touched classes (NOT the full ~30-min suite):
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~CategoryField|FullyQualifiedName~PerItemImpact|FullyQualifiedName~QuotationReuse|FullyQualifiedName~LineItemDisplay"
```
