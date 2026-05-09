# Quickstart — Spec 019 Programa Semilla Brand Pivot

**Branch**: `019-programa-semilla-brand` · **Date**: 2026-05-09

How to build, run, and validate the spec 019 work locally. Assumes `dotnet 10` SDK + Aspire bits are installed (per `CLAUDE.md`).

## 1. Build & boot

```bash
# whole solution
dotnet build FundingPlatform.slnx

# dev with persistent SQL data + auto-deployed dacpac
dotnet run --project src/FundingPlatform.AppHost
```

Then open: `http://localhost:5078/`.

Sentinel admin login (ephemeral mode): `admin@FundingPlatform.com` / `Sentinel123!`. In dev, the standard demo seeded admin (`admin@demo.com`) also works.

## 2. Brand presence (US1, US2, US3)

### Login surface

Open `http://localhost:5078/Account/Login`. Expect:

1. Left rail hero — large teal seedling mark (`mark.svg`) + "Programa Semilla" wordmark + tagline copy from `BRAND-VOICE.md`.
2. Page footer — sponsor partner-logo strip with: Banca para el Desarrollo SBD, CROCUS, nexo, Programa Semilla, 10 años badge.
3. Page background — clean white. No warm-cream tint.
4. Type — Inter only. No serif display anywhere.

```bash
# DOM check — sponsor strip present
curl -s http://localhost:5078/Account/Login | grep -c 'data-testid="sponsor-strip"' # expect 1
# DOM check — sidebar header (post-auth) renders Programa Semilla wordmark
# log in first, then:
curl -s -b cookie.txt http://localhost:5078/Application | grep -c 'Programa Semilla' # expect ≥ 1
```

### Authenticated chrome

Sign in. On every authenticated page expect:

1. Sidebar header — teal seedling mark + "Programa Semilla" wordmark; collapsed state shows mark only with hover tooltip "Programa Semilla".
2. Page footer — sponsor partner-logo strip (same as Login).
3. Tables — teal header band, cream zebra body rows, no internal grid lines on body rows.
4. Buttons — solid teal pill primary, ghost-teal secondary, danger red.
5. Inputs — 44 px min height, teal focus ring (4 px outer, 2 px inner).
6. Cards — 1 px border, no rest shadow, `--shadow-md` on hover/focus.
7. Badges — pill radius, semibold; primary teal, accent yellow with dark text overlay, status colors retuned.

### Reviewer density preserved (US2)

In Chromium devtools, inspect a row in the reviewer queue table and read `padding-top` / `padding-bottom`. Expected ≈ 8 px (`--space-2`). Then inspect an applicant table row (e.g., on `/Application/{id}`); expected ≈ 16 px (`--space-4`, spec 011 FR-060 canonical).

```js
// reviewer queue row
const rev = document.querySelector('[data-density="reviewer"] tbody tr td');
getComputedStyle(rev).paddingTop; // ≈ "8px"
// applicant table row
const app = document.querySelector('[data-density="applicant"] tbody tr td, table tbody tr td');
getComputedStyle(app).paddingTop; // ≈ "16px"
```

## 3. Admin sweep (US3)

Visit `/Admin`. Confirm the spec-017 dashboard layout (4 KPI tiles + 9 grouped capability cards + activity feed when populated) renders with:
- Teal accents on KPI tile rest state.
- Teal glow on the count-up animation (motion timing unchanged from spec 017).
- Yellow decorative dividers between capability sections.

Click-walk all 9 capability cards (Users, Groups, Suppliers, Reports, Currencies, Exchange Rates, Legacy Quotations, Impact Templates, System Configuration) plus the 4 KPI tiles. Each MUST return HTTP 200.

Visit `/Admin/Reports`. Active sub-tab pill MUST render teal background + white text. Animated KPI tickers MUST glow teal on count-up.

## 4. Wow moments re-walk (FR-029)

Walk each of the 4 spec-011 wow moments and capture an updated snapshot:

