# Code Review — Spec 018: PDF Template Lift

**Spec:** [spec.md](spec.md)
**Plan:** [plan.md](plan.md)
**Tasks:** [tasks.md](tasks.md)
**Implementation HEAD:** `2dd427a` (branch `018-pdf-template-lift`)
**Reviewer:** Claude (speckit.spex-gates.review-code, autonomous mode)
**Date:** 2026-05-08

## Compliance Summary

**Overall score:** 23 / 24 functional requirements satisfied (~98%).

| Section | Score |
|---|---|
| Functional Requirements (FR-001..FR-024) | 23 / 24 |
| Spec compliance gate | PASS (≥ 95% threshold met → deep review eligible) |
| Constitution check | PASS — Domain invariants on entities (II), dacpac schema (IV), spec→plan→tasks honored (V) |

### FR roll-call

| FR | Status | Implementation reference |
|---|---|---|
| FR-001 — header logo on every page | Compliant | [`_BrandHeader.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_BrandHeader.cshtml), [`_FundingAgreementLayout.cshtml:80-91`](../../src/FundingPlatform.Web/Views/FundingAgreement/_FundingAgreementLayout.cshtml) |
| FR-002 — partner footer composite | Compliant | [`_BrandFooter.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_BrandFooter.cshtml), [`_FundingAgreementLayout.cshtml:93-109`](../../src/FundingPlatform.Web/Views/FundingAgreement/_FundingAgreementLayout.cshtml) |
| FR-003 — A4 portrait + 20mm/18mm margins | Compliant | [`_FundingAgreementLayout.cshtml:11-14`](../../src/FundingPlatform.Web/Views/FundingAgreement/_FundingAgreementLayout.cshtml), [`SyncfusionFundingAgreementPdfRenderer.cs:22-33`](../../src/FundingPlatform.Infrastructure/DocumentGeneration/SyncfusionFundingAgreementPdfRenderer.cs) |
| FR-004 — brand palette + vendored typography | Compliant | [`_FundingAgreementLayout.cshtml:18-43`](../../src/FundingPlatform.Web/Views/FundingAgreement/_FundingAgreementLayout.cshtml) (`@font-face` from `/lib/fonts/`, `:root` color tokens) |
| FR-005 — cover page | Compliant | [`_CoverPage.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_CoverPage.cshtml) |
| FR-006 — distinct-action-takers commission list | Compliant | [`FundingAgreementController.cs:701-714`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs) |
| FR-007 — intro page (3 fixed paragraphs) | Compliant | [`_IntroPage.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_IntroPage.cshtml) |
| FR-008 — Recursos solicitados table | Compliant | [`_RequestedResourcesPage.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_RequestedResourcesPage.cshtml) |
| FR-009 — Resultados comisión section | Compliant | [`_CommitteeResultsPage.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_CommitteeResultsPage.cshtml) |
| FR-010 — supplier compliance table | Compliant | [`_SupplierVerificationPage.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_SupplierVerificationPage.cshtml) |
| FR-011 — sworn declaration page | Compliant | [`_SwornDeclarationPage.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_SwornDeclarationPage.cshtml) |
| FR-012 — LineCode required for review decision | Minor deviation | [`ReviewService.cs:96-103`](../../src/FundingPlatform.Application/Services/ReviewService.cs) — `RequestMoreInfo` allowed without LineCode per documented R-008 design (research.md). Spec acceptance scenario 1 phrases the requirement as "submit their decision (approve or reject)" so this is a defensible interpretation, but worth a human-eyes confirmation. |
| FR-013 — LineCode ≤16, per-Application unique | Compliant | [`Item.cs:260-`](../../src/FundingPlatform.Domain/Entities/Item.cs), [`Application.cs:91-114`](../../src/FundingPlatform.Domain/Entities/Application.cs), [`dbo.Items.sql:34-37`](../../src/FundingPlatform.Database/Tables/dbo.Items.sql) (filtered UNIQUE index) |
| FR-014 — trim + reject whitespace-only | Compliant | Entity invariants in [`Item.AssignLineCode`](../../src/FundingPlatform.Domain/Entities/Item.cs) and [`Application.SetCompanyName`](../../src/FundingPlatform.Domain/Entities/Application.cs) |
| FR-015 — CompanyName required | Compliant | [`CreateApplicationViewModel.cs`](../../src/FundingPlatform.Web/ViewModels/CreateApplicationViewModel.cs) + entity defence-in-depth |
| FR-016 — CompanyName ≤200, NOT NULL, trimmed | Compliant | [`Application.SetCompanyName`](../../src/FundingPlatform.Domain/Entities/Application.cs), [`dbo.Applications.sql:5`](../../src/FundingPlatform.Database/Tables/dbo.Applications.sql), [`ApplicationConfiguration.cs`](../../src/FundingPlatform.Infrastructure/Persistence/Configurations/ApplicationConfiguration.cs) |
| FR-017 — full pipeline replacement (no toggle) | Compliant | Old partials deleted; no version flag in [`Document.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Document.cshtml) |
| FR-018 — swap-one-file ergonomics | Compliant | Single `<img>` reference per asset in [`_BrandHeader.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_BrandHeader.cshtml) and [`_BrandFooter.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_BrandFooter.cshtml) |
| FR-019 — `FundingAgreement:Funder:*` keys removed | Compliant | [`AppHost.cs:106-107`](../../src/FundingPlatform.AppHost/AppHost.cs) (5 keys + WithEnvironment calls gone), [`DependencyInjection.cs:37`](../../src/FundingPlatform.Infrastructure/DependencyInjection.cs), [`CLAUDE.md`](../../CLAUDE.md) configuration-knobs row removed |
| FR-020 — funder DTO/options deleted | Compliant | `FunderOptions.cs` deleted; no residual `FunderOptions` symbol in `src/`. |
| FR-021 — applicant email/phone/legalId not rendered | Compliant | grep across [`Views/FundingAgreement/`](../../src/FundingPlatform.Web/Views/FundingAgreement) confirms zero references |
| FR-022 — agreement-reference identifier not rendered | Compliant | grep across [`Views/FundingAgreement/`](../../src/FundingPlatform.Web/Views/FundingAgreement) and view-models confirms zero references |
| FR-023 — legacy partials and CSS removed | Compliant | `_FundingAgreement{Header,ItemsTable,SignatureBlocks,TermsAndConditions}.cshtml` deleted; layout CSS replaced wholesale |
| FR-024 — R-005 placeholder banner retired | Compliant | `MARCADOR DE POSICIÓN` literal absent from `src/`; only the absence-assertion in [`FundingAgreementPdfDownloadTests`](../../tests/FundingPlatform.Tests.E2E/Tests/PdfTemplate/FundingAgreementPdfDownloadTests.cs) remains |

### Build / test status

- `dotnet build FundingPlatform.slnx` — **green**, 0 errors, 32 NU1902 OpenTelemetry vulnerability warnings inherited from earlier specs (out of scope for 018).
- `dotnet test tests/FundingPlatform.Tests.Unit --filter "ApplicationCompanyName|ItemLineCode"` — **19 passed**.
- `dotnet test tests/FundingPlatform.Tests.Integration --filter "CompanyName|LineCode"` — **12 passed**.
- E2E suite — compiles; full execution deferred to verify stage per pipeline contract.

### Verdict

**PASS** — spec compliance ≥ 95%, all auto-fixed Important findings resolved, build + targeted tests green. Hand off to verify stage.

---

## Code Review Guide (30 minutes)

> This section guides a code reviewer through the spec 018 implementation.
> Time-boxed at 30 minutes; question-driven; tries to surface the calls
> that need human judgment rather than dumping the compliance matrix.

**Changed files:** ~50 source files, 5 dacpac/cshtml/cs config files, 1 markdown ([`CLAUDE.md`](../../CLAUDE.md)). Headline scope: 8 new Razor partials + 1 layout rewrite + 2 entity invariants + 1 dacpac touch + 5 new test files.

### Understanding the changes (8 min)

Read in this order:

1. **Start with [`spec.md`](spec.md)** — the spec is short; the FR list is the ground truth. Pay attention to FR-012 phrasing ("approve or reject") and the assumption block.
2. **[`Application.cs`](../../src/FundingPlatform.Domain/Entities/Application.cs)** — `SetCompanyName` and `AssignLineCodeToItem` are the load-bearing entity invariants per [Constitution II](plan.md#constitution-check). Skim the `ValidationReasonKey` discriminator added during the auto-fix loop and the per-Application uniqueness check.
3. **[`FundingAgreementController.BuildDocumentViewModelAsync`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs)** (line 689) — the projection that feeds the new Razor partials. This is the largest single change and the place where every FR-006 / FR-008 / FR-009 / FR-010 mapping decision lives.
4. **[`_FundingAgreementLayout.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/_FundingAgreementLayout.cshtml)** — the print CSS. Header/footer pinning, `@page` margins, `<thead>` repeat rule, and signature box geometry.
5. **The 8 partials in [`Views/FundingAgreement/Partials/`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials)** — flat structure, one section each.

