# Feature Specification: AI-Powered Quote Comparison for Reviewers

**Feature Branch**: `020-ai-quote-comparison`
**Created**: 2026-05-11
**Status**: Draft
**Input**: User description: "Bring the AI quotation comparison reviewers do today in ChatGPT into the review screen, persisted, hash-cached, and audited. Per-item comparison ('Ficha') with hybrid input (structured DB + attached files), Anthropic Claude provider behind an `IAiClient` seam, three-stage pipeline (extract → normalize → compare), hash-keyed cache with auto-invalidation, structured output rendered as Tabler comparison table + analysis panel with source citations. Reviewer + admin trigger; per-application rate limit + per-run token cap; admins bypass with audit. PII redaction at the boundary. Hard-fail UX with manual retry. Full audit via existing `AdminAuditEvent`. Spanish (es-CR) output."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reviewer generates AI comparison for one item (Priority: P1)

A reviewer opens an application's review screen. On an item ("Ficha") that has at least two supplier quotations, they click **"Generar comparación"**. Within a minute, the screen renders a side-by-side comparison table (suppliers as columns, attribute rows like Producto / Marca / Material / Garantía / Precio total auto-derived from the quotations) plus narrative sections (Sistemas de Marca, Plazos de Respaldo, Análisis de Costos, Logística y Ubicación) — all in Spanish (es-CR), all derived from the structured quotation data plus the attached supplier files. The reviewer can act on the application without ever leaving the platform or using a third-party tool.

**Why this priority**: Headline value of the feature. Without this, every other story is moot. The current ChatGPT round-trip costs the team time on every multi-supplier item and leaves no audit trail.

**Independent Test**: Seed an application with one item that has two supplier quotations (one CRC, one USD), each with one PDF attachment. As a reviewer, click "Generar comparación" on the item card. Confirm a Tabler-styled comparison table renders with both suppliers as columns, attribute rows populated, narrative sections in Spanish, and the "Análisis de Costos" section names cheapest vs. most expensive in CRC.

**Acceptance Scenarios**:

1. **Given** an item has 2+ supplier quotations and the user holds the Reviewer role with group access, **When** they click "Generar comparación", **Then** within 60 s a structured comparison renders inline with one column per supplier, attribute rows for the offering, narrative sections, and CRC-formatted totals.
2. **Given** an item has only 1 supplier quotation, **When** the reviewer opens the item, **Then** the "Generar comparación" button does not render, replaced by the tooltip "Se necesitan al menos 2 cotizaciones para comparar."
3. **Given** an item has 2+ suppliers but no attached files, **When** the reviewer clicks "Generar comparación", **Then** the comparison still renders using structured DB data and the narrative explicitly notes that no source documents were available.
4. **Given** the reviewer is outside the application's group scope (per spec 016), **When** they open the application, **Then** the "Generar comparación" button does not render and a direct route hit returns 403.

---

### User Story 2 - Cached comparison is reused; auto-invalidated on input change (Priority: P1)

A second reviewer (or the same reviewer returning later) opens the same item. The previously generated comparison renders instantly without an AI call. If anything that fed the original comparison has changed — a new file uploaded, a quotation line edited, a supplier added/removed, or the quotation's exchange-rate snapshot updated — the cached result still renders, but with a visible "Datos desactualizados" badge that names the changed input. The button label becomes "Regenerar". A regeneration replaces the cached artifact in place; only the latest comparison is kept.

**Why this priority**: Without persistence, the AI cost balloons and the second reviewer can't trust what they see. Without auto-invalidation, reviewers act on stale comparisons. Both halves of this story are required for the feature to be usable in real review workflows.

**Independent Test**: Run User Story 1 to seed a cached comparison. Open the same item as a different reviewer; confirm the comparison renders without an AI call. Edit one quotation line's quantity; reload the item; confirm the cached comparison still renders, the "Datos desactualizados" badge names "línea editada", and the button switched to "Regenerar". Click "Regenerar"; confirm a new comparison overwrites the previous one and the badge clears.

**Acceptance Scenarios**:

