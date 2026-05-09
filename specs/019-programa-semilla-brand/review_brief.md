# Review Brief: Programa Semilla Brand Pivot

**Spec:** specs/019-programa-semilla-brand/spec.md
**Generated:** 2026-05-09

> Reviewer's guide to scope and key decisions. See full spec for details.

---

## Feature Overview

Re-anchor the FundingPlatform web platform's visual + naming identity to the canonical sponsor-program brand expressed in the Funding Agreement PDF (spec 018) — Programa Semilla under Sistema de Banca para el Desarrollo, with co-sponsors Banca para el Desarrollo SBD, CROCUS, nexo, and the 10 años badge. Today's web app reads as forest-green "Capital Semilla" while the PDF it produces reads as teal "Programa Semilla"; this spec resolves that drift in a single pass. Going beyond palette, the spec retunes typography (drops Fraunces, sans-only Inter), retunes every component vocabulary (cards, tables, buttons, badges, inputs, sidebar, alerts, modals) to the airy/crisp PDF feel, lands the sponsor-logo strip on `_Layout` and the auth pages, and re-walks every applicant + reviewer + admin + auth surface at the spec-011 wow-moment quality bar. Schema and PDF generation are untouched.

## Scope Boundaries

- **In scope:** Display name pivot (Capital Semilla → Programa Semilla); `tokens.css` palette and type-stack rewrite; component retune across cards / tables / buttons / badges / inputs / sidebar / alerts / modals; sponsor partner-logo footer strip on `_Layout`; seedling-mark hero on Login + Register; brand-SVG + favicon swap; full surface sweep across all four roles (applicant + reviewer + admin + auth); E2E POM rewrites; `BRAND-VOICE.md` rewrite; email sender display + signature update; signing-ceremony confetti palette retune; 9-scene illustration set retint; visual-regression baseline + axe-AA verification; manual `BRAND-PIVOT-SWEEP-CHECKLIST.md` deliverable.
- **Out of scope:** Schema or database changes; PDF generation pipeline behavior; localization translation files (spec 012 invariant); Tabler.io bundle upgrade; net-new wow moments beyond spec 011's four; email-embedded sponsor logos; multi-tenant brand swapping; sponsor-logo legal audit beyond what already ships in the funding agreement PDF; public marketing surface.
- **Why these boundaries:** The platform is pre-production; an aggressive single-mega-spec re-sweep mirrors the established 011 / 017 packaging precedent. Schema and PDF are intentionally frozen to keep the change blast-radius web-only and preserve byte-identity of agreements already issued.

## Critical Decisions

### Single mega-spec packaging
- **Choice:** One spec covering tokens, sponsor chrome, full surface sweep, voice rewrite, asset replacement, and E2E POM rewrites. One PR, one E2E run.
- **Trade-off:** Larger review surface + bigger blast-radius PR vs. inconsistency window if split.
- **Feedback:** Are you comfortable with a single mega-spec at this scope, or would a 2-spec sequence (tokens + signature surfaces first, full sweep second) reduce review burden enough to justify the inconsistency window?

### Sans-only type stack (drop Fraunces)
- **Choice:** Inter for both display + body, JetBrains Mono kept for codes/IDs. Fraunces removed.
- **Trade-off:** Web type stack matches the seed PDF's actual visual reading (no serif), saves ≈ 35 KB. Loses some of the "personality" Fraunces gave headings in spec 011.
- **Feedback:** Does the PDF's no-serif visual matter more than spec 011's display-serif personality?

### Hex pinning now (sampled from PDF)
- **Choice:** Teal `#1FA0A0`, accent `#F2C014`, table-zebra `#FFF3E5` are pinned in the spec from PDF samples; designer override available at the SC-015 sign-off gate.
- **Trade-off:** Pinning gives implementers a concrete target on day one. If the Programa Semilla brand book pins different values, OQ-001 catches it at sign-off.
- **Feedback:** Is sampling-from-PDF acceptable, or do we need a brand-book lookup before code lands?

### Yellow accent is decorative-only (NFR-003)
- **Choice:** `#F2C014` on white measures ≈ 1.7:1 contrast — fails WCAG AA — so yellow is reserved for decorative dividers and filled-badge backgrounds with dark text overlay. A linter/grep gate enforces no semantic-meaning yellow.
- **Trade-off:** Honest about contrast failure; preserves brand fidelity to the PDF's gold rule. Constrains where yellow can be used.
- **Feedback:** Acceptable, or should we pick a darker-yellow alternate that passes AA on its own?

### Sponsor strip on `_Layout` (every authenticated page)
- **Choice:** Partner-logo composite ≤ 56 px tall on `_Layout`, plus on Login + Register + Reset + Confirm.
- **Trade-off:** "Brand presence is felt continuously" is the spec's explicit goal. Vertical real-estate cost on dense reviewer surfaces.
- **Feedback:** Is per-page sponsor chrome the right call, or should it be auth-only + footer-only on dense reviewer/admin pages?

## Areas of Potential Disagreement

> Decisions or approaches where reasonable reviewers might push back.

### Wholesale rename Capital Semilla → Programa Semilla in one PR
- **Decision:** Sweep all surfaces in a single change.
- **Why this might be controversial:** Capital Semilla shipped in spec 012 (April 2026) — only weeks ago. A second rename could read as churn to anyone who tracked spec 012's sign-off gate.
- **Alternative view:** Wait for a stable display-name period; only retire *Capital Semilla* once Programa Semilla branding has been validated externally.
- **Seeking input on:** Confirmation that the PDF reference is the canonical sponsor-program identity and that *Capital Semilla* was an interim choice, not the long-term display name.

