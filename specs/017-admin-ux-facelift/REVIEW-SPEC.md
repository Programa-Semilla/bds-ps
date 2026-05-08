# Spec Review: Admin UX/UI Facelift

**Spec:** specs/017-admin-ux-facelift/spec.md
**Date:** 2026-05-08
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** Spec is implementable as written. Builds on a strong base of prior specs (009/010/011/015/016) with explicit cross-references; scope and contracts are pinned; success criteria are measurable. A handful of planning-phase deferrals are explicit and documented in Assumptions.

## Completeness: 5/5

### Structure
- All required sections present: Overview, User Scenarios, Edge Cases, Functional Requirements, Out of Scope, Key Entities, Success Criteria, Assumptions
- No TBD or placeholder text
- Constitutional sections aligned (E2E required per FR-026, schema-locked per FR-027)

### Coverage
- 7 user stories with clear priority ordering (P1 wow + P1 sweep + P1 empty-states + 3 P2 follow-ups + P3 optional feed)
- 30 functional requirements + 10 OOS clauses
- 21 success criteria
- 13 assumptions explicitly captured
- 11 edge cases enumerated (zero-data, demoted-mid-session, sub-surface health, sentinel, dual-role, etc.)

### Issues
None blocking.

## Clarity: 4.5/5

### Language Quality
- Requirements use MUST consistently
- Numeric / DOM-class / route literals throughout (no "fast" / "user-friendly")
- Token references (`--motion-slow`, `--space-2`, `--color-primary-subtle`) are explicit contracts inherited from spec 011

### Ambiguities Found
1. **FR-015**: "with `/Admin` reachable via clicking the section header (or via a 'Panel' entry inside the section — choice deferred to planning)"
   - Issue: Two implementation paths offered; choice deferred.
   - Note: Acceptable as a planning-deferred decision (explicitly flagged), not a spec ambiguity. Worth pinning early in planning to avoid re-litigation.

2. **Edge case** "Pending-supplier count source is missing or stale: KPI shows the best available count"
   - Issue: "best available count" is vague.
   - Suggestion: Planning should pin the failure mode — show 0 with a soft warning glyph, vs. show "—" placeholder, vs. error tile. Spec deliberately leaves the choice to planning so this is a tracked open thread, not a spec defect.

3. **Voice-guide compliance** (FR-011, FR-013, FR-025, SC-018) depends on `BRAND-VOICE.md` from spec 011 being authoritative; the spec assumes (correctly) that this artifact exists and is enforceable.

## Implementability: 5/5

### Plan Generation
- Sweep inventory enumerated in FR-008 (10 sub-surfaces with sub-views named)
- New partials named: `_AdminDashboard`, `_CapabilityCard`
- Reused partials named: `_KpiTile`, `_EventTimeline`, `_PageHeader`, `_DataTable`, `_StatusPill`, `_EmptyState`, `_ActionBar`, `_ConfirmDialog`, `_ReportSubTabs`
- Data dependencies named: `SupplierService`, `AdminLegacyQuotationsService`, `AgingThresholdDays` config, sentinel-excluded user-store predicate, `AdminAuditEvent`
- Constraints realistic: schema unchanged, < 30 KB gzipped wire weight, 4 KPIs only

### Issues
None.

## Testability: 5/5

### Verification
- Greppable contracts: SC-005 (raw hex), SC-006 (inline `style=`), SC-016 (`git diff` empty against Database project)
- Behavioral contracts: SC-002 (4 reference fixtures), SC-003 (200 OK walk), SC-012 (404 vs 200 routing), SC-014 (feed visible/hidden)
- Visual contracts: SC-013 (DOM-class assertion), SC-015 (axe-playwright), SC-017 (PDF visual identity regression)
- E2E contracts: SC-019 (Playwright pass + new tests + reduced-motion run + semantic POMs)
- Designer/product sign-off: SC-021 (recorded in PR description)

### Issues
None.

## Constitution Alignment

Constitution (v1.0.0, ratified 2026-04-15) — 6 principles:

| Principle | Alignment |
|-----------|-----------|
| I. Clean Architecture | ✅ FR-007 routes data through Application-layer projections; no Web → Domain shortcuts. |
| II. Rich Domain Model | ✅ N/A — this is a presentation/projection spec; no domain entity changes. |
| III. E2E NON-NEGOTIABLE | ✅ FR-026 requires Playwright E2E per user story; SC-019 enforces. |
| IV. Schema-First (dacpac) | ✅ FR-027 / SC-016 forbid Database project edits; explicit `/speckit-spex-evolve` escape hatch. |
| V. Specification-Driven | ✅ This document. |
| VI. Simplicity / YAGNI | ✅ 10 OOS clauses; zero new fonts/illustrations/libraries; reuses existing partials; pre-prod scope rationale captured. |

No violations.

## Recommendations

### Critical (Must Fix Before Implementation)
None.

### Important (Should Fix)
None blocking. Two planning-phase deferrals to track:
- [ ] Pin FR-015 implementation choice (section header click vs. inline "Panel" entry) early in planning.
- [ ] Pin pending-supplier failure mode (empty edge case wording) early in planning so SC-002 fixtures are unambiguous.

### Optional (Nice to Have)
- [ ] Add a sentence to FR-007 naming the projection class (e.g., `IAdminDashboardProjection`) to align with spec 011's convention; currently named only via assumption + edge case. Saves planning ambiguity.
- [ ] Consider an explicit FR or SC for the "no card on dashboard for new capabilities" governance check that the Edge Cases section flags. Without it, the dashboard rots again the moment a future spec ships.

## Conclusion

Spec is sound and ready for clarify/plan. The cross-spec dependencies are load-bearing (009 sentinel, 010 reports + aging threshold + KPI tile, 011 tokens + partials + brand voice + illustrations, 015 currencies / exchange rates / legacy quotations, 016 admin audit events) but explicit and consumable.

**Ready for implementation:** Yes — proceed to `/speckit-clarify` then `/speckit-plan`.

**Next steps:**
1. `/speckit-clarify` to surface any underspecified areas before planning (recommended given the cross-spec surface area).
2. `/speckit-plan` to generate the implementation plan + design artifacts.
3. Then `/speckit-tasks` and `/speckit-implement`.