1. **Given** a fresh cached comparison exists for an item, **When** any reviewer with access opens the item, **Then** the comparison renders without triggering an AI call and the page-render overhead vs. baseline is negligible.
2. **Given** a cached comparison exists and a quotation line on the item is edited, **When** the next reviewer opens the item, **Then** the cached comparison still renders, a "Datos desactualizados" badge names the changed input, and the button label switches to "Regenerar".
3. **Given** the reviewer clicks "Regenerar", **When** generation succeeds, **Then** the previously cached artifact is replaced in place (no history kept) and the badge clears.
4. **Given** the JSON output schema for comparisons is bumped (`v1` → `v2`), **When** any reviewer next opens an item with a `v1` cached artifact, **Then** the cache is treated as stale (because the schema version is part of the input hash) and a fresh generation is required.

---

### User Story 3 - Reviewer queues "Generate all" for the whole application (Priority: P2)

A reviewer needs to assess every item on a multi-item application. Instead of clicking through each item, they click **"Generar todo"** at the application level. The system enqueues a generation job for every item that is missing a comparison or whose comparison is stale (skipping items with fresh cached comparisons). The page reflects per-item status (`Pendiente` → `En progreso` → `Listo` / `Falló`) and updates automatically as each job completes. The reviewer can navigate within the application while jobs run in the background.

**Why this priority**: Convenience layer over US1. Reviewers handling applications with 10+ items would otherwise click 10+ times and wait sequentially. P2 because the per-item flow (US1) already delivers viable value; "Generate all" makes the workflow ergonomic at scale.

**Independent Test**: Seed an application with 5 items, each having 2+ supplier quotations. As a reviewer, click "Generar todo". Verify the page polls for status, each item transitions through `Pendiente` → `En progreso` → `Listo` (or `Falló`) without manual refresh, and no item is regenerated if its prior cached comparison was already fresh.

**Acceptance Scenarios**:

1. **Given** an application with 5 items, 3 of which have fresh cached comparisons, **When** the reviewer clicks "Generar todo", **Then** only the 2 missing-or-stale items are queued and the 3 fresh items remain untouched.
2. **Given** "Generar todo" is in progress, **When** the reviewer navigates within the application, **Then** the polling continues and per-item statuses update without manual refresh.
3. **Given** an admin clicks "Forzar regeneración total" (an admin-only sub-action), **When** confirmed, **Then** every item is queued regardless of cache freshness.
4. **Given** an item's job fails mid-run, **When** the reviewer returns to it, **Then** any prior cached comparison is still visible, an error reason is displayed, and a "Reintentar" button is available.

---

### User Story 4 - Admin overrides rate limit or token cap when justified (Priority: P2)

An application is at its per-app generation cap (10 in 24 h) but the reviewer needs a fresh comparison after a late supplier file landed. An admin opens the item, toggles **"Anular límites"** on the regenerate action, and the system runs the generation despite the cap. The override is recorded on the audit event with `bypassedRateLimit=true` so the team can later see who overrode what and why.

**Why this priority**: Operational lever for edge cases. Without it, hitting the cap blocks legitimate work. P2 because the headline workflow (US1, US2) functions without it, but real-world support cases need this escape hatch.

**Independent Test**: Hit the per-app rate limit by triggering 10 generations in quick succession as a reviewer. Confirm the 11th is blocked with a clear message. Sign in as admin, open the same item, toggle "Anular límites", click "Regenerar". Confirm the generation runs and the resulting audit event has `bypassedRateLimit=true`.

**Acceptance Scenarios**:

1. **Given** an application is at its 10/24h rate cap, **When** a reviewer attempts an 11th generation, **Then** the request is blocked with "Límite de generaciones alcanzado para esta solicitud (10/24h). Inténtelo más tarde o contacte un administrador." and the action is audit-logged with `success=false` and `failureReason=rate_limit_exceeded`.
2. **Given** the same conditions, **When** an admin toggles "Anular límites" and clicks "Regenerar", **Then** the generation runs to completion and the audit event records `bypassedRateLimit=true` against the admin's user ID.
3. **Given** a pre-flight token estimate exceeds the per-run cap, **When** a reviewer clicks "Generar comparación", **Then** the request is rejected before any provider call with a message naming the offending input (e.g., "El proveedor X adjuntó un PDF de 50 páginas; pida una versión recortada o ejecute como administrador para anular el límite.").