### Drop Fraunces despite spec 011 declaring it canonical
- **Decision:** Sans-only Inter; Fraunces vendored files removed.
- **Why this might be controversial:** Spec 011 + spec 018 both declared Fraunces as the heading family; the PDF generator declares Fraunces. Removing Fraunces from the web while spec 018 keeps it for PDF generation creates a small dual-stack.
- **Alternative view:** Keep Fraunces on the web for heading "voice" parity with the PDF's declared stack.
- **Seeking input on:** Whether the visual reading of the PDF (no serif) takes precedence over the declared stack in spec 018.

### Single-mega-spec scope vs. two-spec sequence
- **Decision:** Single mega-spec.
- **Why this might be controversial:** Mega-specs concentrate risk; each surface re-walk + POM rewrite multiplies merge-friction.
- **Alternative view:** Spec A (tokens + assets + sponsor chrome + signature surfaces); Spec B (rest of the sweep + POM rewrites).
- **Seeking input on:** Whether the saved memory `feedback_ui_quality_over_e2e_stability` and the spec 011 / 017 precedent justify single-mega here.

### Reviewer surfaces inherit sponsor strip on every page
- **Decision:** Sponsor strip on `_Layout` means it lands on dense reviewer + admin tables.
- **Why this might be controversial:** Reviewers value vertical density; persistent sponsor chrome competes with table real estate.
- **Alternative view:** Sponsor strip on auth + applicant surfaces only; reviewer/admin surfaces use a minimal seedling-only footer.
- **Seeking input on:** Whether reviewers have raised brand-presence vs. density concerns we should pre-empt.

## Naming Decisions

| Item | Name | Context |
|------|------|---------|
| Display brand | Programa Semilla | Sidebar header, page title suffix, email sender display, footer. Replaces *Capital Semilla* and any dangling *Forge*. |
| Logo asset family | seedling mark + Programa Semilla wordmark | Replaces existing `wwwroot/lib/brand/{mark.svg, wordmark.svg, seal.svg}`. |
| Sponsor partner-logo composite | Banca para el Desarrollo SBD + CROCUS + nexo + Programa Semilla + 10 años | Footer strip on `_Layout` + auth pages; matches spec 018's PDF composite. |
| Token: primary | `--color-primary: #1FA0A0` | Sampled from PDF logo disc; replaces `#2E5E4E`. |
| Token: accent | `--color-accent: #F2C014` | Sampled from PDF gold rule; replaces `#D98A1B`; decorative-only by NFR-003. |
| Token: table-zebra | `--color-table-zebra: #FFF3E5` | New token; sampled from PDF table cream row. |
| Type stack | Inter (display + body) + JetBrains Mono (code) | Drops Fraunces. |
| Deliverable: sweep checklist | `BRAND-PIVOT-SWEEP-CHECKLIST.md` | One row per swept surface; six columns (tokens / components / voice / sponsor / motion / a11y). |
| Code namespaces | `FundingPlatform` | Unchanged (spec 012 invariant). |

## Open Questions

- [ ] Does Programa Semilla's brand book pin specific teal + yellow + neutrals that should override the PDF samples? (OQ-001)
- [ ] Sponsor logo source files: extract from PDF (low fidelity) or request originals from sponsors? (OQ-002)
- [ ] Login hero — large seedling mark only, or commission a "growing seed" scene? (OQ-003)
- [ ] Sidebar collapsed-state breakpoint — Tabler default 992 px or custom? (OQ-004)
- [ ] Confetti palette — teal + yellow only, or include cream + danger-soft? (OQ-005)
- [ ] Email signature layout — text-only or inline seedling mark (compatibility risk)? (OQ-006)
- [ ] 10 años badge — graceful retirement plan when "10 años" stops being current? (OQ-007)
- [ ] BRAND-VOICE.md canonical location — repo root, new spec dir, or replace spec 011 in place? (OQ-008)
- [ ] Visual-regression tooling — continue Playwright snapshots or adopt Percy/Chromatic? (OQ-009)

## Risk Areas

| Risk | Impact | Mitigation |
|------|--------|------------|
| Yellow `#F2C014` carries semantic meaning somewhere it shouldn't | Med | NFR-003 + linter/grep gate + axe-playwright across 5 representative surfaces (FR-035 / SC-005). |
| BRAND-VOICE.md drift survives the sweep (Forge or Capital Semilla strings remain) | Med | SC-002 grep target covers both names; `BRAND-PIVOT-SWEEP-CHECKLIST.md` voice column per swept view. |
| Visual regression on the spec 011 wow moments (4 surfaces) | High | FR-029 + SC-006 — explicit re-walk + snapshot update for each. |
| POM rewrite cost overrun across many surfaces | Med | Saved memory `feedback_ui_quality_over_e2e_stability` accepts the trade-off; planning sequences POM work per surface. |
| WCAG AA contrast regression on retuned status palette | Med | FR-012 + FR-035 + SC-005 — axe-playwright run across ≥ 5 surfaces. |
| Asset budget regression from sponsor-strip composite + seedling SVGs | Med | NFR-002 / SC-011 — ≤ 400 KB gz total; Fraunces removal frees ≈ 35 KB headroom. |
| Print stylesheet leaves sponsor strip on dense print views | Low | Edge case explicit; print-only test asserts. |
| Cached `tokens.css` mid-deploy serves stale palette | Low | Edge case + cache-bust query string on `_Layout` reference. |
| PDF byte-identity break (would invalidate already-issued agreements) | High | SC-014 — regenerated fixture PDF is byte-equal to pre-pivot, or differs only in document timestamp. |
| Schema accidentally touched | High | SC-013 — `git diff main -- src/FundingPlatform.Database/` is empty. |

---
*Share with reviewers before implementation.*
