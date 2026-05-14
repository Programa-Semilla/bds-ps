# Quickstart: AI-Powered Quote Comparison

**Spec**: `spec.md` | **Plan**: `plan.md` | **Date**: 2026-05-11

## Local development setup

### 1. Anthropic API key (via user-secrets)

```bash
dotnet user-secrets --project src/FundingPlatform.AppHost \
    set "AiComparison:Anthropic:ApiKey" "sk-ant-..."
```

The AppHost reads the key and wires it to the web app via `WithEnvironment("AiComparison__Anthropic__ApiKey", ...)`. Never commit the key to `appsettings.json`.

### 2. Optional config overrides

Override any default by setting an env var (or user-secret) with the matching key. See the **Configuration Knobs** section of `plan.md` for the full table. The most common dev overrides:

| Override | Why |
|---|---|
| `AiComparison:ExtractConcurrency=1` | Slow down extract for easier log reading. |
| `AiComparison:SyncHardTimeoutSeconds=180` | More headroom while iterating on prompts. |
| `AiComparison:PromptVersion=dev-YYYY-MM-DD` | Force cache invalidation while testing prompt changes. |
| `AiComparison:RateLimitPerApp24h=999` | Defuse rate limit during a debug session. |

### 3. Run the stack

```bash
dotnet run --project src/FundingPlatform.AppHost
```

Aspire boots SQL Server + Azurite (existing) + Web. The `AddSqlProject` step auto-deploys the dacpac, which now creates `dbo.ComparisonArtifacts` and `dbo.ComparisonJobs`.

## Seed an application with two suppliers

The existing dev seeder creates a sample application. To exercise the comparison flow specifically:

1. Sign in as the sentinel admin (`admin@FundingPlatform.com`).
2. Go to **Admin → Suppliers** and create two `Verified` suppliers (or use seeded ones).
3. Create an application (or use seeded one). On one of its items, add **two** supplier quotations (different suppliers), each with at least one PDF attachment.
4. Submit the application so it lands in the reviewer queue.
5. Sign out, sign in as a reviewer (`reviewer@FundingPlatform.com` if seeded, otherwise create one via Admin).

## Verify US1 — generate per-item comparison

1. Open the application from the reviewer queue.
2. On the multi-supplier item card, click **Generar comparación**.
3. Within 60 s, the comparison region renders below the item card:
   - Tabler-styled side-by-side comparison table (suppliers as columns).
   - Narrative sections (Sistemas de Marca, Plazos de Respaldo, Análisis de Costos, Logística y Ubicación) — all in Spanish.
   - CRC-formatted totals; non-CRC suppliers show original currency in parentheses.
4. Click any citation marker (numeric superscript): a new tab opens the originating PDF via a signed URL (TTL respects spec 014 policy).

## Verify US2 — cache + stale detection

1. Reload the page → the comparison renders instantly with no AI call (check the logs).
2. Edit a `QuotationLine` quantity on the item. Reload the review page.
3. The cached comparison still renders; a **Datos desactualizados** badge names `línea editada`; the action label is now **Regenerar**.
4. Click **Regenerar** → a new generation overwrites the cached artifact; the badge clears.

## Verify US4 — admin bypass

1. As reviewer, run 10 generations in quick succession on the same application's items.
2. On the 11th, the action returns a clear "Límite de generaciones alcanzado…" message.
3. Sign in as admin, open the same item, toggle **Anular límites**, click **Regenerar**.
4. The generation runs; check `dbo.AdminAuditEvents.PayloadJson` for `bypassedRateLimit: true`.

## Offline development (no Anthropic key)

Swap to the stub `IAiClient` via DI registration:

```bash
dotnet user-secrets --project src/FundingPlatform.AppHost \
    set "AiComparison:Provider" "Stub"
```

The stub returns deterministic canned schema-valid responses from `tests/Fixtures/AiComparison/`. Used by E2E tests by default; useful for UI iteration without burning tokens.

## Running tests

```bash
# Unit (PII redactor, hash, normalizer, guards, domain behavior)
dotnet test tests/FundingPlatform.Tests.Unit

# Integration (orchestrator + worker + reaper, real DB, stubbed IAiClient)
dotnet test tests/FundingPlatform.Tests.Integration

# E2E (Playwright + AspireFixture, stubbed IAiClient, all 5 user stories)
dotnet test tests/FundingPlatform.Tests.E2E
```

E2E runs against `--EphemeralStorage=true` per existing fixture conventions.

## Operator notes (post-deploy)

- **Bumping the schema** (`AiComparison:SchemaVersion=v2`) invalidates every cached artifact in place. Plan rollouts during low-traffic windows; queued "Generar todo" jobs will refresh the cache for active applications. There is no migration ceremony — Assumption A-9.
- **Bumping prompts** (`AiComparison:PromptVersion=...`) similarly invalidates the cache. Bumps go in lock-step with edits to `prompts/extract.v1.md` and `prompts/compare.v1.md`.
- **Anthropic API outage**: every generation surfaces `provider_transient` / `provider_hard` cleanly with reviewer-facing retry. Reviewers retain access to prior cached artifacts (FR-I4). No automatic retry.
- **Worker crash** (process restart, OOM): the reaper marks `Running` jobs older than 5 min as `Failed` with `worker_crashed`. Reviewer can click **Reintentar**.