---

### User Story 5 - Source-citation links from cells and paragraphs to originating files (Priority: P3)

Every cell value and every narrative paragraph that derives from a supplier file carries a small superscript citation marker. Clicking a marker opens the originating supplier blob (a signed URL via the existing storage provider) so the reviewer can immediately verify the claim against the actual document. Cells / paragraphs with no file source carry no marker.

**Why this priority**: Trust mechanism. Reviewers need to verify AI claims against source documents to act on them confidently. P3 because the comparison itself (US1, US2) is usable without citations — but adoption stalls if reviewers can't quickly check the source.

**Independent Test**: Generate a comparison for an item with two suppliers, each with a PDF attachment. Confirm at least one cell value and one narrative paragraph carry a citation marker. Click a marker; confirm a new tab opens the originating PDF via a signed URL whose TTL respects the storage policy.

**Acceptance Scenarios**:

1. **Given** a freshly generated comparison, **When** a reviewer hovers a citation marker, **Then** a tooltip identifies the originating supplier + file name.
2. **Given** the reviewer clicks a citation marker, **When** the URL is generated, **Then** it is a signed URL via the existing storage provider with a TTL that matches the configured per-category policy (default 5 min, hard cap 15 min).
3. **Given** a cell or paragraph has no file source (e.g., derives only from structured DB data), **When** the comparison renders, **Then** no citation marker appears for that element.

---

### Edge Cases

- **Mixed-currency item** (one supplier in CRC, another in USD): each quotation's snapshot exchange rate (per spec 015) is used to convert to CRC for the comparison; the "Análisis de Costos" narrative names original currency + applied rate per supplier. New admin rates published later do NOT invalidate the cache (snapshot ID is part of the input hash).
- **Supplier with multiple quotation lines on the same item**: each line is treated as a sub-row in that supplier's column; the comparator may note variation in the narrative.
- **`Pending verification` supplier** (spec 013): compared like any other; the supplier-column header carries a "Pendiente verificación" badge mirroring existing reviewer UI.
- **Encrypted / password-protected PDF**: treated as a redaction failure; the run fails with "No se pudo procesar de forma segura el archivo de [Supplier X]. Pida una versión legible.".
- **Spreadsheet (.xlsx/.csv) attachment**: MVP converts to plain text via existing infra if available; otherwise the run fails with `unsupported_format` and a clear message. Full spreadsheet ingestion is deferred.
- **Concurrent regeneration** (two users click simultaneously): the second click is rejected with "Ya hay una generación en curso." The first completes normally.
- **Background worker crash mid-job**: orphaned `Running` jobs older than 5 min are reaped on startup and marked `Failed` with `failureReason=worker_crashed`; the next reviewer click can retry.
- **Reviewer's group access revoked mid-generation**: the in-flight request completes against the input snapshot it captured at start and the artifact persists. Subsequent page loads enforce the new authorization (button hides; direct route returns 403).
- **Item edited while a sync generation is in flight**: the in-flight run completes against its captured input snapshot; on completion the hash compare detects the drift and the artifact is persisted but immediately renders with a "Datos desactualizados" badge.
- **Application with 50+ items**: "Generar todo" enqueues 50 jobs at default concurrency 2 → roughly 25 × 60 s wall-clock. Polling continues across navigation within the application.
- **Identical files attached to two suppliers**: each supplier's column extracts the file independently; the comparator narrative may flag the file collision in "Notas".
- **Application archived / closed during a queued "Generate all" run**: the worker checks application state on dequeue; jobs against archived applications fail fast with `failureReason=application_closed`.

## Requirements *(mandatory)*

### Functional Requirements

#### A. Trigger & permissions

- **FR-A1**: A "Generar comparación" action MUST appear on every item card on the review screen for users with the `Reviewer` or `Admin` role. The action MUST respect existing group-overlap authorization (spec 016): a reviewer cannot trigger a comparison on an application outside their group scope.
- **FR-A2**: When a cached comparison exists for the item AND its inputs are unchanged, the action label MUST be "Regenerar"; the cached result renders inline above the action.
- **FR-A3**: When inputs have changed since the last generation, the cached result MUST still render inline with a "Datos desactualizados" badge identifying which input changed (file added/removed, line edited, supplier added/removed, exchange-rate snapshot moved, or schema version bumped). The action label remains "Regenerar".
- **FR-A4**: A "Generar todo" action MUST appear at the application level on the review screen. It MUST enqueue a generation job for every item that is missing a comparison or whose comparison is stale. Items with a fresh cached comparison MUST be skipped unless the user holds an `Admin` role and selects an explicit "Forzar regeneración total" sub-action.

