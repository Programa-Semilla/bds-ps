# Quickstart: Admin — Edit Process Name

## What this feature adds
An inline **Name** edit on the Process detail page (`/Admin/Processes/{id}`) — the one Process
detail that was previously immutable. Rename works for Active and Closed Processes, is validated
and unique, and is audited.

## Touch points (one per layer)

| Layer | File | Change |
|---|---|---|
| Domain | `Process.cs` | none expected — `Rename()` already exists (do **not** add a Closed guard) |
| Domain | `AdminAuditEvent.cs` | `+ ProcessRenamed` → `"process.renamed"` |
| Application | `Processes/IProcessService.cs` | `+ RenameAsync(...)` + `RenameProcessCommand` record |
| Infrastructure | `Services/ProcessService.cs` | `+ RenameAsync` impl (mirror `ReassignFundAsync`, no-op skips audit) |
| Web | `Controllers/Admin/AdminProcessesController.cs` | `+ [HttpPost("{id:int}/Rename")] Rename` (mirror `ChangeFund`) |
| Web | `Views/Admin/Processes/Details.cshtml` | `+` inline Name card (mirror Fund card; renders for any status) |

## Manual verification (after implementation)

1. Run the app: `dotnet run --project src/FundingPlatform.AppHost`.
2. Sign in as admin (`admin@programa-semilla.test` / `Sentinel123!` in ephemeral, else the seeded admin).
3. Go to `/Admin/Processes`, open any Process.
4. Edit the **Name** card → save a new name → expect the success toast
   "Nombre del proceso actualizado." and the new name on the header, breadcrumb, and the
   `/Admin/Processes` list.
5. Try to rename it to another Process's name → expect inline "Ya existe un proceso con ese
   nombre."; name unchanged.
6. Clear the name → save → expect inline validation error; name unchanged.
7. Re-save the same name → no error, no change (and no new audit row).
8. Close a Process, then rename it → expect success (rename allowed when Closed).
9. Check the admin audit log → a `process.renamed` entry with old/new name and your user.

## Tests to run (delivery bar: full E2E green)

```
dotnet test tests/FundingPlatform.Tests.Unit
dotnet test tests/FundingPlatform.Tests.Integration
dotnet test tests/FundingPlatform.Tests.E2E
```

## Done when
All `spec.md` success criteria (SC-001…SC-005) pass and the full Playwright E2E suite is green.
