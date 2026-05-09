# Phase 1: Quickstart — PDF Template Lift

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Data model:** [data-model.md](./data-model.md) · **Contracts:** [contracts/README.md](./contracts/README.md)
**Date:** 2026-05-08

This quickstart shows how a developer verifies the feature locally end-to-end.

## 1. Boot the dev stack

```bash
dotnet run --project src/FundingPlatform.AppHost
```

The AppHost orchestrator:

- starts SQL Server in a container (or reuses the persistent dev volume),
- deploys the `FundingPlatform.Database` dacpac (which now includes `Applications.CompanyName` + `Items.LineCode` + the filtered unique index),
- starts `FundingPlatform.Web`.

## 2. Walk the applicant flow (US3)

1. Open `http://localhost:<aspire-port>/Application/Create` as an applicant user.
2. The form now has a **single required input**: "Empresa solicitante (nombre comercial)".
3. **Negative**: leave it blank, click "Crear borrador". A validation error renders inline; no row is written to `dbo.Applications`.
4. **Positive**: enter `Sazón Vegetariano`, click "Crear borrador". Redirect to `Application/Edit/{id}`. Verify in SQL: `SELECT Id, CompanyName FROM Applications WHERE Id = <id>` returns `Sazón Vegetariano`.

## 3. Walk the reviewer flow (US2)

1. As an applicant, add ≥1 item to the draft and submit. Switch to a reviewer account.
2. Open `Review/{id}`.
3. Each item card now has a **`Código de línea` text input** alongside the existing decision controls.
4. **Negative**: click "Aprobar" without filling the line code. The submit is rejected with `TempData["ErrorMessage"] = "Debe ingresar un código de línea."`. No state change in `Items`.
5. **Negative — duplicate**: assign `T1-1` to one item, then try to assign `T1-1` to a sibling item in the same Application. The second submit is rejected with the duplicate-code error.
6. **Positive**: assign distinct codes per item (e.g. `T1-1`, `T1-2`, …) and Approve/Reject per item. Verify in SQL: `SELECT Id, LineCode FROM Items WHERE ApplicationId = <id>` shows the distinct codes.

## 4. Generate the branded PDF (US1)

1. Once the Application reaches `ResponseFinalized`, open `FundingAgreement/{applicationId}/Generate` as the funder operator.
2. Download the PDF. Open side-by-side against `brainstorm/seeds/Copia de Machote FI_SBDCR25-002 Daniel Centeno Bejarano.pdf`.
3. Visual sanity check (SC-001 ±5pt):
   - **Cover**: seedling logo top centred. Title `Informe de evaluación de solicitudes de desembolso` in Fraunces ~32pt. Teal divider. `Empresa solicitante`, `Representante`, `Fecha de emisión`, `Comisión evaluadora` block. Footer composite strip at bottom.
   - **Intro**: centred subtitle, three Spanish paragraphs.
   - **Recursos solicitados**: table with `Tipo / Descripción / Variable / Monto / Empresa seleccionada`. `Variable` column shows reviewer-assigned codes.
   - **Resultados comisión**: summary sentence "Se aprueban las líneas …", bulleted rejected reasons, two subtables with the column orders specified in FR-009.
   - **Información empresas proveedoras**: table with `Fecha de revisión / Empresa proveedora / Hacienda / CCSS / SICOP`.
   - **Sworn declaration**: header repeats; `DECLARO BAJO LA FE DEL JURAMENTO`; PRIMERO/SEGUNDO/TERCERO/CUARTO/QUINTO clauses; embedded approved-lines table; closing line; rounded-rectangle signature box.
4. Text-layer assertions (SC-010 surface):
   ```bash
   pdftotext "<downloaded.pdf>" - | grep -E "(Recursos solicitados|Resultados comisión|Información empresas proveedoras|DECLARO BAJO LA FE DEL JURAMENTO)"
   ```
5. **Anti-regression**: verify the placeholder banner is absent (SC-006):
   ```bash
   pdftotext "<downloaded.pdf>" - | grep -c "MARCADOR DE POSICIÓN" # must print 0
   ```

## 5. Asset-swap ergonomics (FR-018 / SC-005)

1. Replace `src/FundingPlatform.Web/wwwroot/lib/brand/pdf/header-seedling.png` with a different image (same filename).
2. Re-run step 4 above (no rebuild needed if the file is served from disk; touch the served path if cached).
3. The header on every page of the next-generated PDF reflects the new file.
4. Repeat with `footer-partners-strip.png`.

## 6. Edge cases worth poking

- **Zero rejected items**: build a fixture where every item is approved. Generate. Confirm the "2. Líneas no aprobadas" header, the rejected-reasons bullets, and the rejected-lines table are all absent.
- **Single-reviewer commission**: `Comisión evaluadora` shows one name; no plural-language adaptation.
- **Mixed-currency items (CRC + USD)**: per-line conversion notes in spec-015 format render in the new tables.
- **Long product name or rejection reason**: row height grows; footer stays anchored.
- **Page break inside a table**: the `<thead>` band repeats on the continuation page.

## 7. Run the test suites

```bash
dotnet test tests/FundingPlatform.Tests.Unit
dotnet test tests/FundingPlatform.Tests.Integration
dotnet test tests/FundingPlatform.Tests.E2E
```

The E2E tests wired by this feature:

- `FundingAgreementPdfDownloadTests` — SC-010
- `LineCodeReviewFlowTests` — SC-011
- `CompanyNameApplicationFlowTests` — SC-012

Per project memory, the feature is **not delivered until the full E2E suite has been personally executed and is green**.

## 8. Spot-check the perf budget (SC-009)

Run the perf script (extended in this feature) against a 30-item / 10-supplier fixture:

```bash
scripts/perf/funding-agreement-pdf.sh   # or the actual script name introduced by this feature
```

Confirm p95 < 3000 ms.

---

## Diagnostic tips

- **PDF rendered without header/footer on every page**: check that `_BrandHeader` / `_BrandFooter` partials use `position: fixed; top: 0` / `bottom: 0` (R-001) and that the `@page` margins are set to 20mm/18mm.
- **Table header missing on continuation page**: confirm the table uses `<thead>` and the CSS `thead { display: table-header-group; }` rule survived the rewrite (R-003).
- **`SetCompanyName` throws at Application construction**: the Create form skipped the `CompanyName` input or the model binder lost the value — verify `CreateApplicationCommand` carries it.
- **`AssignLineCodeToItem` throws "duplicate"**: the reviewer is reusing a code already taken by a sibling item — surface the sibling's id in the error message during development.
- **dacpac deploy fails with "data loss" warning**: a developer's persistent dev volume has Applications/Items rows from before this feature. Run `EphemeralStorage=true` once or drop the dev volume.
