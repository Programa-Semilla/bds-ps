# Review Brief: Admin UX/UI Facelift

**Spec:** specs/017-admin-ux-facelift/spec.md
**Generated:** 2026-05-08

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Elevates the admin area to spec 011 quality. `/Admin` becomes a capability-complete dashboard (4 action KPIs + 9 grouped capability cards + optional activity feed); every admin sub-surface gets the warm-modern sweep at spec 011 quality bar (tokens-only, partial library, illustration-backed empty states, voice-guide-compliant es-CR copy, semantic POMs); sidebar admin entries collapse under a section header; legacy "AdminCurrencies/AdminExchangeRates/AdminLegacyQuotations" routes get normalized; Reports tabs are re-templated with pill-style chips and animated KPI tickers. Schema unchanged. PDF carve-outs preserved.

## Scope Boundaries

- **In scope:** `/Admin` dashboard re-design + all 10 admin sub-surfaces (Users, Groups, Suppliers, Reports tabs, Currencies, Exchange Rates, Legacy Quotations, Impact Templates, System Configuration); empty-state illustrations across admin tables; sidebar admin grouping; route normalization (3 controllers); Reports tab + KPI tile UX refresh; admin-event activity feed (P3, optional).
- **Out of scope:** Real-time push, new admin capabilities, schema changes, localization layer changes, dark mode, audit-log retention/policy, removing admin capabilities, Reports new content, Sentinel/admin-role rules from spec 009.
- **Why these boundaries:** Pure facelift + capability-completeness. Mirrors spec 011's mega-spec packaging; pre-prod scope justifies aggressive sweep + route renames. Keeps schema and audit model frozen so `/speckit-spex-evolve` is the only escape hatch.

## Critical Decisions

### Mega-spec packaging (single bundled spec)
- **Choice:** One spec for index dashboard + sub-surface sweep + structural cleanup (sidebar grouping + route normalization + Reports tab UX).
- **Trade-off:** Larger review surface; one E2E sweep; one PR. Same risk profile as spec 011 / 008.
- **Feedback:** Should structural cleanup (sidebar grouping + route renames) split into a follow-up spec, or stay bundled?

### Action-oriented KPI set (4 tiles)
- **Choice:** Pending suppliers / Pending legacy quotations / Aging applications / Active users.
- **Trade-off:** 3 actionable + 1 health signal. Volume-oriented alternatives (totals only) were considered and rejected as less operationally useful.
- **Feedback:** Are these the four signals that map to the moments where an admin most often needs to act?

### Route normalization without redirect shim
- **Choice:** Old `/Admin/AdminCurrencies` etc. return 404; no HTTP redirect from old to new path.
- **Trade-off:** Hard cutover keeps the spec scope clean; pre-prod justifies the call. Risk: any external bookmark gets a 404.
- **Feedback:** Confirm no external system has hard-coded the old paths.

### Sub-surface sweep at spec 011 wow-moment quality bar
- **Choice:** HTML restructuring + POM rewrites permitted across every admin sub-surface.
- **Trade-off:** Significant E2E rewrite cost; matches saved memory `feedback_ui_quality_over_e2e_stability`.
- **Feedback:** Confirm willingness to absorb the POM rewrite cost across all 10 sub-surfaces in one spec.

### Activity feed treated as P3 / optional
- **Choice:** Feed renders only when ≥ 1 `AdminAuditEvent` in last 30 days; hides entirely when empty (no empty rail).
- **Trade-off:** Avoids blocking dashboard delivery if `AdminAuditEvent` coverage is sparse in dev. Loses a signal on quiet weeks.
- **Feedback:** Is "hide entirely" the right empty-state behavior, or should the feed always render with a "No recent activity" empty state?

## Areas of Potential Disagreement

> Decisions or approaches where reasonable reviewers might push back.

### Mega-spec packaging vs decomposition
- **Decision:** One bundled spec.
- **Why this might be controversial:** 30 FRs + 7 stories + 10 sub-surfaces = a meaningful review surface; some reviewers prefer 2-3 sequenced specs.
- **Alternative view:** Spec A = index dashboard + KPIs; Spec B = sub-surface sweep + empty states; Spec C = structural cleanup (sidebar/routes/reports tabs).
- **Seeking input on:** Comfort with the mega-spec risk profile vs the decomposition overhead.

### Section grouping ("Catálogo" mixing Suppliers + Currencies + Exchange Rates + Impact Templates)
- **Decision:** Three sections — Usuarios y acceso / Catálogo / Operaciones.
- **Why this might be controversial:** Suppliers and Impact Templates are categorically different from Currencies/Exchange Rates (entity catalog vs configuration catalog). Some reviewers may prefer four sections (split Catálogo).
- **Alternative view:** Four sections — Users & Access / Suppliers / Catalog (Currencies/Rates/Templates) / Operations.
- **Seeking input on:** Three vs four sections; if four, the split point.

### Route normalization scope
- **Decision:** Only the three drift cases (`AdminCurrencies` / `AdminExchangeRates` / `AdminLegacyQuotations`); `Admin` prefix stays in class names.
- **Why this might be controversial:** Some reviewers may want full "kill the prefix" cleanup including class name + namespace; others may want zero changes since pre-prod wins are cheap to defer.
- **Alternative view:** Either (a) extend normalization to class names + folders, or (b) defer entirely until a real "admin module reorganization" spec.
- **Seeking input on:** Right cut.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Dashboard root partial | `_AdminDashboard` | New, replaces inline 3-card grid |
| Capability card partial | `_CapabilityCard` | New, per-capability tile |
| Section header copy | "Usuarios y acceso" / "Catálogo" / "Operaciones" | es-CR, voice-guide-compliant |
| Sidebar section slug | `admin-section` | New testid |
| Normalized routes | `/Admin/Currencies` / `/Admin/ExchangeRates` / `/Admin/LegacyQuotations` | Drops `Admin` prefix on three controllers |
| Sweep checklist deliverable | `ADMIN-SWEEP-CHECKLIST.md` | Per FR-008 + SC-007 |
| Review brief (this doc) | `review_brief.md` | Per spec convention |
| Spec review report | `REVIEW-SPEC.md` | Per spex-gates convention |

## Open Questions

- [ ] FR-015: pin "section header click target" vs "Panel sub-entry" implementation choice during planning.
- [ ] Pending-supplier failure-mode (zero / "—" / error tile) when source is missing or stale — pin during planning.
- [ ] Pending-supplier source enum value — confirm spec-013 supplier-status mapping during planning.
- [ ] Whether to add an explicit governance FR / SC for "future admin specs must update the dashboard's capability cards" (open in REVIEW-SPEC.md optional recommendations).
- [ ] Whether to name the dashboard projection (e.g., `IAdminDashboardProjection`) in the spec or defer to plan.

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| POM rewrite cost overrun across 10 sub-surfaces | High | Saved feedback memory accepts this trade-off; planning sequences POM work per surface so each surface ships green. |
| Dashboard rots when next admin capability lands | Medium | Edge case captured; governance check is an open thread (see Open Questions). |
| `AdminAuditEvent` data sparse in dev → activity feed never appears | Low | FR-024 hides feed gracefully; no empty rail. |
| Pending-supplier source ambiguity | Medium | Assumption captured; planning pins the enum value. |
| Route 404 hits from external bookmarks | Low (pre-prod) | No-redirect call is intentional; pre-prod fixture means real bookmarks don't exist. |
| Mega-spec scope creep | Medium | 10 OOS clauses pin the boundary; ADMIN-SWEEP-CHECKLIST.md drives the inventory walk. |

---
*Share with reviewers before implementation.*
