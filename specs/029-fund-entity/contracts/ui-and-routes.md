# UI / Route Contracts: Fund (Fondo) Entity

**Feature**: 029-fund-entity | **Date**: 2026-06-10

This is an MVC app; "contracts" are the controller routes, their auth, inputs, and observable outcomes. All copy es-CR.

---

## Admin Fund management — `AdminFundsController` (`/Admin/Funds`)

Auth: `[Authorize(Roles = "Admin")]` + `[SupplierAdminDenied]`.

| Method | Route | Input | Outcome |
|---|---|---|---|
| GET | `/Admin/Funds` | `?status=Active\|Archived` (optional) | List rows (Name, Status badge, #Processes, HasRegulation), status filter, "Crear fondo" button |
| GET | `/Admin/Funds/Create` | — | Create form (Name, Description, optional regulation PDF) |
| POST | `/Admin/Funds/Create` | `Name`, `Description`, `RegulationFile?` (IFormFile), antiforgery, `[UploadSizeGuard(FundRegulation)]` | Validates required+unique name, PDF magic-bytes; on success stores blob (if any), persists Fund (Active), audits `fund.create`, toast "Fondo creado.", redirect to Index. On error: ModelState + es-CR toast, no partial data |
| GET | `/Admin/Funds/{id}` | — | Details: Name/Description/Status, regulation (download/replace/remove), list of Processes belonging to the Fund |
| POST | `/Admin/Funds/{id}/Edit` | `Name`, `Description` | Validate+unique; audit `fund.edit`; toast; redirect |
| POST | `/Admin/Funds/{id}/Regulation` | `RegulationFile` (IFormFile), `[UploadSizeGuard(FundRegulation)]` | Upload/replace: store new blob, `SetRegulation`, delete old blob, audit `fund.regulation.set` |
| POST | `/Admin/Funds/{id}/Regulation/Remove` | antiforgery, `data-confirm` | `RemoveRegulation`, delete blob, audit `fund.regulation.remove` |
| POST | `/Admin/Funds/{id}/Archive` | antiforgery, `data-confirm` (statelocking) | `Archive()`; audit `fund.archive`; freeze takes effect; toast |
| POST | `/Admin/Funds/{id}/Reactivate` | antiforgery, `data-confirm` | `Reactivate()`; audit `fund.reactivate`; toast |

Validation messages (es-CR), e.g.: `"El nombre del fondo es obligatorio."`, `"Ya existe un fondo con ese nombre."`, `"La descripción es obligatoria."`, `"Solo se aceptan archivos PDF."`, `"El archivo excede el tamaño máximo permitido."`.

---

## Applicant regulation download — `FundRegulationController` (or action on existing applicant surface)

| Method | Route | Auth | Outcome |
|---|---|---|---|
| GET | `/Funds/{fundId}/Regulation/Download` (or `/Applications/{id}/Regulation`) | authenticated applicant in context of a Process under the Fund | If Fund Active and regulation exists → `BackendStream` `File(stream,"application/pdf",name)`; else 404/no link. Link rendered only when applicable. |

---

## Process create/edit Fund selector — `AdminProcessesController` (existing)

- `GET/POST /Admin/Processes/Create`: `AdminProcessCreateViewModel.FundId` required; dropdown = **Active** Funds; reject save if missing/Archived (`"Debe seleccionar un fondo activo."`).
- Edit path: allow reassigning to another Active Fund (FR-009).
- `GET /Admin/Processes` (Index): new **Fund** column + `?fundId=` filter dropdown alongside the existing `ProcessStatus` filter.

---

## Application creation Group/Fund anchor — `ApplicationController.Create` (existing)

- `CreateApplicationViewModel` gains `GroupId` (required).
- View renders a **Process/convocatoria selector** listing the applicant's eligible groups (member of, under Active Process+Fund), labeled by Process name (and Group when ambiguous):
  - 0 eligible → block create, es-CR message `"No está habilitado para postular en ningún proceso activo."`.
  - 1 eligible → hidden field auto-set, no prompt.
  - ≥2 eligible → required `<select>`.
- `CreateApplicationCommand` gains `GroupId`; `ApplicationService.CreateApplicationAsync` validates membership + active-Fund and sets `Application.GroupId`.

---

## Reports Fund filter — `AdminReportsController` (existing)

- Request DTOs gain `int? FundId`; views add a Fund `<select>` (all Funds incl. Archived for admin visibility).
- Row DTOs gain `FundName`; CSV header/line add a `Fund` column on Applications / Funded Items / Aging.
- Filter clause: `a.Group.Process.FundId == req.FundId`.

---

## Freeze guard (cross-cutting, FR-020/021)

- Read: `IApplicationQueryFilter.ExcludeArchivedFund` composed at all non-admin read sites (see research D6). Observable: archived-Fund applications vanish from applicant list, reviewer queue, signing inbox, reviewer dashboard counts.
- Mutate: controller early-guard + domain `FundArchivedException` on `ApplicationController` (Create/Edit/AddItem/RemoveItem/Autosave/Submit/Remove/Impact) and `QuotationController` (Add/Edit). Observable: es-CR error toast, no state change. Admin actions exempt.