#### B. Input assembly + PII redaction

- **FR-B1**: For each supplier quoting the item, the system MUST assemble: the `Quotation` row (with `QuotationLine` rows, currency code, snapshotted exchange rate per spec 015), the `Supplier` + selected `SupplierBranch` rows (per spec 013), and every blob attached to that quotation via spec-014 storage.
- **FR-B2**: A PII redactor MUST process the assembled payload before any bytes leave the platform. Redacted field set in MVP: applicant national ID (cédula), applicant personal phone, applicant personal email, supplier owner DNI, supplier owner personal phone. Redaction is field-level on structured data and pattern-based on file text.
- **FR-B3**: If a blob cannot be safely redacted (e.g., scanned image with no text layer), MVP default behaviour is to refuse the file with a clear "envíe un PDF con capa de texto" message. (See Assumption A-1 for the OCR alternative.)
- **FR-B4**: The redactor MUST be deterministic and unit-tested against a fixture set of representative supplier documents.

#### C. Three-stage pipeline

- **FR-C1**: Generation MUST run a three-stage pipeline `extract → normalize → compare`, exposed via a single `IComparisonOrchestrator` boundary.
- **FR-C2**: **Extract** — one AI call per supplier, executed in parallel with a configured concurrency cap (default 4). Each call returns schema-constrained JSON containing the supplier's offering for the item: product, brand, material, design/type, compatibility, technical attributes, warranty, quantity, unit price, subtotal, taxes, total, validity, issue date, freight, origin, free-form notes, and per-attribute `source_ref` linking back to the originating blob ID + page.
- **FR-C3**: **Normalize** — pure server-side step (no AI call). Aligns units (kg/lb, m/cm, unit/box), normalizes dates to `MMM DD, YYYY` (es-CR locale), converts non-CRC amounts to CRC using each quotation's snapshot rate (spec 015), and reconciles structured DB values with extracted values. Default reconciliation behaviour: pass both values to the comparator with a discrepancy flag and let the narrative surface it.
- **FR-C4**: **Compare** — single AI call over the normalized supplier-payload array. MUST return the comparison artifact JSON: `items[]` (one per item being compared, MVP = always 1), each with `header` (item label, e.g., "Ficha 3"), `suppliers[]` (column metadata), `attributeRows[]` (variable rows: `{label, cells[]: {supplierIdx, value, sourceRefs[]}}`), and `narrativeSections[]` (variable: `{title, body, sourceRefs[]}` — typically Sistemas de Marca, Mecanismo de Sujeción, Plazos de Respaldo, Análisis de Costos, Logística y Ubicación). The schema MUST be JSON-schema-validated server-side; invalid responses fail the run.
- **FR-C5**: Both AI calls MUST use prompt + schema versions stored as constants/files in the source tree. Active versions MUST be recorded on every audit event.

#### D. Cache + invalidation

- **FR-D1**: A `ComparisonArtifact` row MUST persist each successful generation. Primary key: `(ApplicationItemId)`. Stored fields include: artifact JSON, `inputHash`, `promptVersion`, `schemaVersion`, AI model identifier, `generatedAt`, `generatedByUserId`, `tokenCostInput`, `tokenCostOutput`, `latencyMs`.
- **FR-D2**: `inputHash` MUST be `sha256(canonical_json(input_descriptor))` where `input_descriptor` includes: ordered supplier IDs + branch IDs, ordered file blob hashes (already available from spec-014), structured line-item state (line IDs + amounts + currency + rate snapshot ID), and prompt + schema versions. The hash MUST be deterministic across processes.
- **FR-D3**: When a reviewer opens an item, the system MUST recompute `inputHash` from live state and compare to the persisted hash. Match → cached artifact is "fresh"; mismatch → "stale" + diff describing the changed input.
- **FR-D4**: Successful regeneration MUST replace the existing artifact in place. No history table.

