# Data Model: Funds-Usage Evidence Inbox

**Feature**: 041-evidence-inbox | **Date**: 2026-06-19

## No new persisted entities

This feature adds **no tables, columns, or schema changes** (NFR-002, Constitution IV). It reads existing tables only:

- `Applications` (`State`, `GroupId`, `UpdatedAt`, `PublicCode`/`Id`, `ApplicantId`)
- `Groups` (`ProcessId`) → `Processes` (`Status`) → `Funds` (`Status`, for `ExcludeArchivedFund`)
- `Applicants` (`FirstName`, `LastName`, `UserId`)
- `UserGroupMemberships` (`UserId`, `GroupId`) — group-overlap join
- `FundsUsageEvidence` — listed/served by the existing evidence controller; untouched

## Transient read models (Application layer)

### `EvidenceInboxRowDto`

One row of the inbox. Produced by `IEvidenceInboxProjection`; never persisted.

| Field | Type | Source | Notes |
|-------|------|--------|-------|
| `ApplicationId` | `int` | `Applications.Id` | route value for the evidence link |
| `ApplicationNumber` | `string` | `"APP-{Id:D5}"` | display + row identity (`data-application-number`) |
| `ApplicantName` | `string` | `Applicants.FirstName + LastName` | trimmed; "Solicitante" fallback if empty (mirrors reviewer queue) |
| `FundName` | `string` | `Funds.Name` | row context |
| `ProcessName` | `string` | `Processes.Name` | row context |
| `ExecutedAtUtc` | `DateTimeOffset` | `Applications.UpdatedAt` (execution stamp) | ordering key (desc) |

### Query predicate (executed ∧ active process ∧ in scope)

Evaluated as a single EF query in `EvidenceInboxProjection`:

```text
Applications
  |> ExcludeDeleted
  |> ExcludeArchivedFund                       # spec 029 — archived-Fund apps drop off reviewer reads
  |> where State == AgreementExecuted
  |> join Group on GroupId
  |> join Process on Group.ProcessId
  |> where Process.Status == Active            # FR-004 — closed processes excluded, live
  |> where IsAdmin                              # admin short-circuit (FR-002)
       OR exists UserGroupMembership(m.UserId == Applicant.UserId && scope.GroupIds.Contains(m.GroupId))
  |> join Applicant, Fund, Process for display
  |> order by UpdatedAt desc
  |> take 200                                   # capped, mirrors reviewer queue (no pagination this iteration)
```

Reviewer with empty `GroupIds` and not admin → empty result (FR-002, mirrors `GetPendingInboxAsync` early return).

## Read-only derivation (Web layer)

`FundsUsageEvidenceController` computes a per-request boolean:

```text
IsProcessClosed(applicationId) :=
  Applications.Where(Id == applicationId)
              .Select(a => a.Group.Process.Status)
              .FirstOrDefault() == ProcessStatus.Closed
```

- `Index` → `FundsUsageEvidenceIndexViewModel.IsReadOnly = IsProcessClosed`.
- `Upload` / `EditNote` / `Delete` → if `IsProcessClosed`, reject (no mutation) + es-CR toast + redirect to `Index` (FR-006/FR-007).
- `Download` → unaffected (FR-006/D7).

`ProcessStatus` is an existing enum (`Active = 0`, `Closed = 1`), already EF-mapped and used by spec 029's archived-fund path — **no new enum DDL and no TINYINT `HasConversion` work introduced** (contrast spec 040's new TINYINT columns).

## State / lifecycle

No new states or transitions. The feature observes two existing, independent lifecycles:

- `Application.State == AgreementExecuted` (existing) — gates membership + evidence-page access (spec 036, unchanged).
- `Process.Status` `Active`⇄`Closed` (existing) — the switch between *listed + read-write* and *de-listed + read-only*. Reopening (`Closed → Active`, if performed) restores both automatically because the checks are live.
