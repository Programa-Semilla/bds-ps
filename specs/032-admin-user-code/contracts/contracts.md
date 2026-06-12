# Phase 1 Contracts: Admin-only user provisioning + unique applicant User Code

**Feature**: 032-admin-user-code

This is a server-rendered ASP.NET MVC app; the "contracts" are HTTP route behaviors, the admin form field contract, and the search-matching contract. Each is stated so an E2E test can assert it.

---

## C1. Route contract — registration removed

| Route | Method | Before | After |
|-------|--------|--------|-------|
| `/Account/Register` | GET | 200 + register form | **404** |
| `/Account/Register` | POST | creates user → 302 Login | **404**, no account created |
| `/Account/Login` | GET | 200 (with "Crea una aquí" link) | 200, **no register link** |
| `/Account/Login` | POST | unchanged | unchanged |
| `/Account/ForgotPassword`, reset, `/Account/Profile` | — | unchanged | unchanged |
| `/` (Home) | GET | hero CTA → `/Account/Register` | hero CTA → `/Account/Login` |

**Assertions**: `GET`/`POST /Account/Register` → 404; no element with `asp-action="Register"` / href `/Account/Register` renders on `/` or `/Account/Login`; `data-testid="public-landing-cta-button"` resolves to the Login URL.

---

## C2. Admin user create/edit — User Code field contract

Endpoints unchanged: `POST /Admin/Users/Create`, `POST /Admin/Users/{id}/Edit` (auth: `Admin`; `[SupplierAdminDenied]`).

**Request gains** (form field bound to VM): `UserCode` (string, optional in the model, conditionally required by role).

**Behavior matrix**:

| Role selected | `UserCode` submitted | Result |
|---------------|----------------------|--------|
| Solicitante | non-blank, ≤50, unique | 302 success; `Applicant.UserCode` persisted |
| Solicitante | blank / whitespace | 200 re-render, ModelState error on `UserCode`: "El código de usuario es obligatorio para el rol Solicitante." |
| Solicitante | duplicate of another applicant | 200 re-render, error: "El código de usuario ya está en uso." |
| Solicitante | > 50 chars | 200 re-render, length validation error |
| Revisor / Administrador / Administrador de proveedores | (field hidden) | 302 success; no `UserCode` involved; no Applicant row |

**UI contract**: the User Code field block (`data-testid="admin-user-usercode"` recommended, wrapping a labelled input) is visible **iff** the role selector value is `Applicant`, toggled by the same client JS that already shows/hides the LegalId field. On Edit, the field is prefilled from `UserDetailDto.UserCode`.

---

## C3. Search-matching contract (widened surfaces)

For each surface, given an applicant A with distinct values `name`, `email`, `legalId`, `userCode`, a search term equal to (a full or partial substring of) any one of those four returns A; an unrelated term excludes A. Empty term → unchanged paged list. Matching is case-insensitive (existing collation behavior).

| Surface | Endpoint | Matches AFTER this feature |
|---------|----------|----------------------------|
| Admin users list | `GET /Admin/Users?search=` | Email, FirstName, LastName, **LegalId**, **UserCode** |
| Reviewer queue | `GET /Review?search=` and `GET /Review/QueueRows?search=` | FirstName, LastName, LegalId, **UserCode**, **Email** |
| Report — Applications | `GET /Admin/Reports/Applications?search=` | FullName, LegalId, Email, **UserCode** |
| Report — Applicants | `GET /Admin/Reports/Applicants?search=` | FullName, LegalId, Email, **UserCode** |
| Report — Aging | `GET /Admin/Reports/AgingApplications?search=` | FullName, LegalId, Email, **UserCode** |
| Applicants CSV export | `GET /Admin/Reports/Applicants/Export?search=` | (same predicate as Applicants report) + **UserCode column in output** |

**Column-surfacing contract (D6/FR-016)**:
- Admin users list renders a "Código de usuario" cell (`—` when null).
- Applicants report table + CSV include a "Código de usuario" column/header.
- Reviewer queue and Applications/Aging reports: **match-only**, no new column.

---

## C4. Profile contract

`GET /Account/Profile` for an applicant renders a **read-only** "Código de usuario" field (disabled + `administrado` badge, mirroring `Código personal`), value = `Applicant.UserCode` or `—`. Non-applicants: field absent (no Applicant row). The applicant cannot submit a change to it (not bound on profile POST).
