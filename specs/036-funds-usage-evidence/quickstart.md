# Quickstart: Validating the Funds-Usage Evidence Stage

## Prerequisites

- Run the app via Aspire: `dotnet run --project src/FundingPlatform.AppHost`
- An application in **`AgreementExecuted`** state (drive one through the signing ceremony:
  generate agreement → upload signed PDF → reviewer approves), in a group the test reviewer is assigned to.
- Sign in as a reviewer assigned to that application's group (or as `admin@…`).

## Manual walkthrough (maps to user stories)

1. **US1 — Collect evidence**: Open the application's reviewer surface → click **"Evidencia de uso de fondos"**.
   Upload a PDF, then an image. Both appear in the list with file name, uploader, timestamp, and a download link.
   Click a download link → original file is served back.
2. **US2 — Annotate**: On an item, add a note (try 250 chars — accepted; try 251 — rejected with es-CR message).
   Edit the note text → the displayed note updates without re-uploading.
3. **US3 — Delete**: Delete an item → confirm dialog appears → confirm → item disappears and its download 404s.
   Cancel on another item → nothing changes.
4. **US4 — Scoped access**:
   - As a reviewer **not** in the application's group → the evidence URLs return 404.
   - As the applicant → 404 (role gate).
   - On an application **not** yet `AgreementExecuted` → the stage link is absent and the URLs 404.
5. **Negative**: Upload a `.txt` or `.zip` → rejected with es-CR message, no item created. Upload a > 20 MiB file →
   rejected (413 → error toast), no item created.

## Automated tests

```bash
# Unit (domain gate + note invariant + EvidenceFileTypePolicy)
dotnet test tests/FundingPlatform.Tests.Unit --filter "FullyQualifiedName~FundsUsageEvidence"

# Integration (real DB: upload/list/edit/delete, group-scope, AgreementExecuted gate, audit rows)
dotnet test tests/FundingPlatform.Tests.Integration --filter "FullyQualifiedName~FundsUsageEvidence"

# E2E (filtered — the delivery gate per CLAUDE.md): the four user stories
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~FundsUsageEvidence"
```

## Done-when (delivery bar)

- Filtered E2E green for `FundsUsageEvidenceTests` (all four stories), plus any signing-ceremony helper/dev-seam
  added to reach `AgreementExecuted` (research D8).
- Unit + integration green for the new classes.
- An audit row exists for each upload / note-edit / delete (assert in integration).
