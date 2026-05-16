# EVOLVE NOTE: FR-001 Group↔Process binding — UI drift

**Date**: 2026-05-15
**Spec**: 021-feedback-session-may13 / US1 / FR-001
**Trigger**: Review of US1 (Annual program cycle administration) — admin cannot
create, see, or change a Group's owning Process from any UI surface.
**Resolution**: Option B — Fix Code. Spec is correct and authoritative.

---

## Mismatch 1: Group admin UI has no Process axis (MAJOR)

**Spec requires:**
- **FR-001**: "Every existing `Group` MUST belong to exactly one `Process`."
- **US1 Acceptance #1**: admin "creates Groups *Norte*, *Sur*, *Centro* **under
  the Process**."
- **US1 Independent Test**: admin completes "the full create-Process →
  assign-Plantilla → **create-Groups** → assign-reviewers flow **without
  leaving *Administración***."

**Code does:**

| Layer | State | Verdict |
|---|---|---|
| `Group` entity | `ProcessId` non-null FK, `Create(name, processId)`, `MoveToProcess()` | ✅ correct |
| `dbo.Groups.ProcessId` | `INT NOT NULL FK → Processes.Id` (T012) | ✅ correct |
| `IGroupService.CreateAsync` | no `processId` parameter; no `MoveToProcess` method | ❌ |
| `GroupService.CreateAsync` | **force-assigns every new Group to the hardcoded `"Migración inicial"` Process** | ❌ |
| `AdminGroupCreateViewModel` / `AdminGroupEditViewModel` | only `Name` — no Process field | ❌ |
| `Groups/Create.cshtml`, `Edit.cshtml` | only a Name input — zero Process selector | ❌ |
| `GroupService.ListAsync` → `GroupRow` | Process not projected; Groups index never shows the owning Process | ❌ |
| `Processes/Details.cshtml` Groups panel | read-only list; "Administrar grupos" links to the flat, Process-less `/Admin/Groups` | ❌ |

`GroupService.CreateAsync` documents the gap in its own comment: *"the spec-016
admin Groups form was not extended with a Process picker; new ad-hoc groups land
under the bootstrap 'Migración inicial' Process... A dedicated Process selector
is tracked as spec-021 follow-up work."*

**Type**: Behavioral + UX. **Severity**: Major — user-facing, breaks US1
Acceptance #1. Data integrity is not at risk (the FK is physically 1:1); the
defect is that the binding is invisible and uncontrollable from the admin seat,
so Groups *read* as floating free / reusable across Processes.

**Originating cause — tasks.md decomposition gap**: the US1 task block
(T077–T084) created the Process + Plantilla surfaces (`AdminProcessesController`
T080, `Processes/*` views T083) and the cascading Process→Group filter on
`/Admin/Users` (T082), but contains **no task to extend the Group admin surface
with the Process axis**. T031 added only the domain field. The code faithfully
matches `tasks.md`; `tasks.md` does not match `spec.md`.

## Mismatch 2: US1 E2E weakened to tolerate Mismatch 1 (MAJOR — test integrity)

**Spec requires** (T075): E2E "driving full create-Process → assign-Plantilla →
**create-Groups** → assign-reviewers".

**Code does**: `US1_ProcessAdmin.cs` step 5 deep-links `GET /Admin/Groups/Create`
with the comment *"until a per-Process Group create surface lands in a later
spec sweep"*. Step 7's cascade check is `optionsAll.Count >= optionsAfter.Count`
— trivially true even when the cascade narrows nothing. The "116→0 E2E green"
state therefore does **not** evidence FR-001 compliance. Violates NFR-004's
intent and the project rule that E2E must drive the real user journey.

---

## Resolution: Option B — Fix Code

Spec is correct; code (and the `tasks.md` decomposition behind it) is
incomplete. No `spec.md` change → no `/speckit-plan` or `/speckit-tasks`
regeneration required. `tasks.md` remains out of sync by one omitted task; this
note is the authoritative record of the work that closes the gap.