Question: does the [projection living in the Web controller](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs#L689) instead of [`FundingAgreementService`](../../src/FundingPlatform.Application/Services/FundingAgreementService.cs) (where [plan.md](plan.md) said it would land) bother anyone? It is the right behaviour but the wrong layer — the controller now owns currency formatting, summary-paragraph composition, and supplier dedupe.

### Key decisions that need your eyes (12 min)

**1. `RequestMoreInfo` bypasses LineCode** ([`ReviewService.cs:96-103`](../../src/FundingPlatform.Application/Services/ReviewService.cs), relates to [FR-012](spec.md#requirements))

The implementation requires a non-blank LineCode only for `Approve` and `Reject`. `RequestMoreInfo` is allowed to bounce an item without a code (rationale documented in [research.md R-008](research.md)). The spec's acceptance scenario 1 says "submit their decision (approve or reject)" so this is a defensible reading — but FR-012 itself is broader ("for each item before the per-item review decision can be persisted").
- Question: is the R-008 carve-out correct, or should `RequestMoreInfo` also require a LineCode?

**2. Sworn-declaration legal copy is hardcoded** ([`_SwornDeclarationPage.cshtml`](../../src/FundingPlatform.Web/Views/FundingAgreement/Partials/_SwornDeclarationPage.cshtml), relates to [FR-011](spec.md#requirements) and the [Open Clarification](spec.md#open-clarifications))

The PRIMERO–QUINTO clauses are baked into the Razor partial verbatim from the seed PDF. The spec [Open Clarification](spec.md#open-clarifications) flags this as `[NEEDS CLARIFICATION]` — Legal has not yet confirmed the seed copy is canonical.
- Question: do we ship under the spec assumption ("canonical until answered") or wait on Legal? If we ship and Legal revises, the clauses live in one Razor partial → cheap to swap.

**3. Header / footer offset math** ([`_FundingAgreementLayout.cshtml:80-109`](../../src/FundingPlatform.Web/Views/FundingAgreement/_FundingAgreementLayout.cshtml))

`#brand-header { top: -16mm; height: 18mm; }` with a 20mm top page margin. In Blink HTML→PDF, `position: fixed` on `top: -16mm` typically places the header into the reserved margin band, leaving a ~2mm visual band into the content area. The `height: 18mm` plus the gold-divider strip can put the header within ~2mm of the body content.
- Question: do we want a tighter band? Verify by eye against the seed under the SC-001 ±5pt tolerance.

**4. Acuerdo label shape** ([`FundingAgreementController.cs:724`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs))

`acuerdoLabel = $"FA-{application.Id}"` — used in approved/rejected line tables. The seed PDF uses `FI_SBDCR25-002`-style identifiers, which encode tract + sequence. A future `Tract` entity is explicitly [out of scope](spec.md#out-of-scope) for spec 018, but the chosen `FA-{id}` shape ignores the seed's pattern entirely.
- Question: is `FA-{id}` good enough as a v1 placeholder, or do we want a more seed-compatible format (e.g. `FI-SBDCR{yy}-{id:D3}`) until [Tract] lands?

**5. Validation discriminator via `ArgumentException.Data`** ([`Item.cs:260-`](../../src/FundingPlatform.Domain/Entities/Item.cs), [`Application.cs:54-`](../../src/FundingPlatform.Domain/Entities/Application.cs))

The auto-fix loop replaced fragile `ex.Message.Contains("16", ...)` mapping in `ReviewService` and `ApplicationService` with a stable `ex.Data["FundingPlatform.ValidationReason"]` discriminator the entity sets. Keeps Constitution II intact (entity owns the rule) but introduces a new convention for the codebase.
- Question: should this become a project-wide pattern for entity-to-application-error mapping (rather than per-feature ad-hoc)? Document in the constitution if yes.

### Areas where I'm less certain (5 min)

- [`LongTablePagebreakTests`](../../tests/FundingPlatform.Tests.Integration/FundingAgreement/LongTablePagebreakTests.cs) ([T021a](tasks.md#phase-3-user-story-1)): the original task description called for `pdftotext -layout` assertions over a 50-item rendered PDF to verify `position: fixed` headers repeat across page breaks. The shipped test only exercises the entity-side LineCode uniqueness loop 50× — it does NOT actually render a PDF or inspect pagebreaks. The auto-fix loop updated the class summary to honestly state the scope, but the substantive R-003 / Outstanding Risk #2 assertion remains uncovered until the E2E layer ([T022](tasks.md#phase-3-user-story-1)) runs.
- [`BuildDocumentViewModelAsync` line 727](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs): items are sorted by `LineCode ?? "￿"` (high-codepoint sentinel). Items in `RequestMoreInfo` state with a null LineCode would sort to the end. Given FR-012 is enforced for Approve/Reject, this is uncommon at PDF-generation time (the Generate gate requires a finalized review) but the sort could produce surprising orderings during preview rendering.
- The `commissionMembers` list display order is `StringComparer.CurrentCulture` ([`FundingAgreementController.cs:714`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs)) — for es-CR culture this sorts accent-aware. The seed shows Paola / Milena / Aldo in apparent insertion order, not alphabetical. SC-002 says "reproduces the seed verbatim" — small risk this fails on the seed scenario.
- [`BuildInlineErrorViewAsync`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs) (around line 874) re-runs `BuildDocumentViewModelAsync` on every inline-error path — so a missing-conversion preview pays the projection cost twice. Minor performance smell, not a correctness issue.

### Deviations and risks (5 min)

- [`BuildDocumentViewModelAsync`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs#L689) lives on the controller, not on [`FundingAgreementService`](../../src/FundingPlatform.Application/Services/FundingAgreementService.cs) where [plan.md](plan.md) placed it. Dependency direction is preserved (controller depends on `IUserStoreReader` which is fine), but a downstream caller wanting to reuse the projection (e.g. another report) cannot.
  - Question: should this be lifted to the Application layer in a follow-up, or is the controller-side mapping the new convention?
- [`tasks.md`](tasks.md) marks T036 (perf script), T051 (perf check), T052 (visual regression hex sampling), T053 (asset-swap smoke), T054 (manual quickstart pass) as `[ ]`. Polish-phase items deferred to verify / human stages. Acknowledged in the [pipeline contract](../../tasks.md) (T055 notes: "E2E suite compiles with new tests, full execution deferred to verify stage").
- [`CompanyNameRequiredTests`](../../tests/FundingPlatform.Tests.Integration/Applications/CompanyNameRequiredTests.cs) and [`LineCodeRequiredAndUniqueTests`](../../tests/FundingPlatform.Tests.Integration/Reviews/LineCodeRequiredAndUniqueTests.cs) use `UseInMemoryDatabase`, which contradicts the [project memory](../../CLAUDE.md) "Integration tests must hit a real DB, never mocks." The whole pre-existing integration suite already uses InMemory, so this is the established convention rather than a regression. Worth flagging for the broader testing-strategy review (out of scope here).
- The auto-fix loop produced one new commit-eligible diff (validation discriminator + test class summary fix). Stage and commit alongside the existing implementation commit, or treat as a follow-up — caller's call.

---

## Deep Review Report

**Date:** 2026-05-08
**Branch:** `018-pdf-template-lift`
**Implementation HEAD:** `2dd427a` (pre-auto-fix); auto-fix changes uncommitted at review time.
**Rounds:** 1 (no Critical findings; 2 Important auto-fixed; gate moved to PASS)
**Gate outcome:** PASS
**Invocation:** quality-gate (autonomous mode via `speckit.spex-gates.review-code` from `speckit-spex-ship`)

### Summary

| Severity  | Found | Fixed | Remaining |
|---|---|---|---|
| Critical  | 0     | 0     | 0     |
| Important | 2     | 2     | 0     |
| Minor     | 5     | 0     | 5     |
| **Total** | **7** | **2** | **5** |

**Internal review perspectives applied:** correctness, architecture, security, performance, testing/edge-cases (consolidated into a single autonomous pass — no parallel agent dispatch given autonomous-mode "return immediately" directive).
**External tools:** CodeRabbit — **skipped** (CLI not installed on this runner). Copilot — **skipped** (CLI not installed). The skip is a runner-environment limitation, not a config decision; both tools are enabled in [`deep-review-config.yml`](../../.specify/extensions/spex-deep-review/deep-review-config.yml) (`coderabbit: true`) and the review-code invocation explicitly requested both.

### Findings

#### FINDING-1 (auto-fixed)
- **Severity:** Important
- **Category:** correctness / fragility
- **File:** [`src/FundingPlatform.Application/Services/ReviewService.cs:119`](../../src/FundingPlatform.Application/Services/ReviewService.cs)
- **Resolution:** fixed (round 1)

**What was wrong:** the application-layer mapping from `ArgumentException` to `UserFacingErrorCode.LineCodeTooLong` vs. `LineCodeRequired` keyed on `ex.Message.Contains("16", StringComparison.Ordinal)`. The "16" is the spec-mandated max length, embedded in the entity's English exception message. If a developer rewrites the message (e.g. for clarity, or accidentally drops the literal "16"), the dispatch silently miscategorises `TooLong` as `Required` and the user sees the wrong Spanish error. Pre-existing project memory says "[NFR-001](../../CLAUDE.md) — all Application-layer code, logs, and exception messages stay English" but English is not contractual.

**Why it mattered:** silent regression risk on a user-facing error path. The mapping is the wrong place for a string-content sniff; the entity should expose a structured discriminator.

**Fix applied:** introduced [`Item.ValidationReasonKey`](../../src/FundingPlatform.Domain/Entities/Item.cs) (`const string`) and `LineCodeRequiredReason` / `LineCodeTooLongReason` discriminator constants. The entity now stamps `ex.Data[ValidationReasonKey] = <reason>` on each `ArgumentException` it throws. `ReviewService.ReviewItemAsync` reads the marker via a `switch` expression. Same shape applied to `Application.SetCompanyName` (FINDING-2 below).

#### FINDING-2 (auto-fixed)
- **Severity:** Important
- **Category:** correctness / fragility
- **File:** [`src/FundingPlatform.Application/Services/ApplicationService.cs:86`](../../src/FundingPlatform.Application/Services/ApplicationService.cs)
- **Resolution:** fixed (round 1)

**What was wrong:** identical pattern to FINDING-1: `ex.Message.Contains("200", StringComparison.Ordinal)` to discriminate `CompanyNameTooLong` from `CompanyNameRequired`.

**Fix applied:** [`Application.SetCompanyName`](../../src/FundingPlatform.Domain/Entities/Application.cs) now stamps `ex.Data[Item.ValidationReasonKey]` with `Application.CompanyNameRequiredReason` or `Application.CompanyNameTooLongReason`. `ApplicationService.CreateApplicationAsync` reads the marker and switches on it.

#### FINDING-3 (deferred — informational, not auto-fixed)
- **Severity:** Minor
- **Category:** test-quality / spec-compliance gap
- **File:** [`tests/FundingPlatform.Tests.Integration/FundingAgreement/LongTablePagebreakTests.cs`](../../tests/FundingPlatform.Tests.Integration/FundingAgreement/LongTablePagebreakTests.cs)
- **Resolution:** annotated; substantive coverage gap remains for verify stage

**What is wrong:** the test class advertises "long-table smoke test" with a 50-item Application "must span multiple pages" and "brand header / footer (CSS `position: fixed`) must repeat across page breaks". The actual test body only exercises [`Application.AssignLineCodeToItem`](../../src/FundingPlatform.Domain/Entities/Application.cs) 50 times and asserts entity-side cardinality — no PDF render, no `pdftotext -layout`, no pagebreak inspection. The substantive R-003 / Outstanding Risk #2 assertion (the original [T021a](tasks.md#phase-3-user-story-1) intent) is not covered.

**Why it matters:** if Blink's `position: fixed` rule silently fails to repeat on page-2..N, the regression slips through to production unless the E2E [`FundingAgreementPdfDownloadTests`](../../tests/FundingPlatform.Tests.E2E/Tests/PdfTemplate/FundingAgreementPdfDownloadTests.cs) catches it (and that test renders a single-item Application, so it cannot exercise long-table behaviour either).

**Fix applied (partial):** the auto-fix loop updated the class summary to honestly state the test scope, removing the misleading claim. The substantive assertion gap is left for the verify stage / a follow-up task.

#### FINDING-4 (deferred — minor)
- **Severity:** Minor
- **Category:** architecture / layering
- **File:** [`src/FundingPlatform.Web/Controllers/FundingAgreementController.cs:689-861`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs)
- **Resolution:** noted in Code Review Guide

**What is wrong:** [`BuildDocumentViewModelAsync`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs#L689), `ComposeApprovedSummaryParagraph`, and `BuildConversionNote` live on the Web controller. [plan.md](plan.md) explicitly states "Rewrite the document projection method in `src/FundingPlatform.Application/Services/FundingAgreementService.cs`". The shipped projection is in the wrong layer.

**Why it matters:** dependency direction is preserved (the controller depends on `IUserStoreReader` from the Application layer, which is fine), but the projection cannot be reused by another caller without copy-paste. The summary-paragraph composition and currency-format helpers are also testable units that now require a controller harness to exercise.

**Why deferred:** moving the projection is a larger refactor that risks breaking the inline-error reuse path ([`BuildInlineErrorViewAsync`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs#L874)) and the ApplicantResponse-aware preview gate. Out of auto-fix scope.

#### FINDING-5 (deferred — minor)
- **Severity:** Minor
- **Category:** correctness / spec-fidelity
- **File:** [`src/FundingPlatform.Web/Controllers/FundingAgreementController.cs:724`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs)
- **Resolution:** noted in Code Review Guide (decision #4)

**What is wrong:** `acuerdoLabel = $"FA-{application.Id}"` — surfaces in every approved/rejected/declaration table row. The seed PDF uses `FI_SBDCR25-002`-style codes encoding tract + sequence. SC-002 says "the generated PDF reproduces the seed verbatim (modulo content driven by genuine database values that match the seed dataset)". Whether `FA-{id}` qualifies as "genuine database value matching the seed dataset" is debatable.

**Why deferred:** the spec [explicitly excludes `Tract` entity introduction](spec.md#out-of-scope), so a true seed-compatible label is out of scope. A minor tweak (`FI-{date}-{id:D3}`) would improve seed fidelity without introducing the entity, but a human should pick the format.

#### FINDING-6 (deferred — minor)
- **Severity:** Minor
- **Category:** performance
- **File:** [`src/FundingPlatform.Web/Controllers/FundingAgreementController.cs:701-714`](../../src/FundingPlatform.Web/Controllers/FundingAgreementController.cs)
- **Resolution:** noted; relates to deferred [T036](tasks.md#phase-3-user-story-1) perf script

**What is wrong:** the commission-members hydration loops `await _userStoreReader.GetDisplayNameAsync(...)` sequentially per distinct reviewer. For an Application with N reviewers, this is N round-trips serialised on the request thread. Could be `Task.WhenAll`.

**Why deferred:** typical commission size is 1–4 reviewers (per spec acceptance scenarios). Net cost is tiny vs. the dominant Razor + Blink cost. Worth fixing if [SC-009](spec.md#measurable-outcomes) (3-second p95) regression shows up under [T036/T051](tasks.md#phase-6-polish--cross-cutting-concerns).

#### FINDING-7 (deferred — minor)
- **Severity:** Minor
- **Category:** test-quality
- **File:** [`tests/FundingPlatform.Tests.E2E/Tests/PdfTemplate/FundingAgreementPdfDownloadTests.cs`](../../tests/FundingPlatform.Tests.E2E/Tests/PdfTemplate/FundingAgreementPdfDownloadTests.cs)
- **Resolution:** noted; full E2E execution is the verify stage's responsibility

**What is wrong:** the test asserts the PDF text layer contains four section headings + the absence of `MARCADOR DE POSICIÓN`. It does not assert mixed-currency conversion notes appear (US1 acceptance scenario 5, [Edge Case "Mixed-currency"](spec.md#edge-cases)), nor does it verify the `Empresa solicitante` cover line carries the applicant-supplied CompanyName end-to-end (SC-012 covers this in [`CompanyNameApplicationFlowTests`](../../tests/FundingPlatform.Tests.E2E/Tests/Applications/CompanyNameApplicationFlowTests.cs) but that test only asserts persistence, not PDF cover rendering).

**Why deferred:** test coverage is "good enough for golden path"; the gaps are edge-case assertions appropriate for follow-up E2E hardening.

### Auto-fix loop summary

- **Round 1:** 2 Important findings auto-fixed (FINDING-1 and FINDING-2). Re-build green. Re-target tests green (19 unit + 12 integration spec-018 tests). 5 Minor findings retained for human review (no auto-fix performed; per smart-mode they require judgment to resolve).
- **Round 2:** not entered — gate flipped to PASS after Round 1 (Critical + Important == 0).
- **Diff produced by auto-fix:** uncommitted changes to 4 files:
  - [`src/FundingPlatform.Domain/Entities/Item.cs`](../../src/FundingPlatform.Domain/Entities/Item.cs) (+~20 lines: `ValidationReasonKey` constants, exception `Data` stamping)
  - [`src/FundingPlatform.Domain/Entities/Application.cs`](../../src/FundingPlatform.Domain/Entities/Application.cs) (+~15 lines: same pattern for CompanyName)
  - [`src/FundingPlatform.Application/Services/ApplicationService.cs`](../../src/FundingPlatform.Application/Services/ApplicationService.cs) (mapping path now reads `ex.Data` discriminator)
  - [`src/FundingPlatform.Application/Services/ReviewService.cs`](../../src/FundingPlatform.Application/Services/ReviewService.cs) (same)
  - [`tests/FundingPlatform.Tests.Integration/FundingAgreement/LongTablePagebreakTests.cs`](../../tests/FundingPlatform.Tests.Integration/FundingAgreement/LongTablePagebreakTests.cs) (class summary now honestly describes scope, no body change)

The caller (ship pipeline / verify stage) is responsible for committing or rolling back the auto-fix diff.
