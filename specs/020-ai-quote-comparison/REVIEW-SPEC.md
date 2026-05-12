# Spec Review: AI-Powered Quote Comparison for Reviewers

**Spec:** specs/020-ai-quote-comparison/spec.md
**Date:** 2026-05-11
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** Spec is implementable as written. All mandatory sections complete, requirements testable, success criteria measurable, edge cases enumerated. Constitution alignment verified. The 8 brainstorm open questions were resolved with explicit defaults under **Assumptions** (A-1..A-8) and flagged for reconfirmation during `/speckit-plan` — this is correct discipline, not residual ambiguity. No critical or important fixes needed before planning.

## Completeness: 5/5

### Structure
- Purpose / problem framing: present (top of file + User Story 1)
- User Scenarios & Testing: 5 prioritized stories (P1×2, P2×2, P3×1) with independent-test descriptions and Given/When/Then acceptance scenarios
- Edge Cases: 12 enumerated scenarios under the User Scenarios section
- Requirements (Functional A–I, Non-Functional Performance/Security/Accessibility/Locale/Observability/Maintainability): all populated
- Key Entities: present (`ComparisonArtifact`, `ComparisonJob`, `AdminAuditEvent` reuse)
- Success Criteria: 12 measurable outcomes (SC-001..012)
- Assumptions: 12 items (A-1..A-12) covering deferred OQs + integration constraints
- No `[NEEDS CLARIFICATION]`, no TBD, no placeholder text

### Coverage
- Functional surface: trigger/permissions, input assembly + redaction, three-stage pipeline, cache, output schema/rendering, generate-all + polling, cost guardrails, audit, failure handling — all enumerated
- Non-functional surface: performance, security, accessibility, locale, observability, maintainability — each with concrete thresholds or behaviors
- Error / failure modes: 4 explicit failure FRs (FR-I1..I4) plus per-error edge cases

**Issues:** None.

## Clarity: 5/5

### Language Quality
- Verbs use RFC-2119 style (`MUST`, `MAY`); no soft "should" left in normative sections
- Numeric thresholds explicit: 60 s sync timeout, 90 s hard timeout, ≤100 ms cached overhead, ≤50 ms hash recompute, default rate limit 10/24h, 200,000-token cap, default polling 3 s, default extract concurrency 4, default worker concurrency 2, default reap-after 5 min, signed-URL TTL 5 min default + 15 min hard cap
- All Spanish UI strings quoted verbatim (e.g., "Generar comparación", "Datos desactualizados", "Anular límites", "Forzar regeneración total") — no es-CR ambiguity
- "Configurable" appears with defaults always stated alongside, so plan-time tunability is preserved without leaving any value undefined for MVP

**Ambiguities Found:** None blocking. Two intentional, low-risk uses of "MAY" (FR-G3 admin override, FR-C3 normalizer reconciliation) where the choice is explicitly captured in Assumptions.

## Implementability: 5/5

### Plan Generation
- Dependencies identified: 7 internal specs (001/002/003/012/013/014/015/016) named by number with the specific primitive each contributes (`Quotation`, `Supplier`, `IObjectStorage`, exchange-rate snapshot, group-overlap predicate, `AdminAuditEvent`)
- External runtime: Anthropic Claude API + Anthropic.SDK NuGet (flagged as new managed dep per CLAUDE.md, with this spec as the approval vehicle — A-10), Aspire-hosted background worker, EF Core 10
- Configuration knobs: a complete table of new keys + defaults, ready to drop into `AppHost.cs` registration
- Schema additions: `dbo.ComparisonArtifacts`, `dbo.ComparisonJobs` + composite indexes (pre-prod note: edited directly into the dacpac, no migration ceremony)
- Scope is bounded: per-item primary unit, single AI provider, no streaming, no cross-application comparison, no history table, no localization beyond es-CR — all explicit out-of-scope items defer to future specs

**Issues:** None.

### Notes on integration-anchor naming
The spec names internal primitives directly (`IComparisonOrchestrator`, `IAiClient`, `IPiiRedactor`, `ComparisonArtifact`, `ComparisonJob`). Strictly these are implementation hints. **Per project convention (visible in specs 011-019)**, integration anchors back into named existing system primitives are spec-level — they identify *what to integrate with* and *what new primitives become integration points for future specs*, not *how to implement them*. The planner is free to rename internals; the boundary names matter because spec 020+ may reference them.

