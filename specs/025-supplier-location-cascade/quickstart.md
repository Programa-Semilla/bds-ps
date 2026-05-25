# Quickstart: Supplier Branch Location Cascade

## Run

```bash
dotnet run --project src/FundingPlatform.AppHost
```

AppHost auto-deploys the dacpac (including the new `dbo.Districts` table + `02_SeedDistricts.sql`) to the SQL container outside ephemeral mode.

## Verify — applicant new-supplier (US1, P1)

1. Sign in as `applicant@programa-semilla.test` / `Demo123!`.
2. Open a Draft application → an item → **Agregar proveedor** (`/Application/{id}/Item/{itemId}/Supplier/Add`).
3. Type a cédula jurídica that matches no supplier → the **Nuevo proveedor** panel appears.
4. In the principal-branch section:
   - **Provincia** is a dropdown. Pick *San José*.
   - **Cantón** repopulates to San José's cantones only. Pick one.
   - **Distrito** repopulates to that cantón's distritos only. Pick one.
   - Change **Provincia** → confirm Cantón **and** Distrito reset.
5. Fill the quote fields, submit. Expect success and a persisted branch whose `ProvinceId/CantonId/DistrictId` match the selections and whose legacy `Province` reads `"{Distrito}, {Cantón}, {Provincia}"`.
6. Submit with any of the three unselected → rejected inline with a localized message, no supplier created.

## Verify — applicant new branch on existing supplier (US2, P2)

1. Look up an existing **Verified** supplier by cédula.
2. **Agregar nueva sucursal** → same three-tier cascade in the new-branch panel → submit → new branch carries the location.

## Verify — admin branch edit (US3, P3)

1. Sign in as an admin (`admin@programa-semilla.test` / `Sentinel123!` in ephemeral E2E).
2. Suppliers → a supplier → **Detail** → **Editar** on a branch.
3. The Provincia/Cantón/Distrito dropdowns are pre-selected to the branch's current values (when set); change them → save → branch reflects the new location.

## Tests

```bash
dotnet test tests/FundingPlatform.Tests.Unit          # SetLocation 3-tier invariant, display-string compose
dotnet test tests/FundingPlatform.Tests.Integration   # /api/districts; seed count + FK integrity; branch persists DistrictId
dotnet test tests/FundingPlatform.Tests.E2E           # US1/US2/US3 cascade journeys
```

Delivery bar (CLAUDE.md): the full E2E suite must be personally executed and green before this is considered delivered.
