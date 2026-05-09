# Review Guide: Programa Semilla Brand Pivot

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-05-09
**Status:** PASS (retry 1) — all prior "Areas where I'm less certain" items resolved 2026-05-09. Risks/open questions retained as reviewer-attention items but no longer block planning.

---

## What This Spec Does

Re-anchors the FundingPlatform web UI to the canonical sponsor-program identity *Programa Semilla* (under Sistema de Banca para el Desarrollo) so it matches the sponsor-branded Funding Agreement PDF that already ships in production from spec 018. Today an applicant lives in a forest-green "Capital Semilla"-named app and downloads a teal "Programa Semilla"-wordmarked PDF — the visual + name divergence undermines program credibility. This spec retires the placeholder palette/name in one mega-pivot.

**In scope:** Token rewrite (palette, type stack, status colors, shadow), partial retune (buttons, tables, cards, badges, inputs, alerts, modals), shared chrome (sidebar header + sponsor strip + cache-bust), all 9 empty-state SVGs, confetti palette, favicons + brand SVGs, BRAND-VOICE.md, email-template sender-name + signature, ~30 swept surfaces across applicant / reviewer / admin / auth, and the audit / contrast / regression / perf gates.

**Out of scope:** [Schema and dacpac changes](spec.md#out-of-scope), [PDF generation pipeline (spec 018 invariant)](spec.md#out-of-scope), [localization layer (spec 012 invariant)](spec.md#out-of-scope), Tabler.io upgrade, public marketing surface, multi-tenant brand swapping, email-embedded sponsor logos, sponsor-brand legal audit beyond the spec-018 footprint, any net-new wow moments.

## Bigger Picture

This is a brand-identity convergence pass between three already-shipped specs: 011 introduced the warm-modern *Forge / Capital Semilla* placeholder, 012 renamed the display brand to *Capital Semilla*, 017 rebuilt the admin dashboard at that bar, and 018 branded the Funding Agreement PDF with the canonical sponsor identity. Spec 019 is the moment the web UI catches up to the PDF the platform already produces. The mega-spec packaging mirrors specs 011 / 017 — the team's established posture for pre-prod sweeps where partial brand pivots would create more divergence than they resolve.

Two adjacent themes a reviewer should keep in mind: (a) the spec-011 motion catalog and reduced-motion contract are preserved verbatim — this is **not** a motion redesign; (b) [`tokens.css` is the only file allowed to hold raw hex](plan.md#summary), the spec-011 invariant carried forward and enforced by the [`brand-grep-gate.sh`](plan.md#source-code-repository-root) audit script (T030).

The single-tenant assumption matters here: if SBD ever wants to host sister programs on the same platform, multi-tenant brand swapping is a different spec entirely. The spec calls this out and the plan does nothing to enable or block that future.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [spec.md User Story 1](spec.md#user-story-1---end-to-end-visual-continuity-for-the-applicant-priority-p1) and the [Functional Requirements section](spec.md#functional-requirements). As you read, consider:

- Is "applicant continuity from Login through PDF" really the right MVP slice, or would shipping the [shared chrome (Foundational Phase 2)](tasks.md#phase-2-foundational-blocking-prerequisites) alone be enough to demo the pivot to the user before the per-surface sweep?
- The spec leaves [FR-014 type weights](spec.md#functional-requirements) at "target" rather than "MUST set"; the [research pin in R10](research.md#r10---display--heading-weight-values-pins-fr-014) defaults to 700/600 subject to SC-015 sign-off. Is "target → designer override at sign-off" the right deferral pattern, or should planning lock concrete floors so the values are testable in unit/visual tests rather than only in the sign-off gate?
- The spec asserts the [yellow accent `#F2C014` is decorative-only](spec.md#requirements) (NFR-003) and a grep gate enforces it (research R11). Is the grep heuristic — keyword-list against selectors containing `error|danger|warning|info|status|valid|invalid` — broad enough to catch real misuse, or will it false-pass on creative selector naming?

### Key decisions that need your eyes (12 min)

**Mega-spec packaging vs phased pivot** ([plan.md Summary](plan.md#summary))

The plan ships palette + name + voice + chrome + 30+ surfaces in one PR. Spec rationale: partial pivots would re-introduce divergence between applicant / reviewer / admin views. The cost is a single very-large diff and POM rewrites across most E2E pages.

- Question for reviewer: At pre-prod scale this matches the 011 / 017 precedent, but the diff size will make code review a multi-session effort. Is the team comfortable with that, or should the [Foundational phase (T005..T032)](tasks.md#phase-2-foundational-blocking-prerequisites) ship as its own PR first to lock palette + chrome before the surface sweep?

**Sponsor SVGs re-traced from PDF raster** ([research.md R2](research.md#r2--sponsor-logo-sources-resolves-oq-002))

The plan re-traces sponsor logos from the existing PDF asset strip rather than blocking on sponsor-vector requests. A follow-up task replaces them in place when originals arrive (no spec change).

- Question for reviewer: Re-tracing CROCUS / nexo / SBD logos from a 56 px-tall raster strip risks low fidelity. If the user notices visible jaggedness in the live walk-through (SC-015), what's the rollback path — ship anyway and replace later, or delay merge until originals arrive?

**Cache-busting via MSBuild-generated `BuildInfo.g.cs`** ([research.md R12](research.md#r12--cache-bust-strategy-for-tokenscss-resolves-spec-edge-case))

The plan adds an MSBuild target that emits `BuildInfo.g.cs` from `git rev-parse --short HEAD` at compile time and appends the hash as a query parameter to the `tokens.css` `<link>`.

- Question for reviewer: This is a small but new build-time codegen step. Is the project already comfortable with code-generated source files, or would a runtime hash (e.g., `Assembly.GetExecutingAssembly().Location` mtime) be more idiomatic and avoid the new MSBuild target?

**`BRAND-VOICE.md` moves to repo root** ([research.md R8](research.md#r8--brand-voicemd-canonical-location-resolves-oq-008))

Voice file moves from `specs/011-warm-modern-facelift/BRAND-VOICE.md` to `/BRAND-VOICE.md` with a single-line redirect banner left behind.

- Question for reviewer: Repo-root placement matches `CLAUDE.md` / `README.md`, but it does mean a future contributor reading spec 011's PR has to chase a redirect. Is that worth the discoverability win, or should the canonical file land under `specs/019-programa-semilla-brand/` instead?

**Density rule preserved via `data-density` attribute** ([research.md R14](research.md#r14--space-2-vs-space-4-density-rule-preserves-fr-031))

Reviewer surfaces keep `--space-2` cell padding, applicant surfaces get `--space-4`. The mechanism is the spec-011 `data-density="reviewer"` attribute; only the visual treatment of the table partial changes.

- Question for reviewer: See "Areas where I'm less certain" below — there's a numeric drift between FR-019 / FR-031 / research / quickstart that the reviewer should land definitively before T021 / T046 ship.

### Areas where I'm less certain (5 min) — all resolved on retry 1 (2026-05-09)

- **Applicant cell padding token — RESOLVED.** Canonical pinned to `--space-4` ≈ 16 px (spec 011 FR-060 carry-forward). FR-019, US2 Independent Test, research R14 E2E note, quickstart §2.3, T021 implementation, T046 test, and the US2 sub-summary in tasks.md all now say applicant `--space-4` ≈ 16 px / reviewer `--space-2` ≈ 8 px.
- **FR-001 / FR-006 / FR-037 / FR-040 task-tag coverage — RESOLVED.** FR-001 explicitly tagged on T028, T030, T076, T077; FR-006 on T075, T076, T078; FR-037 on T032, T082. FR-040 documented in a new "Out-of-scope guardrails (no implementing task)" table in tasks.md alongside FR-038/FR-039/FR-041/FR-042.
- **Admin sub-surface inventory — RESOLVED.** FR-027 inventory realigned to actual `Views/Admin/` project layout: drops `Audit` (no view exists), adds `Configuration` + `ImpactTemplates`. Spec FR-027, US3 narrative, T058 POMs, T067 implementation, T068 checklist sweep all consistent. T067 includes a NOTE that any future `AdminAudit` view becomes a follow-up scope.
- **`canvas-confetti` grep path — RESOLVED.** T072 now greps `src/FundingPlatform.Web/wwwroot/lib/canvas-confetti/`, matching the plan's vendored-library structure.
- **FR-021 yellow-badge contrast floor — RESOLVED.** T083 expanded with a targeted axe-playwright assertion that the yellow-accent badge variant reports `≥ 4.5:1` contrast for badge text on `--color-accent` fill (FR-021 / NFR-003).
- **US6 email scope vs project state — RESOLVED.** A NOTE on the US6 phase header records that no `EmailTemplates/` directory or `IEmailSender` wiring exists at this iteration. T075 skips with a clear message when `IEmailSender` is not registered. T076 sweeps via grep across Web/Application/Infrastructure for stale strings. T077 targets Identity sender-name configuration in `Program.cs` / `appsettings.*.json`. T078 records a deferred "Email subsystem (deferred)" row in `BRAND-PIVOT-SWEEP-CHECKLIST.md`.

### Risks and open questions (5 min)

- If [SC-015 designer sign-off](spec.md#measurable-outcomes) overrides the sampled `#1FA0A0` teal late in the cycle, are the [4 visual-regression baselines (T084)](tasks.md#accessibility--visual-regression) and the [updated `perf-baseline.json` (T086)](tasks.md#performance-baseline) cheap enough to recapture, or should baselines wait until after sign-off?
- Email-template sender-name change ([FR-006](spec.md#requirements), T076..T078) — does the AspireFixture's SMTP capture work for both Identity-flow emails (confirmation / reset) and any custom platform-generated emails, or does the spec need an explicit follow-up to enumerate which templates exist? T078 says "sweep any remaining email-template files surfacing in the grep" but doesn't bound the count.
- Sponsor strip print-hide attribute ([R13](research.md#r13--surfaces-requiring-print-stylesheet-adjustments-pins-spec-edge-case), T085) is opt-in via `data-print-hide="sponsor-strip"`. If a future surface forgets to set the attribute, the strip clutters its print view. Should there be a default-hide rule with explicit opt-in for auth pages, or is opt-out the right polarity given most prints are reports/agreements?
- The 10-años badge retirement ([OQ-007](spec.md#open-questions), R7) is deferred to a future spec when the program crosses its anniversary. Is anyone tracking the calendar trigger, or could the badge silently go stale?
- [FR-021 yellow-badge dark-text overlay](spec.md#requirements) is qualitative ("dark text overlay because `#F2C014` on white fails AA"). The spec-review pass flagged this as optional; the plan does not pin a `≥ 4.5:1` floor against the badge fill. Will [axe-playwright AA (FR-035 / SC-005, T083)](tasks.md#accessibility--visual-regression) catch a low-contrast text-on-yellow combination, or does the badge variant need its own targeted contrast assertion?

---

*Full context in linked [spec](spec.md), [plan](plan.md), [tasks](tasks.md), [research](research.md), [data-model](data-model.md), and [quickstart](quickstart.md).*
