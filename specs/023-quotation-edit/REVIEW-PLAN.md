# Review Guide: In-place Quotation Field Edit

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-20

---

## What This Spec Does

When an applicant has attached a supplier quotation to a draft application and later spots a typo (wrong price, wrong validity date, wrong branch) — or a reviewer returns the application asking for that exact fix — there is currently no way to correct the row without deleting the quotation and re-uploading the PDF. That deletion loses `CreatedAt`, breaks audit continuity, and orphans any reviewer comment that referenced it. This spec adds a per-row *Editar* affordance on `Application/Edit` (and the underlying GET/POST endpoints on `QuotationController`) so the owner can change `Price`, `Currency`, `ValidUntil`, or `SupplierBranch` (same supplier only) in place.

**In scope:** in-place edit of those four fields while the application is `Draft` or `ReturnedForChanges`; reuse of the Supplier/Add input block via a new shared `_QuoteFields.cshtml` partial; silent invalidation of the spec 020 `ComparisonArtifact` cache for the affected Item.

**Out of scope:** PDF replacement (stays on the existing Replace endpoint), cross-supplier swap (Delete + re-Convert remains the path), editing on `UnderReview`/`Approved`, admin/reviewer/SupplierAdmin editing of applicant quotes, optimistic-concurrency tokens, and edits on quotes flagged `LegacyNeedsReview` (admin-only via spec 015).

## Bigger Picture

This spec sits squarely on top of the **post-May-13 information architecture** that emerged from spec 021 (`feedback-session-may13`). Specifically: spec 021 commit `07e07c6` decommissioned `Application/Details` as the draft editor, leaving `Application/Edit` as the single applicant-facing draft surface. The originating bug (URL `/Application/Edit/1003`) noticed that `Application/Edit` exposed item-level edit affordances but no per-quotation affordance — so this spec closes the loop the 021 consolidation opened.

Three load-bearing dependencies you may want context on:

