# Contract: Sidebar before → after (US8, zero removals)

File: `Views/Shared/_Layout.cshtml` (data block + render loop). `Models/SidebarEntry.cs` record unchanged. Reuse existing section-header markup/CSS (`nav-item-section-header`, `fl-sidebar-section-header`, `nav-item-admin-child`, `data-section-testid`).

`SidebarEntry(Slug, Label, Url, Icon, AllowedRoles[], ShowToUnauthenticated=false)`.

## Mapping (all 18 current entries placed exactly once; AllowedRoles verbatim)

| Slug | Label (es-CR) | Url | AllowedRoles | New group |
|---|---|---|---|---|
| home | Inicio | `/` | [] | **Inicio** (top, no header) |
| my-applications | Mis solicitudes | `/Application` | [Applicant] | **Inicio** (top) |
| review-queue | Cola de revisión | `/Review` | [Reviewer,Admin] | **Inicio** (top) |
| generate-agreement | Generar convenio | `/Review/GenerateAgreement` | [Reviewer,Admin] | **Inicio** (top) |
| signing-inbox | Bandeja de firmas | `/Review/SigningInbox` | [Reviewer,Admin] | **Inicio** (top) |
| — | **Administración** | `/Admin` | [Admin] | section header (exists) |
| suppliers | Empresas proveedoras | `/Admin/Suppliers` | [Admin] | **Administración** |
| plantillas | Plantillas base | `/Admin/Plantillas` | [Admin] | **Administración** |
| reports | Reportes | `/Admin/Reports` | [Admin] | **Administración** |
| currencies | Monedas | `/Admin/Currencies` | [Admin] | **Administración** |
| exchange-rates | Tipos de Cambio | `/Admin/ExchangeRates` | [Admin] | **Administración** |
| users | Usuarios | `/Admin/Users` | [Admin] | **Administración** |
| system-config | Configuración del sistema | `/Admin/Configuration` | [Admin] | **Administración** |
| — | **Proceso** | `/Admin/Processes` | [Admin] | section header (NEW, `data-section-testid="proceso-section"`) |
| processes | Procesos | `/Admin/Processes` | [Admin] | **Proceso** (or the header itself links here) |
| groups | Grupos | `/Admin/Groups` | [Admin] | **Proceso** |
| starters | Starters | (see open decision) | [Admin] | **Proceso** (NEW entry) |
| impact-templates | Plantillas de impacto | `/Admin/ImpactTemplates` | [Admin] | **Proceso** |
| legacy-quotations | Cotizaciones pendientes | `/Admin/LegacyQuotations` | [Admin] | **Proceso** |
| supplier-admin-suppliers | Empresas proveedoras | `/Admin/Suppliers` | [SupplierAdmin,Admin] | **supplier-admin-only variant** (unchanged) |

Top-level visibility logic (`IsEntryVisible`, Applicant-suppression-for-staff, supplier-admin-only branch) preserved unchanged.

## Open decisions (flagged for the user — see research D7)

1. **Starters route.** No standalone applications-list controller. MVP recommendation: nav-reachable route rendering the existing applications listing (`Views/Admin/Reports/Applications.cshtml`) filtered by Process — via a thin action or a deep link to the Reports Applications tab with `processId`. Confirm preferred form in plan review.

2. **Process-scoped Reportes/Plantillas duplication.** The stakeholder confirmed Reportes + Plantillas should appear under both Administración and Proceso (admin-wide vs process-scoped). **No process-scoped routes exist.** This contract places each existing surface once (no net-new surfaces, per YAGNI). Two options for the user:
   - (a) **Defer** true process-scoped Reportes/Plantillas to a future spec (recommended) — Proceso shows Starters / Grupos / Plantillas de impacto / Cotizaciones pendientes.
   - (b) **Build now** process-scoped report/template surfaces (expands this spec; new controllers/queries). Out of the current lean scope.