**Chosen create UX**: group creation moves **into Process Details** — the owning
Process is implied by the page context (`/Admin/Processes/{id}`), which is the
literal reading of FR-001 / US1 ("Groups *under* the Process").

### Execution plan

1. `IGroupService` — `CreateAsync` gains a `processId` parameter; add
   `MoveToProcessAsync(int groupId, int newProcessId, string actorUserId, ct)`.
2. `GroupService` — implement both; **delete the `"Migración inicial"`
   hardcode**; write `AdminAuditEvent`s (Process id in the create payload; new
   `GroupMoveProcess` action kind for reparenting).
3. `AdminProcessesController` — add `POST /Admin/Processes/{id}/Groups` (create a
   Group in this Process).
4. `Processes/Details.cshtml` — replace the read-only Groups panel header link
   with an inline "Nuevo grupo" create form (Process implied by route `{id}`).
5. `AdminGroupsController` / `Groups/Edit.cshtml` — Edit gains a Process
   `<select>` driving `MoveToProcessAsync` (reparenting). The standalone
   Process-less `Groups/Create` surface is retired.
6. `GroupRow` + `GroupService.ListAsync` + `Groups/Index.cshtml` — project and
   display the owning Process as a column.
7. `US1_ProcessAdmin.cs` — step 5 rewritten to create the three Groups via
   Process Details; step 7 cascade assertion strengthened to require the Group
   dropdown to actually narrow to the picked Process's Groups.

### Known deviation

`tasks.md` US1 block is missing the Group-admin-Process-axis task. Tracked here;
to be reflected on the next `/speckit-tasks` regeneration.

---

## Resolution executed (2026-05-16)

Implemented per the plan above (commit `3600959`). Build green; unit 26/26,
integration 34/34, the six reworked admin E2E classes 18/18.

### Mismatch 3 (discovered during E2E): shared-fixture contamination — MAJOR

The first full E2E run after the fix surfaced **43 failures**, all one root
cause:

- `SubmitApplicationHandler.ResolveMinimumQuotationsAsync` resolves an
  applicant's submit-time minimum quotations by walking
  `UserGroupMemberships → Groups → ProcessPlantillas` and taking
  `FirstOrDefault()`.
- The E2E helper `RegisterUserAsync → AssignAllGroups` assigns **every**
  registered user to **every** Group — which violates **FR-002** ("Applicants
  belong to exactly one Group").
- Before this fix, `US1_ProcessAdmin`'s Groups landed in the Plantilla-less
  "Migración inicial" Process, so `ResolveMinimumQuotationsAsync` found no
  snapshot and fell back to the default (2).
- After the fix (correctly, per FR-001) `US1`'s Groups sit under its
  Plantilla-bearing `Crocus 2025` Process (`MinimumQuotationsPerItem = 3`).
  Every later-registered applicant, force-joined to those Groups, now resolved
  to that snapshot → every downstream submit/review/appeal test that supplies
  2 quotations failed with *"must have at least 3 quotation(s)"*.

**Verdict**: not a product defect. In production **FR-002 holds** — an applicant
is in exactly one Group, so the resolution is deterministic. The defect is E2E
**test isolation**: `US1_ProcessAdmin` leaked a Plantilla-bearing
Process-with-Groups into the shared `AspireFixture`.

**Resolution** (commit on top of `3600959`): `US1_ProcessAdmin` deletes the
three Groups it creates in a `[TearDown]`, keeping the shared fixture neutral.

**Follow-up seam (out of scope here)**: `RegisterUserAsync → AssignAllGroups`
applied to applicants is itself an FR-002 violation; and
`ResolveMinimumQuotationsAsync` / `ResolveStageClosesAtAsync` rely on
`FirstOrDefault` over the applicant's memberships. Both are correct only while
the single-Group invariant holds. A future spec could (a) make the test helper
assign applicants to one Group, and (b) resolve the Plantilla from the
Application's own Process rather than the applicant's memberships.

### Final verification

Full Playwright E2E suite personally executed: **224 passed / 0 failed /
5 skipped** — identical to the pre-drift `88ca991` baseline. NFR-004 / SC-016
satisfied.