| Surface | URL | Test fixture |
|---|---|---|
| Applicant home dashboard | `/Application` | `tests/FundingPlatform.Tests.E2E/Pages/ApplicantHomePage.cs` |
| Journey timeline | `/Application/{id}/Journey` | `tests/FundingPlatform.Tests.E2E/Pages/JourneyPage.cs` |
| Signing ceremony | trigger from signing inbox | `tests/FundingPlatform.Tests.E2E/Brand/SigningCeremonyConfettiTests.cs` |
| Reviewer queue dashboard | `/Reviewer/Queue` | `tests/FundingPlatform.Tests.E2E/Pages/ReviewerQueuePage.cs` |

Each MUST screenshot-match the new committed reference under `specs/019-programa-semilla-brand/snapshots/` (refreshed in this PR).

## 5. Reduced-motion check (FR-034 / SC-010)

```bash
# Chromium devtools → Rendering → Emulate CSS media feature prefers-reduced-motion: reduce
```

Reload `/Admin` and trigger a signing ceremony. Expect:
- KPI tickers render final values immediately (no count-up animation).
- Confetti is suppressed; signing ceremony renders the static teal-branded card.

Run the dedicated reduced-motion E2E:

```bash
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~ReducedMotion"
```

MUST be green.

## 6. Confetti palette (US4)

Trigger a signing ceremony. Visually confirm confetti particles use teal `#1FA0A0` + yellow `#F2C014` + neutral `#FFFFFF` + primary-subtle `#D7EDED` (per research R5). No amber. No forest-green.

```bash
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~SigningCeremonyConfetti"
```

## 7. Empty-state illustrations (US5)

Force a fresh applicant account with zero applications:

```bash
# in the AspireFixture fresh DB scenario
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~EmptyApplicantHomeIllustration"
```

Visit `/Application`. Expect the empty-state illustration to render with **teal** strokes (not forest-green) on a white background.

For each of the 9 illustration scenes, verify visually that `<svg>` strokes use `stroke="var(--color-primary)"` or its inlined teal value. The 9 scenes are listed in `specs/011-warm-modern-facelift/spec.md` and re-shipped under `wwwroot/lib/brand/illustrations/`.

## 8. Email templates (US6)

Trigger an account confirmation send via the AspireFixture's test SMTP capture:

```bash
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~EmailTemplateSender"
```

Captured email MUST have:
- Sender display name: `"Programa Semilla / Sistema de Banca para el Desarrollo"`.
- Signature block: ends with the same string.
- No inline `<img>` for sponsor logos.
- No literal "Capital Semilla" or "Forge" in body, sender, or subject.

## 9. Brand-grep gates (SC-001, SC-002, NFR-003)

```bash
# all-in-one gate
scripts/brand-grep-gate.sh
```

Expected output: `OK` and exit 0. The script asserts:
1. Legacy hex (`#2E5E4E`, `#1F4438`, `#E1ECE6`, `#D98A1B`, `#FBEED6`, `#FAF7F2`, `#F4EFE6`, `#E5DED2`) absent outside `tokens.css` history comments and `wwwroot/lib/brand/pdf/` (FR-039 carve-out).
2. Strings `"Forge"` / `"Capital Semilla"` absent outside `git history`, archived `specs/011-warm-modern-facelift/BRAND-VOICE.md`, and `specs/`/`brainstorm/`/`CHANGELOG.md` documents.
3. `--color-accent` (or `#F2C014`) not paired with semantic-context selectors (per research R11 heuristics).

## 10. Token audit (SC-004)

```bash
scripts/tokens-audit.sh
```

