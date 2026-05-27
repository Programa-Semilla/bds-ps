# Quickstart: Validating 027 Review & Funding-Agreement UX Refinements

How to run and verify each story. Delivery bar: the **full E2E suite must be personally executed and green** (constitution III, SC-008).

## Run

```bash
dotnet build FundingPlatform.slnx
dotnet run --project src/FundingPlatform.AppHost          # dev (persistent SQL + dacpac)
```

Seed logins (ephemeral E2E): admin `admin@programa-semilla.test` / `Sentinel123!`; demo `applicant@ / reviewer@ / demo-admin@programa-semilla.test` / `Demo123!`.

## Per-story manual verification

- **US1** — Generate an agreement as a reviewer, open `/Applications/{id}/FundingAgreement`; the "Generado — … por X" line shows a name, not a GUID. Delete/clear the generator's name → falls back to email.
- **US2** — On the signed-upload section, click Aprobar → confirm "Esto ejecuta el convenio." appears; dismiss → no change; confirm → executes. Click Rechazar → "Esto rechaza la carga…" appears; reject still requires the comment.
- **US3** — On the FA page, the applicant block shows company, representative, legal id + type, email, phone, código del solicitante, group, submission date. Empty optional field → "—". Generate the PDF → document body unchanged.
- **US4** — Walk one application (with ≥1 approved and ≥1 rejected line, and a non-CRC quote) through: reviewer review → applicant accept/reject → reviewer generate → applicant sign → reviewer signed-review. At every screen each line shows line code, product, category, **technical specs**, status; approved lines show supplier + amount (+ conversion note); rejected lines show reason + every quoted supplier/amount.
- **US5** — As reviewer, set "Código del solicitante" on `/Review/{id}`, save → persists; appears on the FA page; log in as that applicant → visible read-only on profile, no edit control.
- **US6** — Spot-check forms across applicant/admin/reviewer: every required field shows the `*` marker with `aria-label="campo obligatorio"`; optional fields show none.
- **US7** — On applicant forms, each field has an info icon; hover → HTML tooltip renders formatted (bold/list), not escaped; leave → dismisses; es-CR copy.
- **US8** — As admin, sidebar shows Inicio / Administración / Proceso groups; every prior destination still reachable. As reviewer/applicant, their items and role-gating unchanged. Supplier-admin-only variant unchanged.

## Tests

```bash
dotnet test tests/FundingPlatform.Tests.Unit          # projection mapping (US4), display-name fallback (US1)
dotnet test tests/FundingPlatform.Tests.Integration   # CodigoPersonal write via real DB (US5)
dotnet test tests/FundingPlatform.Tests.E2E           # per-story Playwright journeys (gate)
```

E2E must drive the **real UI journey** from the landing page through to each surface — no deep-link shortcuts to MVC routes the UI never exposes (project convention). Key E2E:
- US1/US3/US4 signed-review: FA page assertions (name not GUID; applicant block; decision summary).
- US2: confirm-modal appears and gates the POST.
- US4: a single test that asserts the same field set on all five screens.
- US5: reviewer sets code → applicant profile shows it read-only.
- US6: marker present on a sampled required field per form area.
- US7: hover shows the HTML tooltip bubble.
- US8: per-role sidebar destination parity (before/after) + the three group headers.
