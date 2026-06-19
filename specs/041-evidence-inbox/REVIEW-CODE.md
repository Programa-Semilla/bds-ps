# Code Review — 041 Evidence Inbox

---

## Deep Review Report

> Automated multi-perspective code review results. This section summarizes
> what was checked, what was found, and what remains for human review.

**Date:** 2026-06-19 | **Rounds:** 1/3 | **Gate:** PASS

Scope: this session's work (`a6e8a7c..HEAD`) — the spec-041 evidence inbox + read-only gate, plus the carried spec-040 audit-surface refactor. The full spec-040 surface was excluded (already deep-reviewed at its own delivery).

### Review Agents

| Agent | Findings | Status |
|-------|----------|--------|
| Correctness | 0 | completed |
| Architecture & Idioms | 1 | completed |
| Security | 0 | completed |
| Production Readiness | 2 | completed |
| Test Quality | 7 | completed |
| CodeRabbit (external) | — | skipped (CLI not installed) |
| Copilot (external) | — | skipped (CLI not installed) |

### Findings Summary

| Severity | Found | Fixed | Remaining |
|----------|-------|-------|-----------|
| Critical | 0 | 0 | 0 |
| Important | 3 | 3 | 0 |
| Minor | 7 | 4 | 3 (accepted/deferred) |

### What was fixed automatically

All three Important findings were the same real weakness in the **US2 crafted-mutation E2E test** — it didn't genuinely prove the FR-007 server-side read-only gate. Fixed in one round:
- The crafted Upload now sends a **real PDF** (was empty, so an unrelated empty-file guard was rejecting it); added a missing crafted **EditNote** POST (SC-003 requires all three mutations); and `CraftedPostAsync` was rewritten to fire from a **still-valid session** (process is now closed from a separate admin browser context so the antiforgery token doesn't rotate) via an authenticated `HttpClient`, asserting each crafted write returns **200 + lands on the evidence Index** (proving it reached the gate) with **no state change**.
- Four Minor fixes: integration inclusion test now asserts `FundName`/`ProcessName` (FR-003); the `ReturnedFromAudit` unit test now asserts `repo.Received()` for that state; and the projection's misleading `orderby` comment was corrected.

Re-run after fixes: filtered E2E `EvidenceInbox` **4/0**, integration `EvidenceInboxQueryTests` **9/0**, unit `ReturnedFromAudit` **2/0**.

### What still needs human attention

All Critical/Important findings were resolved. Three Minor findings remain, accepted as documented in [review-findings.md](review-findings.md) and `tasks.md` Deviations — reviewers may want to confirm the judgment calls:

- **Extra DB round-trip in the evidence `Index`** (`FundsUsageEvidenceController`): the `State` and `Process.Status` reads hit the same row twice. Left unmerged to avoid reshaping the security-ordered `IsAccessibleAsync`. Is the micro-cost acceptable vs the no-disclosure ordering risk? (We judged: yes.)
- **Silent 200-row cap** in `EvidenceInboxProjection`: no log when truncated. Pagination is explicitly out of scope for 041. Acceptable until the pagination iteration?
- **InMemory-only coverage** for the soft-deleted / archived-fund exclusions (no real-SQL E2E backstop), matching the spec-036 precedent and reusing already-shipping query filters.

### Recommendation

All findings addressed. Code is ready for human review with no known blockers; the three remaining Minor items are documented accept/defer decisions, not gaps.