#### E. Output schema + rendering

- **FR-E1**: Artifact JSON MUST conform to a versioned schema (`ComparisonArtifact.v1.json`). The schema version MUST be part of the input hash so any schema bump invalidates existing cached artifacts.
- **FR-E2**: The reviewer view MUST render the artifact as a styled comparison table (suppliers as columns, `attributeRows` as rows, item header above) followed by a stacked panel of narrative sections.
- **FR-E3**: Every cell and narrative section that carries `sourceRefs` MUST render a citation marker that opens the originating blob via the existing storage URL flow (signed URL respecting the configured TTL).
- **FR-E4**: Currency amounts MUST display in CRC formatting (`₡` prefix, es-CR thousands separator) using each quotation's snapshot rate. Original-currency amounts MUST appear in parentheses for non-CRC quotations (consistent with spec 015 display rules).
- **FR-E5**: All AI-generated copy MUST be in es-CR Spanish. Section titles, attribute labels, and narrative bodies MUST be produced in Spanish by the comparator prompt.

#### F. "Generar todo" + polling

- **FR-F1**: "Generar todo" MUST create one queued job per stale-or-missing item (or per all items when "Forzar regeneración total" is used). Jobs MUST be processed by a hosted background worker.
- **FR-F2**: The application-level review screen MUST poll a status endpoint at a configurable interval (default 3 s) while any job for the application is `Pending` or `Running`. Polling MUST stop automatically when all jobs for the application are `Completed` or `Failed`.
- **FR-F3**: Per-item status states: `None` | `Cached-Fresh` | `Cached-Stale` | `Pending` | `Running` | `Failed`. State transitions MUST be persisted; the UI MUST reflect them on every poll.
- **FR-F4**: Worker concurrency MUST be configurable (default 2 jobs in flight). Excess jobs queue.

#### G. Cost guardrails

- **FR-G1**: Per-application rate limit: max `N` generations per rolling 24-hour window (default `N = 10`, configurable). Counts successful + failed generations; cached-view reads do NOT count.
- **FR-G2**: Per-run token cap: a configured ceiling (default 200,000 input tokens) on the combined extract+compare call. Estimated pre-flight from blob sizes + structured payload size; runs that would exceed the cap MUST be rejected before any provider call with a reviewer-facing message naming the offending input.
- **FR-G3**: An `Admin` role MAY bypass FR-G1 and FR-G2 on a per-click basis via an explicit "Anular límites" toggle on the action. Each override MUST be recorded on the audit event.

#### H. Audit

- **FR-H1**: Every generation attempt (success or failure) MUST emit an `AdminAuditEvent` (existing infra from spec 016) with: `actorUserId`, `actorRole`, `applicationId`, `applicationItemId`, ordered `supplierIds[]`, `inputHash`, `promptVersion`, `schemaVersion`, AI model identifier, `tokenCostInput`, `tokenCostOutput`, `latencyMs`, `success`, `failureReason` (when applicable), `bypassedRateLimit` (bool), `bypassedTokenCap` (bool).
- **FR-H2**: Raw prompts and raw model responses MUST NOT be persisted. Only hashes + structured metadata.
- **FR-H3**: Audit rows MUST carry the data needed for a future cost-rollup dashboard (by application, by program, by reviewer, by time window) to be built without schema changes elsewhere.

#### I. Failure handling

- **FR-I1**: Provider transient errors (HTTP 5xx, network timeout, provider 429) MUST surface "Generación falló: el proveedor de IA no respondió. Reintentar." with a Retry button. No automatic retry. Audit `failureReason=provider_transient`.
- **FR-I2**: Provider hard errors (4xx other than 429, invalid API key, model deprecated) MUST surface "Generación falló. Contacte un administrador." Detailed reason MUST be logged + audited as `failureReason=provider_hard:{code}`. Retry button visible.
- **FR-I3**: Schema-validation failures MUST fail the run, MUST NOT persist or mutate the cached artifact, and MUST surface "La respuesta de IA no fue válida. Reintentar." Audit `failureReason=schema_invalid` with the validator's first error path.
- **FR-I4**: All failure modes MUST leave any prior cached artifact intact and visible.

