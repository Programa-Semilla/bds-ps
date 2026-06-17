# Code Review — Funds-Usage Evidence Stage (036)

**Spec:** [spec.md](spec.md) · **Plan:** [plan.md](plan.md) · **Compliance:** 12/12 FRs (100%); SC-001–SC-007 covered by green Unit (28) / Integration (5) / E2E (4) tests.

---

## Code Review Guide (30 minutes)

This section guides a code reviewer through the implementation, focusing on high-level questions that need human judgment. Compliance scoring and the requirement checklist live in the implement console report, not here.

**Changed files:** ~16 — 1 table DDL, 3 Application files (`FileCategory`, `StorageOptions`, `EvidenceFileTypePolicy` + DTOs/interface), 1 Domain entity + 1 audit-keys edit, 2 Infrastructure files (EF config + service) + DbContext/DI/audit-writer edits, 1 controller, 2 views + 1 panel edit, 1 ViewModel, 1 Resources, plus Unit/Integration/E2E tests + POM.

### Understanding the changes (8 min)

- Start with `src/FundingPlatform.Domain/Entities/FundsUsageEvidence.cs`: the aggregate and its `CreateForExecutedApplication` factory — this is where the `AgreementExecuted` gate ([FR-001](spec.md)) and the ≤250 note invariant ([FR-006](spec.md)) live (Constitution II).
- Then `src/FundingPlatform.Web/Controllers/FundsUsageEvidenceController.cs`: the 5 actions + the `IsAccessibleAsync` / `EvidenceBelongsAsync` gates — the single auth point for [FR-002](spec.md)/[FR-009](spec.md).
- Then `src/FundingPlatform.Infrastructure/Services/FundsUsageEvidenceService.cs`: blob + row + audit orchestration (mirrors `FundService`).
- Question: the feature is a thin slice over existing seams (`IObjectStorage`, `IReviewerScope`, `UploadSizeGuard`, `IAdminAuditEventWriter`). Does the decomposition feel right, or is the service thin enough to fold into the controller?

### Key decisions that need your eyes (12 min)

**Service location avoids a namespace/type clash** (`src/FundingPlatform.Infrastructure/Services/FundsUsageEvidenceService.cs`)

The Application layer namespace `FundingPlatform.Application.FundsUsageEvidence` collides with the domain type `FundsUsageEvidence`. The service was placed in `…Infrastructure.Services` (not a `…Infrastructure.FundsUsageEvidence` folder, which is what [plan.md](plan.md) suggested) to avoid the type-vs-namespace ambiguity; the entity is aliased `EvidenceEntity` inside the service.
- Question: acceptable deviation from the plan's suggested path, or would you prefer a global alias and the planned folder?

**Applicant refusal is 403, out-of-scope reviewer is 404** (`FundsUsageEvidenceController.cs`, [FR-002](spec.md))

`[Authorize(Roles="Reviewer,Admin")]` fires before the controller body, so an applicant gets 403/AccessDenied (role refusal); an authenticated reviewer who fails the group-scope check gets `NotFound()` (404, no disclosure). Both are no-disclosure refusals and match the established `ReviewerScopeTests` convention.
- Question: is the 403-vs-404 split acceptable, given the spec says "refused with no disclosure of existence" without mandating a specific code?

**Cross-application evidence-id guard** (`EvidenceBelongsAsync`)

The note/delete/download actions take both `applicationId` (scoped) and `evidenceId`. The guard rejects a route whose `evidenceId` belongs to a *different* application than the scoped route app — closing a cross-scope hole the service alone wouldn't catch (the service loads by id only).
- Question: right place for this check (controller vs. pushing application-id into the service signatures)?

**Two-commit upload + best-effort blob cleanup** (`FundsUsageEvidenceService.UploadAsync`, [research D9](research.md))

Blob is written first, then the row is saved (to assign the id the audit payload references), then the audit row + a second `SaveChanges` — mirroring `FundService`. On a row-save failure the blob is deleted best-effort so no orphaned row exists.
- Question: is the two-`SaveChanges` pattern (row, then audit) acceptable here as it is in `FundService`, or should this be one explicit transaction?

### Areas where I'm less certain (5 min)

- `EvidenceFileTypePolicy.cs` ([FR-004](spec.md)): the magic-byte families follow [research D3](research.md). `.docx`/`.xlsx` share the zip signature (can't distinguish Word from Excel by magic) and a missing/empty declared content-type is allowed (the magic-byte check is authoritative). Is the allow-list strict enough for the client's needs?
- `FundsUsageEvidenceService.ListAsync`: uploader display name resolved via an **inner** join to `AspNetUsers`. Safe because `UploadedByUserId` is an FK to a real user, but a left join would be more defensive.
- Integration tests use `UseInMemoryDatabase` + `InMemoryObjectStorage`, matching the `FundServiceTests` precedent — **not** the literal "real DB" wording in [tasks.md](tasks.md) T017/T021/T024. Real-DB coverage comes from the E2E suite (AspireFixture). Is the InMemory-for-service + real-DB-for-E2E split acceptable here?

### Deviations and risks (5 min)

- **Service path** differs from [plan.md](plan.md)'s `Infrastructure/FundsUsageEvidence/` (moved to `Infrastructure/Services/` to avoid the namespace clash). Question: accept?
- **No separate repository** — the service uses `AppDbContext` directly, per the allowance in [contracts/interfaces.md](contracts/interfaces.md) ("may be folded into the service"). Mirrors `FundService`.
- **US2 oversize-note E2E** bypasses the `maxlength=250` attribute via JS to exercise the *server* guard (the browser would otherwise cap input at 250). Question: is exercising the server guard this way acceptable, or should it assert only the client cap?
- No other deviations from [plan.md](plan.md) were identified.
