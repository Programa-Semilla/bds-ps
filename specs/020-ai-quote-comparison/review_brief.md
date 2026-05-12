# Review Brief: AI-Powered Quote Comparison for Reviewers

**Spec:** specs/020-ai-quote-comparison/spec.md
**Generated:** 2026-05-11

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Reviewers comparing supplier quotations on an item today must download attached files, paste them into ChatGPT with a hand-curated prompt, and read the comparison there before returning to the platform to act. This feature brings the AI comparison **into the review screen**: one click on an item ("Ficha") generates a structured side-by-side supplier comparison + Spanish narrative analysis, persisted and hash-keyed to its inputs so freshness is unambiguous and reusable across reviewers. PII is redacted before bytes leave the platform; every generation is audit-logged via the existing `AdminAuditEvent` infrastructure (spec 016). The first version uses Anthropic Claude behind an `IAiClient` seam so future provider swaps don't require touching call sites.

## Scope Boundaries

- **In scope:** per-item comparison + app-level "Generar todo" convenience; three-stage pipeline (extract → normalize → compare); hybrid input (structured DB + attached files); hash-keyed cache with auto-invalidation; structured Tabler table + narrative output with source citations; PII redaction; reviewer + admin trigger with admin override of rate / token caps; full audit; es-CR Spanish output.
- **Out of scope:** multi-provider routing, streaming responses, embeddings / vector search, cross-application comparison, cost-rollup admin dashboard, editable AI output, auto-generation on submission, SignalR push, history of past generations, locales beyond es-CR.
- **Why these boundaries:** MVP delivers the headline value (in-platform AI comparison with audit + cache + citations) on a single AI provider through a seam that lets later specs add providers, dashboards, and streaming without re-architecting. Pre-production status lets the team iterate on schema + prompts without migration ceremony.

## Critical Decisions

### Three-stage pipeline (extract → normalize → compare), not single-shot
- **Choice:** Per-supplier structured extraction first, server-side normalization (units / dates / currency), then a single comparator call over the normalized array.
- **Trade-off:** Higher token cost and more code than a single-shot prompt, but materially lower hallucination risk, reusable extraction artefacts, and a clean place to enforce the JSON schema.
- **Feedback:** Is the extra cost acceptable for the lower hallucination floor? Should extraction be downgraded to a single batched call?

### Hybrid input (structured DB rows + attached files), not files-only
- **Choice:** Send Claude both the structured `Quotation` / `QuotationLine` rows AND the attached supplier blobs.
- **Trade-off:** Higher token usage than DB-only; bigger blast radius if a file leaks PII (mitigated by FR-B2 redactor). Anchors hallucinations against authoritative DB values.
- **Feedback:** Worth the extra tokens? Or trust the files and have the comparator note discrepancies post-hoc?

### Hash-keyed cache with auto-invalidation, latest-only (no history)
- **Choice:** One persisted artefact per `ApplicationItemId`, replaced in place on regen. Hash includes supplier IDs, blob hashes, line state, currency snapshot ID, prompt version, schema version.
- **Trade-off:** No audit trail of *what AI ever showed reviewers*. Loses ability to "see what the comparison said three weeks ago".
- **Feedback:** Is "latest only" acceptable, or do we need a history table for compliance / dispute resolution?

### Per-application rate limit + per-run token cap, with admin bypass
- **Choice:** 10 generations / 24h / application; 200,000-token pre-flight cap; admin "Anular límites" toggle bypasses both with audit.
- **Trade-off:** Reviewers occasionally hit the cap and must wait or escalate; admins must judge when to bypass.
- **Feedback:** Are the defaults right? Should non-admin reviewers be able to bypass *with* a documented reason field?

### Sync per-item + background queue for "Generar todo", with polling (not SignalR)
- **Choice:** Single click = sync HTTP request with 60 s typical / 90 s hard timeout. App-level "Generar todo" enqueues per-item jobs; UI polls a status endpoint at 3 s intervals.
- **Trade-off:** Sync requests can feel long for items with many large PDFs; polling is less elegant than push but ships faster.
- **Feedback:** Is sync acceptable up to 90 s, or push everything to background? Polling acceptable for MVP, or invest in SignalR now?

## Areas of Potential Disagreement

### "Latest only" cache (no history table)
- **Decision:** No history of past generations.
- **Why this might be controversial:** Compliance / dispute scenarios may need "what did the AI say when reviewer X approved this on date Y?"
- **Alternative view:** Keep an append-only history table with the same artifact JSON; expose a "Ver versiones anteriores" link to admins only.
- **Seeking input on:** Is there a current or anticipated compliance requirement to retain past AI outputs?

### Anthropic-only in MVP, abstraction seam without multi-provider
- **Decision:** Single provider in MVP; `IAiClient` seam exists but no routing, fallback, or A/B logic.
- **Why this might be controversial:** Some teams prefer to ship multi-provider on day 1 to avoid vendor lock-in or to comply with data-residency requirements upfront.
- **Alternative view:** Add an `OpenAi` / `AzureOpenAi` implementation now and route by config; defer multi-provider routing logic but keep two implementations live.
- **Seeking input on:** Any near-term need for a non-Anthropic provider (data residency, cost, customer requirement)?

### PII redaction list scope (5 fields)
- **Decision:** Cédula, applicant phone, applicant email, supplier owner DNI, supplier owner phone.
- **Why this might be controversial:** Real procurement docs may carry other sensitive data (banking info, fiscal IDs of third parties, CCSS account numbers) that the list doesn't cover.
- **Alternative view:** Adopt a deny-by-default classification (allow-list of fields rather than deny-list) for the structured payload, plus a broader regex/NER pass on file text.
- **Seeking input on:** Should the redactor be expanded before MVP ships? If so, what fields specifically?

