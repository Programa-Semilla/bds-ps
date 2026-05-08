# Designer / product review signoff record — spec 017

> Per SC-021. T087 produces this draft note for the PR description; the actual designer/product sign-off is a human-only check captured on the live PR.

## What to walk through

The reviewer should land on `/Admin` as an Admin user against each of the four reference fixtures from SC-002:

1. **Zero of everything** — fresh install / freshly registered admin: 4 KPI tiles all read 0 (or "—" for legacy), 9 capability cards present, activity feed absent.
2. **Mixed mid-state** — some pending suppliers, some legacy quotations, ≥ 1 aging application, several active users: KPI strip animates 0 → final on first paint over `--motion-slow`; KPI deep-links navigate to filtered surfaces.
3. **All thresholds tripped** — 30+ pending suppliers, 5+ legacy quotations, 50+ aging applications, 100+ users: KPI tiles show large counts in `N0` formatting; tickers still cap at `--motion-slow`.
4. **Prod-like dataset** — values that mirror production: tile readings reasonable, 9 cards still scan above-fold on 1366 × 768 (or with one scroll), activity feed shows ≤ 5 most-recent events with es-CR copy + relative timestamps + deep-links.

## Sign-off criteria (SC-021)

The reviewer confirms, for each reference fixture above:

- [ ] KPI strip is identifiable on first paint without scrolling.
- [ ] Capability sections (`Usuarios y acceso` / `Catálogo` / `Operaciones`) are identifiable on first paint.
- [ ] Activity feed (when present) is identifiable on first paint.
- [ ] Voice-guide pass — no ALL CAPS shouting, no exclamation marks, no "submit" CTAs, no passive voice in microcopy.
- [ ] Reduced-motion mode renders ticker targets immediately (verified by toggling OS-level setting).

## How this draft is produced

T087 runs after Phase 7 + Phase 10 work completes; the draft enumerates the SC-021 criteria so the reviewer can paste sign-off directly into the PR conversation. Automation does not stamp human sign-off.

## Status

DRAFT — awaiting human reviewer.