- **Spec 013** owns the `branch.SupplierId == quotation.SupplierId` invariant. This spec re-asserts it on a new entity method `Quotation.ChangeBranch` rather than re-checking it only in the service ([plan.md Principle II](plan.md#constitution-check)).
- **Spec 015** owns the multi-currency snapshot rails (`EditAmount`, `ChangeCurrencyAsync`, `ExchangeRate.IsUsed`, the `LegacyNeedsReview` flag). The Edit path routes through those primitives unchanged; no new snapshot semantics are being introduced ([data-model.md §1.1](data-model.md#11-quotation-entity--additions)).
- **Spec 020** owns the `ComparisonArtifact` AI-cache. Edits invalidate the per-Item cache via a new narrow seam `IComparisonCacheInvalidator` ([research.md §R0.4](research.md#r04--comparisonartifact-cache-invalidation-seam)). The invalidation is *silent* — the reviewer's next *Generar todo* picks up the miss; no banner is shown.

No new schema, no new managed dependencies, no new admin-audit event types. The blast radius is intentionally small — six new files in production code, three new test classes, one refactor (`Supplier/Add.cshtml` consumes the new partial).

---

## Spec Review Guide (30 minutes)

> This guide focuses your 30 minutes on the parts of the plan that need human judgment. Each section points to a specific location and frames the review as questions.

### Understanding the approach (8 min)

Read [spec.md §User Story 1](spec.md#user-story-1--applicant-fixes-a-typo-on-a-draft-stage-quotation-priority-p1) and [§User Story 2](spec.md#user-story-2--applicant-applies-a-reviewer-requested-correction-priority-p1) for the originating use cases, then [plan.md §Summary](plan.md#summary) for how the implementation maps onto them. As you read, consider:

- Are *Price*, *Currency*, *ValidUntil*, *SupplierBranch* the right four fields to expose? Is there a fifth field — e.g. *Notes* or *SupplierContactId* — that applicants will reasonably ask for next, and if so does the partial shape ([research.md §R0.2](research.md#r02--shared-partial-shape-_quotefieldscshtml)) extend gracefully?
- The plan picks `Application/Edit` over `Item/Edit` for the affordance ([research.md §R0.1](research.md#r01--where-does-the-edit-affordance-render)). Does that match the May-13 "one screen, no leaving" ask, or does it overload an already-dense page?
- The Edit affordance is hidden when the application is outside `{Draft, ReturnedForChanges}` ([FR-008](spec.md#functional-requirements)). Is there any other state — e.g. an admin-initiated `OnHold` — where the applicant should still be able to fix a typo?

### Key decisions that need your eyes (12 min)

**No optimistic-concurrency token** ([plan.md §Complexity Tracking](plan.md#complexity-tracking))

The plan explicitly deviates from the constitution's "Optimistic concurrency MUST be used for entities with concurrent edit risk" gate, justifying it on the grounds that the application owner is the only actor and the two-tabs-same-user case is acceptably resolved by last-write-wins.
- Question for reviewer: Is the same-user-two-tabs case really the only concurrent-edit risk? What about a background `StageExpiryReminderService` (spec 021) that might transition state mid-edit — would a token catch that, or is the FR-008 422 gate sufficient?

**Branch invariant on the entity, not the service** ([research.md §R0.6](research.md#r06--branch-invariant-entity-vs-service))

`Quotation.ChangeBranch(SupplierBranch)` throws `ArgumentException` on cross-supplier; the service translates the exception into a `ModelState` field error.
- Question for reviewer: Is throwing-and-catching across the Application/Domain seam the right pattern here, or would a `TryChangeBranch` / result-type pattern keep control flow cleaner? Look at how `EditAmount` handles its own guards today for precedent.

**`SetValidUntil` introduction** ([T014](tasks.md#phase-2-foundational-blocking-prerequisites), [data-model.md §2.3 step 8](data-model.md#23-service-method-orchestration-order))

The plan adds a new `Quotation.SetValidUntil(DateOnly)` method because the entity has no such method today. The task narrative is internally ambivalent about the visibility (`internal` vs `public`).
- Question for reviewer: should `ValidUntil` even be a new entity method, or is a simple property setter (with the same guard) more in keeping with how other dates on the aggregate are handled? Worth a 30-second look at the existing `Quotation` shape.

**Silent AI-cache invalidation** ([FR-009](spec.md#functional-requirements), [research.md §R0.4](research.md#r04--comparisonartifact-cache-invalidation-seam))

When the applicant edits a quote on a returned application, the reviewer's prior AI comparison for that Item is wiped without notice. The reviewer regenerates on demand.
- Question for reviewer: is there a UX risk that a reviewer who was mid-conversation with a stale artifact now sees an empty comparison region with no explanation? Should the regeneration auto-fire instead of waiting for *Generar todo*? (Spec [assumption 2](spec.md#assumptions) accepts the trade-off explicitly — confirm you agree.)

**Synchronous, fail-fast cache invalidation** ([research.md §R0.4](research.md#r04--comparisonartifact-cache-invalidation-seam))

The invalidator runs after `SaveChangesAsync` but inside the same request. If the DB delete fails, the Edit POST fails too — even though the quotation was already saved.
- Question for reviewer: should this be wrapped in the same transaction as the entity save, or is the current "save first, invalidate after, fail loud" ordering correct? The current shape means a partial failure leaves a stale `ComparisonArtifact` next to a fresh quote.

### Areas where I'm less certain (5 min)

- [data-model.md §2.4 *ComparisonCacheInvalidator implementation*](data-model.md#24-new-abstraction-icomparisoncacheinvalidator) hedges between "deletes the row" and "sets a stale flag — to be confirmed by the spec 020 read path." [Task T009](tasks.md#phase-2-foundational-blocking-prerequisites) defers the choice to implementation time. If spec 020's read path doesn't expose a stale flag, the implementer will discover that mid-task; that may be fine, but it's a real source of late-binding risk.
- [research.md §R0.3](research.md#r03--branch-picker-data-source) says the picker filters branches by `IsActive` "if such a flag exists on `SupplierBranch`; otherwise all." I did not verify which is true. If `IsActive` exists, a deactivated branch that the quotation currently points to will show up as the selected option but be absent from the picker — worth a check.
- [Task T030](tasks.md#phase-5-user-story-3--applicant-changes-currency-on-an-attached-quotation-priority-p2) references a test name (`EditQuotation_IdempotentRepeat_DoesNotInvalidateCache`) that does not appear in [T028](tasks.md#phase-5-user-story-3--applicant-changes-currency-on-an-attached-quotation-priority-p2)'s explicit test list. Either T028 needs that extra case or T030's reference needs to point at the existing idempotency test in T017(d).
- The plan claims `IComparisonCacheInvalidator` "preserves spec 020 internals" but the chosen Infrastructure implementation directly mutates the `ComparisonArtifact` `DbSet` ([T009](tasks.md#phase-2-foundational-blocking-prerequisites)). If spec 020 grows additional invariants on `ComparisonArtifact` later, this end-run will silently bypass them.

### Risks and open questions (5 min)

- If the spec 020 `ComparisonArtifact` shape evolves to include reviewer-authored annotations bound to a specific hash, does the silent invalidation in [FR-009](spec.md#functional-requirements) lose reviewer work? Spec 023 does not address this.
- The `EditQuotationOutcome.MissingRate` branch ([data-model.md §2.2](data-model.md#22-new-service-method)) maps a domain exception (`MissingRateException` from spec 015) into HTTP 422. Is 422 the right code here? Spec 015 itself doesn't normalize on a code for missing-rate at the controller boundary. [contract](contracts/quotation-edit-endpoint.md#status-codes) commits to 422; worth a glance to confirm consistency with how Supplier/Convert reports the same situation today.
- The plan's success criteria include [SC-005](spec.md#measurable-outcomes) ("The existing Supplier/Add end-to-end suite remains green"). [T005](tasks.md#phase-2-foundational-blocking-prerequisites) refactors `Supplier/Add.cshtml` to consume the new partial. The Supplier/Add E2E tests rely on `data-testid` selectors; [T005](tasks.md#phase-2-foundational-blocking-prerequisites) notes "Existing `data-testid` selectors remain identical (the partial owns them now)." Is there a regression risk from cshtml-section ordering (validation script placement, antiforgery token location) that the test selectors won't catch?
- [Open Question OQ-3](spec.md#open-questions) defers email deep-linking to a future spec-021 touch-up. Confirm we're OK that reviewers who currently get a "returned for changes" email won't get a one-click path to the new Edit affordance in v1.

---
*Full context in linked [spec](spec.md), [plan](plan.md), [tasks](tasks.md), [research](research.md), [data-model](data-model.md), and the [endpoint contract](contracts/quotation-edit-endpoint.md).*