### Image-only PDFs are refused (not OCR-redacted)
- **Decision (A-1):** MVP refuses files without a text layer with a clear "envíe un PDF con capa de texto" message.
- **Why this might be controversial:** Pushes work back to suppliers, which may slow some applications.
- **Alternative view:** Add an OCR-then-redact pre-pass (e.g., Tesseract via existing infra) so image-only PDFs work on first contact.
- **Seeking input on:** How frequent are image-only PDF uploads in practice?

### Spanish-only output (no English fallback, no other locales)
- **Decision:** All AI copy in es-CR per spec 012.
- **Why this might be controversial:** Locks out future English-speaking reviewer/admin users without a re-spec.
- **Alternative view:** Ship the comparator prompt with a `locale` parameter from day 1 so multi-locale is one config flip later.
- **Seeking input on:** Any planned non-Spanish reviewer access in the next 6 months?

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Reviewer-facing primary action | "Generar comparación" | Item card on review screen (FR-A1) |
| Reviewer-facing regen action | "Regenerar" | Replaces "Generar comparación" once a cached artifact exists (FR-A2) |
| Reviewer-facing app-level action | "Generar todo" | Application-level review screen (FR-A4) |
| Admin sub-action of Generar todo | "Forzar regeneración total" | Admin-only (FR-A4, A-7) |
| Stale-cache badge | "Datos desactualizados" | Names the changed input (FR-A3) |
| Admin override toggle | "Anular límites" | Per-click toggle on the action (FR-G3) |
| Concurrent-regen rejection | "Ya hay una generación en curso." | Verbatim Spanish (Edge Cases) |
| Persisted artefact entity | `ComparisonArtifact` | Keyed by `ApplicationItemId` (FR-D1) |
| Background-job entity | `ComparisonJob` | Per-item lifecycle (FR-F1) |
| Pipeline orchestrator | `IComparisonOrchestrator` | Application-layer boundary (FR-C1) |
| AI provider seam | `IAiClient` | Infrastructure-layer abstraction (NFR-M1) |
| PII redactor | `IPiiRedactor` | Boundary before AI egress (FR-B2) |
| Output schema | `ComparisonArtifact.v1.json` | Versioned; bump invalidates cache (FR-E1) |

## Open Questions

- [ ] **A-1 / OQ-001**: Refuse image-only PDFs vs. OCR-then-redact pre-pass — confirm during plan.
- [ ] **A-2 / OQ-002**: Final model picks (Sonnet 4.6 + Opus 4.7 default; reconsider after token-cost estimate).
- [ ] **A-3 / OQ-003**: Spreadsheet ingestion in MVP yes/no.
- [ ] **A-4 / OQ-004**: Polling vs. SignalR for "Generar todo".
- [ ] **A-5 / OQ-005**: Citation marker visual + interaction style.
- [ ] **A-6 / OQ-006**: DB-vs-file discrepancy reconciliation — flag both vs. one wins.
- [ ] **A-7 / OQ-007**: Force-regenerate-all UX (single composite vs. two-step).
- [ ] **A-8 / OQ-008**: Token-cost dashboard aggregation dimensions.
- [ ] **SC-012 measurement protocol**: how the 70% task-time reduction is measured (sample selection, who runs it, baseline definition).
- [ ] **History table for compliance**: do we need a per-generation audit trail beyond the latest artifact?
- [ ] **Redaction list completeness**: what other sensitive fields (banking info, CCSS, fiscal IDs of third parties) should the deny-list cover?

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| AI hallucination on commercially significant fields (price, warranty, validity) leading reviewers to act on wrong values | High | Three-stage pipeline grounds extraction in structured DB rows; normalizer reconciles DB ↔ file; comparator surfaces discrepancies in narrative; citation markers let reviewer verify against source. |
| PII leakage to AI provider via supplier files | High | `IPiiRedactor` is the **only** path that constructs outbound request bodies (NFR-S1); deterministic + fixture-tested (FR-B4); image-only PDFs that can't be safely redacted are refused (A-1). |
| Token cost runaway from reviewers regenerating repeatedly | Medium | Per-app rate limit (10/24h default) + per-run token cap (200k input default); cached views are free; admin bypass is per-click + audit-logged. |
| Long sync requests timing out for items with many / large suppliers | Medium | Server hard timeout 90 s; pre-flight token-cap rejection; "Generar todo" pushes work to background worker; concurrent-regen rejected at DB-level. |
| Prompt-injection via supplier file content altering comparator behaviour | Medium | NFR-S5: file content wrapped in delimited blocks; system prompt instructs the model to ignore in-document instructions. Output is JSON-schema-validated server-side (FR-C4). |
| Provider availability / 4xx-5xx during reviewer workflow | Medium | Hard-fail UX with manual retry; cached artifact stays visible; failure reason audit-logged with structured `failureReason` codes. |
| `IAiClient` seam fails to actually isolate the provider when a second one is added later | Medium | SC-010 makes provider-isolation a verification target (code-review checklist); NFR-M1 is normative. |
| New `Anthropic.SDK` NuGet dep introduces transitive supply-chain risk | Low | Standard package; well-known maintainer; reviewed during plan; CLAUDE.md spec-approval gate (this spec is the approval). |

---

*Share with reviewers before implementation.*
