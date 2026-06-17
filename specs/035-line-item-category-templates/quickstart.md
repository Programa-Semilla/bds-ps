# Quickstart: Line-Item Category Templates, Application-Level Impacts with Per-Item Attribution, and Quotation Reuse

**Feature:** 035 | **Phase:** 1 | **Evolved:** 2026-06-16

Manual walkthrough to exercise the feature end-to-end once implemented. Run the stack via Aspire:

```bash
dotnet run --project src/FundingPlatform.AppHost
```

Seed accounts (dev): `demo-admin@programa-semilla.test` / `Demo123!` (admin), `applicant@programa-semilla.test` / `Demo123!` (applicant).

---

## 1. Admin: configure category fields (US1 — unchanged)

1. Sign in as admin → **Administración → Categorías** (`/Admin/Categories`).
2. **Crear categoría**: name e.g. `Equipo`; add fields via **Agregar campo**:
   - `Marca` — Texto — opcional
   - `Modelo` — Texto — requerido
   - `Costo unitario` — Decimal — requerido
   - `Garantía (meses)` — Entero — opcional
3. Reorder, edit a label, remove one field; **Guardar**.
4. Confirm the category and its fields persist (re-open Edit) and render in sort order.

## 2. Applicant: declare the application's impacts (US2 — app level)

1. Sign in as applicant → **Crear solicitud**, enter company name + group → draft created → **Editar**.
2. Open the **Impactos** step (`/Application/{id}/Impacts`).
3. **Agregar impacto** → select an **active impact template** → its parameter fields appear → fill required values → save.
4. Add a **second** impact (a different template) with its own values → both appear as distinct sections, each labeled by impact name.
5. Remove one impact → confirm it (and any line attributions to it) disappears.
6. Attempt **Revisar y enviar** with **zero** declared impacts, or a declared impact missing a required value → submission blocked with an es-CR message.
7. (Edge) If no active impact templates exist, the step shows the es-CR empty-state ("no hay plantillas de impacto activas").

## 3. Applicant: add a line item with category fields + impact attribution + justification (US3)

1. Back on the draft → **Agregar línea** → item form:
   - Select **Categoría = Equipo** → the configured fields appear dynamically (input type per data type).
   - Fill product name + the category fields (required ones enforced). (No "Especificaciones técnicas" free-text field — it's gone.)
   - **Impactos relacionados**: a multi-select listing **only the impacts declared for this application** → attribute the line to one or more.
   - **Justificación de impacto**: a short textarea (max 300 chars, with a live counter) → write why the item supports the selected impact(s).
   - Save.
2. Add a second line item with a different category and its own attribution + justification.
3. Attempt **Revisar y enviar** with: a required category field blank, **zero** impact attributions, or an **empty** justification → submission blocked with an es-CR message naming the line + missing element.
4. (Edge) If the application declares no impacts yet, the item form shows an empty-state linking to the Impactos step (you cannot attribute to nothing).
5. Complete everything → submission allowed.

## 4. Applicant: reuse a multi-product quotation (US4 — unchanged)

1. On line item A → **Agregar cotización** → choose supplier (legal-id lookup) + branch, enter price/currency/validity, upload the vendor PDF. Save.
2. On line item B → **Agregar cotización** → choose **Reutilizar cotización existente** → pick A's quotation:
   - Supplier + branch + document are pre-filled (no re-upload).
   - Enter B's **own** price (different from A). Save.
3. Edit B's price → confirm A's price is unchanged.
4. Confirm the reuse picker shows **only** quotations from this application.
5. Delete line item A (the one that uploaded the PDF) → confirm B still downloads the shared document. Remove B's quotation → the document/blob is now gone (last reference).

## 5. Verify display everywhere (US5)

For an application that declares impacts and has populated line items:
1. **Applicant Details** (`/Application/Details/{id}`) → an application-level **Impactos** card (declared impacts + values) + each line shows its category field label/values, attributed impact name(s), and justification.
2. **Applicant Review** (`/Applications/{publicCode}/Review`) → same: app impacts card + per-line category values + attribution + justification.
3. Submit → sign in as reviewer (`reviewer@programa-semilla.test`) → **reviewer detail** → app impacts card + each item's category values + attributed impacts + justification.
4. Generate the **funding-agreement PDF** → an app-level impacts block + each line item block includes category values + attributed impacts + justification.
5. (AI quote-comparison) the per-item comparison context includes the product + category descriptors + the justification (all PII-scrubbed); raw impact parameter values are excluded (research D16).

## 6. Teardown verification (SC-003)

```bash
# Should return ZERO results across src/ (excluding specs/ docs):
grep -rIn "TechnicalSpecifications" src/
grep -rIn "ImpactTemplateIdsCsv\|PlantillaImpactTemplates\|AttachImpactTemplate" src/
# Per-item impact (the superseded prior-035 design) must be gone:
grep -rIn "Item\.ImpactTemplateId\|Items\.ImpactTemplateId" src/
grep -rInE "Item\.(SetImpact\b|ImpactParameterValues)" src/
```

Impact field data now lives on `ApplicationImpact` (application level); items carry only `ItemImpact` attributions + `ImpactJustification`. Admin **Plantillas** create/edit pages no longer show an impact-template picker; min-quotations + required-field flags remain.

---

## Automated gates (delivery bar = filtered E2E)

```bash
dotnet test tests/FundingPlatform.Tests.Unit          # domain: Application.AddImpact/RemoveImpact, Item.AttributeImpacts/SetImpactJustification(≤300), Application.Validate gates
dotnet test tests/FundingPlatform.Tests.Integration   # app-level multi-impact round-trip, attribution round-trip, RemoveImpact strips attributions, re-keyed ImpactParameterValues, category CRUD, reference-counted retention (real DB)
# Filtered E2E for the touched classes (NOT the full ~30-min suite):
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~ApplicationImpacts|FullyQualifiedName~ItemImpactAttribution|FullyQualifiedName~ImpactDisplay|FullyQualifiedName~CategoryField|FullyQualifiedName~QuotationReuse"
```