Expected output: `OK` and exit 0. Asserts that `tokens.css` is the only file in the repo with raw hex color values (carries forward spec 011's tooling).

## 11. Asset budget (SC-011 / NFR-002)

```bash
scripts/asset-budget-check.sh
```

Expected output: `Total brand wire weight: <N> KB gz (limit: 400 KB)` with `<N> ≤ 400`. Removing Fraunces from `wwwroot/lib/fonts/fraunces/` frees ≈ 35 KB of headroom for the new sponsor SVGs and regenerated illustrations.

## 12. Performance baseline (NFR-001)

Capture a fresh post-pivot perf baseline:

```bash
scripts/perf-baseline-capture.sh \
  --url http://localhost:5078/Application \
  --url http://localhost:5078/Reviewer/Queue \
  --output specs/019-programa-semilla-brand/perf-baseline.json
```

Compare against `specs/011-warm-modern-facelift/perf-baseline.json`. LCP and TBT MUST NOT regress.

## 13. Schema-unchanged gate (SC-013)

```bash
git diff --stat src/FundingPlatform.Database/
# expected: zero output
```

If anything appears, escalate via `/speckit-spex-evolve` per FR-038.

## 14. PDF identity gate (SC-014)

```bash
dotnet test tests/FundingPlatform.Tests.E2E --filter "Category=PdfIdentity"
```

A regenerated fixture Funding Agreement PDF MUST be byte-equal to a pre-pivot fixture (or differ only in a document-creation timestamp). This spec touches no PDF surface; the test is a regression check (FR-039).

## 15. WCAG AA contrast (SC-005)

```bash
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~AxeContrast"
```

Pass on at least 5 surfaces: applicant home, reviewer queue, admin index, login, signing ceremony.

## 16. Visual regression (SC-012)

```bash
dotnet test tests/FundingPlatform.Tests.E2E --filter "FullyQualifiedName~VisualRegression"
```

Compares 4 reference snapshots (applicant home, reviewer queue, admin index, login) against committed baselines under `specs/019-programa-semilla-brand/snapshots/`. Diff is reviewed manually on PR.

## 17. Surface checklist (FR-028 / SC-008)

Walk `specs/019-programa-semilla-brand/BRAND-PIVOT-SWEEP-CHECKLIST.md` row by row. Every row MUST be checked before merge. The checklist enumerates all swept surfaces with columns for: visual tokens / component vocabulary / voice-guide compliance / sponsor chrome / motion / accessibility.

## 18. Designer / product / user sign-off (SC-015)

Open the running app on the user's hardware. Walk: Login → applicant home → reviewer queue → admin index → signing ceremony. The user reviews:
- Hex palette match against the PDF reference.
- Sponsor strip layout on `_Layout` and on auth pages.
- Sidebar header layout.
- Display + heading weight values (research R10) — pin or override.

Record sign-off in the PR description.

## 19. Run the test suites

```bash
# unit (no expected delta — quick safety net)
dotnet test tests/FundingPlatform.Tests.Unit

# integration (no expected delta — same)
dotnet test tests/FundingPlatform.Tests.Integration

# E2E (Playwright; full suite — required for delivery bar)
dotnet test tests/FundingPlatform.Tests.E2E
```

All MUST pass. Memory bar: a feature is not delivered until the **full E2E suite has been personally executed and is green**.

---

## What "done" looks like

- [ ] Brand-grep gate green (SC-001 / SC-002).
- [ ] Sponsor strip rendered on `_Layout` + Login + Register + Reset Password + Confirm Email (SC-003).
- [ ] Tokens audit green (SC-004).
- [ ] `axe-playwright` AA passes on 5 representative surfaces (SC-005).
- [ ] All 4 wow moments re-walked at the new bar; snapshots refreshed (SC-006 / SC-012).
- [ ] `BRAND-VOICE.md` updated; voice review checked off in `BRAND-PIVOT-SWEEP-CHECKLIST.md` for every swept view (SC-007).
- [ ] `BRAND-PIVOT-SWEEP-CHECKLIST.md` shipped with every row checked (SC-008).
- [ ] Full E2E suite green locally (SC-009).
- [ ] Reduced-motion E2E test green; no new motion outside spec 011 catalog (SC-010).
- [ ] Asset budget ≤ 400 KB gz (SC-011 / NFR-002).
- [ ] `git diff --stat src/FundingPlatform.Database/` empty (SC-013).
- [ ] PDF identity preserved (SC-014).
- [ ] User sign-off (palette + sponsor strip + sidebar header + heading weights) recorded in PR description (SC-015).
- [ ] Perf baseline committed; LCP / TBT no-regression vs spec 011 baseline (NFR-001).