## Testability: 5/5

### Verification
- Every Success Criterion is verifiable: 8 are objectively measurable (latency, count, hash-diff banner content, audit-row contents), 3 are behavioural and testable via E2E + fixture sweep (PII fixture, English-leakage fixture, citation marker presence), 1 is process-verifiable via code-review checklist (SC-010 future-provider isolation)
- SC-012 introduces a baseline-comparison metric (≥70% task-time reduction); this requires before/after measurement design. Plan should call out the measurement protocol; not blocking for spec acceptance
- Each user story carries an independent-test description (constitution Principle V compliance)
- Each acceptance scenario is in Given/When/Then form (constitution Principle III alignment for E2E test generation)

**Issues:** SC-012 needs a measurement protocol defined in plan.md. Flagged as Optional below.

## Constitution Alignment

Constitution v1.0.0 reviewed against spec content.

| Principle | Status | Notes |
|---|---|---|
| I. Clean Architecture | ✅ | New primitives placed in correct layers: `IComparisonOrchestrator` + DTOs in Application; `IAiClient` impl in Infrastructure; `ComparisonArtifact` / `ComparisonJob` entities in Domain; review-screen surfacing in Web. Spec respects inward-pointing dependency rule. |
| II. Rich Domain Model | ⚠ Plan-time | Spec doesn't define entity behaviour methods (e.g., `ComparisonArtifact.MarkStale()`, `ComparisonJob.Reap()`). Acceptable at spec stage; **plan MUST define them** to satisfy "behavior on the entity, not in services". |
| III. E2E Testing (NON-NEGOTIABLE) | ✅ | Each user story has independent-test description; acceptance scenarios are Given/When/Then. Spec doesn't mention test stack (correct — that's plan.md). |
| IV. Schema-First Database | ✅ | Spec explicitly states dacpac edits are direct (Assumption A-9, dependencies section). No EF migrations referenced. |
| V. Specification-Driven Development | ✅ | Spec follows the 5-section structure; user stories independently testable; out-of-scope items explicit. |
| VI. Simplicity & Progressive Complexity | ✅ | YAGNI applied: streaming, multi-provider, embeddings, cost-rollup dashboard, history table, SignalR — all explicitly deferred. The single abstraction added (`IAiClient`) has a stated current need (NFR-M1) and a planned-not-speculative second-provider use case. Defaults provided for every configuration knob. |
| Tech Standards Table | ⚠ Stack drift | Constitution lists ".NET 8+ (latest LTS)" and "Local file system (initial)". CLAUDE.md (current) supersedes with .NET 10.0 + Azure Blob Storage / Azurite (spec 014). Spec 020 inherits the current stack via spec 014; **constitution doc is stale, not the spec**. Out of scope for this review; flag separately. |

**Violations:** None for this spec. (Constitution-document staleness re tech versions is pre-existing, not introduced by spec 020.)

## Recommendations

### Critical (Must Fix Before Implementation)
None.

### Important (Should Fix)
None.

### Optional (Nice to Have)
- [ ] **SC-012 measurement protocol**: define in `plan.md` how the 70% task-time reduction is measured (sample selection, timing methodology, who runs it).
- [ ] **Domain behaviour methods**: when planning, define entity methods on `ComparisonArtifact` and `ComparisonJob` (e.g., `IsStaleAgainst(InputDescriptor)`, `Reap()`, `RecordSuccess(...)`, `RecordFailure(...)`) to satisfy Constitution Principle II.
- [ ] **OQ resolution at planning**: A-1..A-8 each carry a "reconfirm during plan" flag. The `/speckit-plan` step should explicitly revisit each, not just inherit the assumption.

## Conclusion

Spec is sound and ready for `/speckit-plan`. All mandatory sections complete, every requirement testable, every success criterion measurable, every edge case has a defined behaviour, and constitution alignment is clean (with one Plan-stage carry-over for domain behaviour methods).

**Ready for implementation:** Yes — after `/speckit-plan` and `/speckit-tasks`.

**Next steps:**
1. User reviews this report and the spec.
2. If approved, run `/speckit-plan` (planner should reconfirm A-1..A-8 and define domain behaviour methods per Constitution II).
3. After plan, `/speckit-tasks`, then `/speckit-implement`.