### Non-Functional Requirements

#### Performance
- **NFR-P1**: Per-item synchronous generation MUST complete within 60 s for the typical case (2-5 suppliers, ≤10 MB total attached blobs). Server hard timeout 90 s.
- **NFR-P2**: Reviewer page load with a cached fresh artifact MUST add ≤100 ms to current review-screen render time.
- **NFR-P3**: Hash recomputation per item on page load MUST complete in ≤50 ms for typical inputs.
- **NFR-P4**: "Generar todo" on an application with 10 stale items MUST process them within 10 minutes wall-clock at default worker concurrency.

#### Security
- **NFR-S1**: PII redaction (FR-B2) is the security boundary for AI egress. The redactor MUST be the only path that constructs outbound provider request bodies; direct provider client calls outside the orchestrator MUST be forbidden by code structure.
- **NFR-S2**: AI provider API keys MUST be supplied via the configured secret store (never `appsettings.json` in source). Logs MUST NOT contain the API key.
- **NFR-S3**: Signed-URL TTL for citation links MUST honor the per-category policy in spec 014 (default 5 min, hard cap 15 min).
- **NFR-S4**: All AI-generated copy MUST be treated as untrusted; rendering MUST sanitize against XSS (no raw HTML escape hatches in comparison views).
- **NFR-S5**: File content sent to the comparator MUST be wrapped in clearly delimited blocks; the system prompt MUST instruct the model to ignore in-document instructions to alter behavior (prompt-injection mitigation).

#### Accessibility
- **NFR-A1**: The comparison table MUST meet WCAG 2.1 AA: proper header scoping, color is not the sole indicator of "stale" or "cheapest" badges, and citation markers are keyboard-focusable.
- **NFR-A2**: Long narrative blocks MUST collapse beyond a screen-height threshold with a keyboard-accessible "Mostrar más" toggle.

#### Locale
- **NFR-L1**: All AI-generated copy and surrounding UI chrome MUST render in es-CR, consistent with spec 012. Currency formatting follows spec 015 conventions.

#### Observability
- **NFR-O1**: Each pipeline stage (`extract`, `normalize`, `compare`) MUST emit a structured log entry with stage name, item ID, supplier IDs, latency, token usage (where applicable), and outcome.
- **NFR-O2**: A failed generation MUST log the full failure reason at `Warning` level without including raw prompts or PII.
- **NFR-O3**: Token usage and latency MUST flow through the same telemetry channel as the audit event.

#### Maintainability
- **NFR-M1**: A single `IAiClient` abstraction MUST be the only seam over the AI provider. Adding a second provider in a future spec MUST require zero changes outside the provider-implementation folder + DI registration.
- **NFR-M2**: Prompt and JSON-schema files MUST be checked into the source tree; version constants are bumped with the file.

### Key Entities

