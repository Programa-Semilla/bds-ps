# Research: AI-Powered Quote Comparison for Reviewers

**Spec**: `spec.md` | **Plan**: `plan.md` | **Date**: 2026-05-11

## Open Question Reconfirmation (A-1..A-8)

Each spec assumption flagged "to be reconfirmed during planning" was revisited. All eight defaults are confirmed unchanged. Detailed justifications recorded in `plan.md` under **Decisions Locked This Plan**. Summary:

| OQ | Decision | Status |
|---|---|---|
| A-1 / OQ-001 (image-only PDFs) | Refuse with `envíe un PDF con capa de texto`. No OCR in MVP. | Confirmed |
| A-2 / OQ-002 (model picks) | Extract: `claude-sonnet-4-6`. Compare: `claude-opus-4-7`. Both configurable. | Confirmed |
| A-3 / OQ-003 (spreadsheets) | Deferred; `unsupported_format` failure. | Confirmed |
| A-4 / OQ-004 (polling vs SignalR) | Polling at 3 s. No SignalR. | Confirmed |
| A-5 / OQ-005 (citation style) | Numeric superscript markers; hover ⇒ tooltip; click ⇒ signed URL in new tab; keyboard focusable. | Confirmed |
| A-6 / OQ-006 (DB-vs-file reconciliation) | Both flow to comparator with discrepancy flag; narrative surfaces it. | Confirmed |
| A-7 / OQ-007 (Forzar regeneración total UX) | Two-step (toggle Anular límites → click Forzar regeneración total). | Confirmed |
| A-8 / OQ-008 (cost dashboard) | Out of MVP; audit row carries all dashboard dimensions for future spec. | Confirmed |

## Technology Research

### Decision: Anthropic.SDK NuGet (new managed dependency)

- **Rationale**: Direct Anthropic API is the chosen provider (per brainstorm + spec). The official `Anthropic.SDK` package handles auth, streaming, retries, and JSON-mode (schema-constrained outputs) — implementing these by hand against raw HTTP would expand MVP without ROI.
- **Alternatives considered**:
  - Hand-rolled `HttpClient` wrapper: rejected — duplicates SDK work; loses upstream parity when the API evolves.
  - OpenAI-compatible bridge (e.g., LiteLLM-style multi-provider): rejected for MVP — spec scopes to a single provider; multi-provider routing is a future spec.
- **Approval**: this plan + the spec (A-10) serve as the dependency-approval record per CLAUDE.md.

### Decision: JsonSchema.Net (existing graph) for schema validation

- **Rationale**: Already pulled in transitively by spec 014 (JSON validation of storage policy configs). Reuse avoids adding a second schema lib.
- **Alternatives considered**: `Newtonsoft.Json.Schema` (license terms incompatible with our existing posture); custom validator (too much surface for MVP).

### Decision: Aspire-hosted `BackgroundService` for `ComparisonJob` worker (in-process, not separate project)

- **Rationale**: Spec NFR-P4 sets the bar at 10 items in 10 min wall-clock at concurrency 2 — comfortably handled by an in-process `BackgroundService` co-located with the Web app. Aspire AppHost can later split it out as a separate project with no spec-layer change.
- **Alternatives considered**:
  - Hangfire / Quartz.NET: rejected — heavier than the MVP needs; introduces a queue store + dashboard surface beyond requirements.
  - Separate Aspire project from day 1: rejected by YAGNI; the boundary line is the orchestrator, not the process.

### Decision: PII redactor uses regex + structured-field redaction (no NER, no LLM call)

- **Rationale**: FR-B2 enumerates 5 specific field types; all are pattern-recognizable (cédula format, phone format, email format, supplier owner DNI/phone). Deterministic regex satisfies FR-B4 ("MUST be deterministic and unit-tested"). LLM-based redaction is non-deterministic and would require a redaction-validation pass.
- **Alternatives considered**:
  - Microsoft Presidio: rejected — large dep + per-process model load; overkill for the 5-field scope.
  - Anthropic redaction call as a pre-pass: rejected — fails determinism requirement; doubles AI cost.

### Decision: `Anthropic.SDK` PDF/Vision native ingestion for supplier blobs (no separate OCR pipeline)

- **Rationale**: Claude can ingest PDFs natively. For text-layer PDFs, ingestion is fast and faithful. For image-only PDFs, MVP refuses (A-1) rather than running OCR.
- **Alternatives considered**:
  - Tesseract OCR pre-pass: rejected — adds redactor complexity (image text egress safety) and a new failure mode set; defer to a future spec.

## Integration Anchor Confirmation

Verified that named primitives in the spec map to existing code (Explore subagent results captured in this plan's structure section):

- `Quotation`, `QuotationLine`, `ExchangeRateSnapshot` → `src/FundingPlatform.Domain/Entities/Quotation.cs` (spec 015 wires the snapshot).
- `Supplier`, `SupplierBranch` → `src/FundingPlatform.Domain/Entities/Supplier.cs`, `SupplierBranch.cs` (spec 013).
- `Item` ("ApplicationItem" / "Ficha" in spec terminology) → `src/FundingPlatform.Domain/Entities/Item.cs`.
- `IObjectStorage` → `src/FundingPlatform.Application/Abstractions/Storage/IObjectStorage.cs` (spec 014).
- `AdminAuditEvent` → `src/FundingPlatform.Domain/Entities/AdminAuditEvent.cs` + `Persistence/AdminAuditEventReader.cs` (spec 016).
- Group-overlap predicate → `Persistence/Repositories/ApplicationRepository.cs` (`GetByStateForReviewerAsync`, `ApplicantSharesAnyGroupAsync`).
- `BackgroundService` exemplar → `Infrastructure/Storage/EnsureContainersHostedService.cs`.
- Review screen → `Web/Controllers/ReviewController.cs` + `Web/Views/Review/Review.cshtml`.
- dacpac → `src/FundingPlatform.Database/Tables/dbo.*.sql`.

No anchor mismatches.

## SC-012 Measurement Protocol

Captured in `plan.md` under **SC-012 Measurement Protocol**. Not implementation work — feature-lead activity 2 weeks post-deploy; output goes to `docs/measurements/sc-012-quote-comparison.md`.

## Outstanding Risks (carry into tasks.md as visibility, not blockers)

- **Anthropic API rate limits** at the org tier — currently `Tier 2` per existing observations; full-pipeline cost burst on `Generar todo` for a 50-item app would peak around 50 + 50 calls in ~25 min, well within tier limits. No mitigation needed in MVP; observability tasks instrument latency + token usage.
- **Schema bumps invalidate cache** — explicit by FR-D2 / FR-E1. Bumping `SchemaVersion` is a deliberate operator action; document in the runbook (touched in `quickstart.md`).
- **Image-only PDF refusal rate** — operational signal; track via failure-reason metric. If refusal rate is meaningful, that justifies the OCR future spec.
