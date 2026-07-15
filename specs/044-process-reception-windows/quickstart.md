# Quickstart: Fund Process Reception Windows (044)

## Run

```bash
dotnet run --project src/FundingPlatform.AppHost
```

The AppHost auto-deploys the dacpac (new `dbo.ProcessEvents` table + `SolicitudWindowDays` drop) in run mode.

## Manual walkthrough

### Admin — configure windows
1. Sign in as admin (`admin@programa-semilla.test` / `Sentinel123!` in ephemeral E2E).
2. Go to `/Admin/Processes/{id}` → **Ventanas de recepción** card.
3. Add a window: name `Convocatoria 1`, start/end as CR local datetimes (`datetime-local`). Verify the state badge (Próxima / Abierta / Cerrada).
4. Add a second non-contiguous window; deactivate one; try `end ≤ start` → es-CR validation error, nothing saved.

### Applicant — gating + notice
1. Sign in as applicant (`applicant@programa-semilla.test` / `Demo123!`).
2. `/Application/Create` for a group whose process has a window:
   - **Inside a window** → notice shows "Recepción abierta · cierra el dd/MM/yyyy HH:mm" with countdown; submit a complete application → succeeds.
   - **Before/between windows** → notice shows next open instant + "puede preparar un borrador"; submit → 422 with es-CR reason; new-draft creation still allowed.
   - **All windows closed** → notice shows closed; **new-draft creation blocked** with es-CR reason; an existing draft still opens and is editable but cannot submit.
3. Process with **no windows** → behaves exactly as before (open).

## Test commands

```bash
# Domain evaluation + entity invariants
dotnet test tests/FundingPlatform.Tests.Unit --filter "FullyQualifiedName~ReceptionWindow"

# Submission/autosave gating + admin CRUD against real SQL
dotnet test tests/FundingPlatform.Tests.Integration --filter "FullyQualifiedName~ReceptionWindow|FullyQualifiedName~SubmitGuard|FullyQualifiedName~Autosave"

# Filtered E2E (delivery gate)
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~ReceptionWindow"
# Regression: existing submission still green with no windows configured
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~Submit|FullyQualifiedName~ApplicantCompanySelection"
```

## Acceptance checkpoints (map to spec SCs)

- **SC-001**: two non-contiguous windows open/close independently at the configured CR instants.
- **SC-002** (unit/integration, faked clock): `now == StartUtc` → allowed; `now == EndUtc` → blocked.
- **SC-003/SC-004**: applicant notice + disabled-submit reason correct outside; countdown + successful submit inside.
- **SC-005**: no-window process submission E2E unchanged (run the regression filter above).
- **SC-006**: all-closed process blocks new-draft creation, existing-draft editing still works.
- **SC-007**: all dates render in CR/es-CR `dd/MM/yyyy HH:mm`; boundary agreement.

## Gotchas (from research)

- `ProcessEventType` TINYINT **must** map `HasConversion<byte>()` or real-SQL materialization throws `Byte→Int32` (InMemory hides it; E2E catches it).
- Removing the Solicitud duration gate must also strip the throw in **`AutosaveFieldHandler`** — otherwise draft editing stays time-gated, violating FR-015.
- Gating is pure UTC instant comparison; `IBusinessTimeZone` is used only for admin input parsing and display formatting.
- E2E seeds windows **relative to real `UtcNow`** (no clock freeze); the exact-boundary case is unit/integration only.