- **ComparisonArtifact**: One row per `ApplicationItem` that has ever been compared. Stores the structured comparison JSON, the input hash that produced it, the prompt + schema versions, the AI model identifier, the user who triggered it, generation timestamp, token costs (input/output), and latency. Replaced in place on regeneration; no history table.
- **ComparisonJob**: A queued or in-flight generation request. Carries `applicationItemId`, requesting user, status (`Pending` | `Running` | `Completed` | `Failed`), `bypassedRateLimit` / `bypassedTokenCap` flags, last-status-change timestamp, and (on completion) a reference to the resulting `ComparisonArtifact` or a failure reason. Reaped if `Running` for more than 5 min without progress.
- **AdminAuditEvent (existing, spec 016)**: Reused unchanged. Every generation attempt emits one event.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reviewer with at least one item containing 2+ supplier quotations can click "Generar comparación" and see a structured comparison table + narrative analysis rendered inline within 60 s, end-to-end (no manual file download, no third-party tool).
- **SC-002**: A second reviewer opening the same item later sees the cached comparison instantly (≤100 ms over baseline page load) without triggering an AI call.
- **SC-003**: When any input changes (file added/removed, line edited, supplier added/removed, currency snapshot moved, schema bumped), the cached comparison renders with a visible "Datos desactualizados" badge that names the changed input, and the action label switches to "Regenerar".
- **SC-004**: Every cell value and every narrative paragraph that derives from a supplier file carries a clickable citation marker that opens the originating file via the existing storage URL flow.
- **SC-005**: AI output is in es-CR Spanish for 100% of generations across a fixture suite (zero English leakage in attribute labels, narrative bodies, or section titles).
- **SC-006**: PII fields enumerated in FR-B2 do NOT appear in any captured outbound request payload across a fixture-based test sweep of representative supplier documents.
- **SC-007**: Per-application rate limit blocks the 11th generation in a 24-hour window with a clear message; the same user as `Admin` with the override toggle succeeds; both events appear in the audit log with the correct `bypassedRateLimit` flags.
- **SC-008**: "Generar todo" on an application with N stale items completes (or surfaces per-item failures) within `(N / 2) × 60 s` wall-clock at default worker concurrency, and the page polls and updates per-item status without manual refresh.
- **SC-009**: A failed generation leaves any prior cached artifact intact and visible, surfaces a clear error message + "Reintentar" button, and emits an audit event with `success=false` and a populated `failureReason`.
- **SC-010**: Adding a second AI provider in a hypothetical future spec touches only files inside the provider-implementation folder + DI registration (verified by code-review checklist; no production-code change required outside those paths to keep MVP behaviour green).
- **SC-011**: An ad-hoc SQL query against the audit table can roll up token cost by application, by program, by reviewer, and by time window using only audit-row columns (no joins to deleted artifact data required).
- **SC-012**: Reviewer task time on a multi-supplier item drops by at least 70 % compared to the current ChatGPT round-trip baseline (measured on a representative sample of 5 items by 3 reviewers before/after).

## Assumptions

- **A-1 (OQ-001)**: For image-only PDFs that cannot be safely redacted, MVP default is to refuse the file with a clear "envíe un PDF con capa de texto" message rather than introducing an OCR-then-redact pre-pass. To be reconfirmed during planning.
- **A-2 (OQ-002)**: Default model picks are Sonnet 4.6 for extraction and Opus 4.7 for comparison; both are configurable. Final selection (and whether Sonnet 4.6 alone suffices for both stages) is settled during planning after a token-cost estimate against a sample application.
- **A-3 (OQ-003)**: Spreadsheet (.xlsx/.csv) ingestion is deferred. MVP converts to plain text via existing infrastructure if available; otherwise the run fails with `unsupported_format`. To be reconfirmed during planning.
- **A-4 (OQ-004)**: Polling (default 3 s) is the MVP mechanism for "Generar todo" status. SignalR is not introduced in MVP; it can be swapped in later as a local change to the polling endpoint and a small front-end change.
- **A-5 (OQ-005)**: Citation marker style mimics the source-image convention (numeric superscripts). Final visual + interaction (hover preview vs. click-through) is a design pass during planning.
- **A-6 (OQ-006)**: When the normalizer detects a discrepancy between structured DB values and extracted file values, both values are passed to the comparator and the narrative surfaces the discrepancy. Neither value silently wins.
- **A-7 (OQ-007)**: The admin "Forzar regeneración total" sub-action is a separate explicit click after enabling the "Anular límites" toggle (two-step). Single-click composite action is rejected because it makes accidental over-regeneration too easy.
- **A-8 (OQ-008)**: The token-cost dashboard is out of MVP scope, but FR-H3 ensures the audit row carries program, reviewer, application, item, and timestamp dimensions so the dashboard is buildable later without schema changes.
- **A-9**: The project is pre-production. Schema changes (`dbo.ComparisonArtifacts`, `dbo.ComparisonJobs`, audit row column additions) are made directly to the dacpac with no migration ceremony, and any cached artifacts may be invalidated by schema bumps.
- **A-10**: The `Anthropic.SDK` NuGet package is a new managed dependency; this spec is the approval vehicle per `CLAUDE.md`.
- **A-11**: Group-overlap authorization (spec 016) is the sole authorization mechanism for triggering and viewing comparisons; no separate ACL is introduced.
- **A-12**: The supplier-quotation file storage category in spec 014 (`Storage:Categories:supplier-quotations`) already enforces upload-side size limits; this feature does not add another upload-side cap.
