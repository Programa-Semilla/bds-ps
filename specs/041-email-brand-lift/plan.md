# Implementation Plan: ALIA Transactional Email Brand UI-Lift

**Branch**: `041-email-brand-lift` | **Date**: 2026-06-19 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/041-email-brand-lift/spec.md`

## Summary

Lift every transactional email into a single Programa Semilla brand design system and adopt the "ALIA" voseo copy. Approach: rebuild the shared Razor `_EmailLayout` into a 600px, table-based, inline-CSS branded shell plus a small set of reusable partials (brand header, partner footer, CTA button, status/info card, detail list, hero), and route **both** delivery paths — the spec-021/028 outbox emails and the spec-021/033 direct-send identity/stage emails — through that one shell so the brand has a single source of truth (research Decision 1). Brand images are referenced by absolute URL composed from the existing `Notifications:BaseUrl` against the official assets already in `wwwroot/lib/brand/` (Decision 2). Three new emails are added: an applicant "Solicitud en revisión" outbox event at the real `Submitted→UnderReview` transition (Decision 3), a direct-send "Tu contraseña fue actualizada" identity email (Decision 4), and a branded "Nueva empresa para revisión" template + notifier seam whose live trigger is deferred to OQ-1 (Decision 5). No schema change and no new dependencies.

## Technical Context

**Language/Version**: C# / .NET 10.0, ASP.NET MVC, Razor
**Primary Dependencies**: existing only — MailKit (SMTP), Mailgun HTTP (vendored client), Razor view engine, Aspire smtp4dev sidecar (E2E). No new managed deps (NFR-006).
**Storage**: SQL Server via dacpac. **No schema change** — new outbox event is string-stored; password-changed + FR-013 are not outbox events; `VersionHistory` table already exists.
**Testing**: Unit (NUnit), Integration (real DB), E2E (Playwright + `MailCaptureClient`/smtp4dev).
**Target Platform**: Linux server (Aspire-orchestrated).
**Project Type**: Web application (server-rendered MVC).
**Performance Goals**: N/A (email rendering is background/best-effort; no new hot path).
**Constraints**: Email-client compatibility — table layout, inline CSS, ≤600px, no flexbox/grid, no external CSS; legible with images blocked; WCAG AA text contrast; es-CR (NFR-001..007).
**Scale/Scope**: ~20 outbox HTML bodies + ~20 text twins, ~6 direct-send identity/stage templates, 1 layout rebuild + ~6 new partials, 1 new outbox event (fully wired + tests), 1 new direct-send email + wiring, 1 deferred-trigger stub template + notifier seam.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| **I. Clean Architecture** | PASS. `NotificationEvent` (Domain), `NotificationTemplateBindings` (Application), renderer/factories/resolver (Infrastructure + Web Services), views (Web). New event respects inward dependencies. The notifier seam for FR-013 mirrors `IProviderCreatedNotifier` (Application interface / Infrastructure impl). |
| **II. Rich Domain Model** | PASS. FR-011 reuses the existing `Application.StartReview()` domain transition; no business rule moves into services. The `VersionHistory` add stays in `ReviewService` alongside the other transition records (existing pattern). |
| **III. E2E (NON-NEGOTIABLE)** | PASS. Filtered mail-capture E2E for the two new sendable emails + a representative redesigned-event sweep; FR-013 template render-tested (no live trigger). |
| **IV. Schema-First (dacpac)** | PASS — **zero schema change**. String-stored event (spec-028 pattern); no EF migration; `VersionHistory` reused. |
| **V. Specification-Driven** | PASS. spec → plan → tasks → implement; stories independently testable. |
| **VI. Simplicity / YAGNI** | PASS. Reuses shell/partials/outbox/direct-send/notifier patterns; defers FR-013's trigger; no new deps; no build step. |

**Result**: PASS, no violations. Complexity Tracking not required.

**Post-Phase-1 re-check**: still PASS — Phase 1 introduces no new projects, dependencies, or schema; the only structural change is extracting a shared `IEmailViewRenderer` (a generalization of code already in `RazorEmailRenderer`) and new Razor partials.

## Project Structure

### Documentation (this feature)

```text
specs/041-email-brand-lift/
├── spec.md              # Requirements (done)
├── REVIEW-SPEC.md       # Spec review: SOUND (done)
├── checklists/requirements.md
├── plan.md              # This file
├── research.md          # Phase 0 (done)
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/           # Phase 1
│   ├── email-design-system.md
│   ├── notification-events.md
│   └── identity-emails.md
└── tasks.md             # Phase 2 (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/FundingPlatform.Domain/
└── Notifications/NotificationEvent.cs                 # + ApplicationUnderReviewApplicant (enum + storage maps)

src/FundingPlatform.Application/
├── Notifications/Templates/NotificationTemplateBindings.cs   # + binding for the new event
└── Suppliers/Notifications/IProviderCreatedNotifier.cs       # pattern mirror for FR-013 notifier seam (new interface alongside)

src/FundingPlatform.Infrastructure/
├── Notifications/Resolvers/NotificationRecipientResolver.cs  # + bucket switches for the new event
├── Email/PasswordChangedEmailFactory.cs                      # NEW (mirrors ForgotPasswordEmailFactory)
├── Email/EmailViewRenderer.cs (or generalize RazorEmailRenderer) # shared Razor-view-to-string used by direct-send
└── Suppliers/ (or Notifications/) CompanyForReviewNotifier.cs # NEW stub notifier (FR-013), no live trigger

src/FundingPlatform.Application/Services/ReviewService.cs     # enqueue ApplicationUnderReviewApplicant at StartReview transition (+ VersionHistory row)

src/FundingPlatform.Web/
├── Services/RazorEmailRenderer.cs                            # + LogoUrl/PartnerStripUrl on EmailRenderModel; expose IEmailViewRenderer
├── Controllers/AccountController.cs                          # send password-changed at ChangePassword/ProfileChangePassword success
├── (Infrastructure) ConsumePasswordResetTokenHandler.cs      # send password-changed at reset success
└── Views/Emails/
    ├── _EmailLayout.cshtml         # REBUILT branded shell
    ├── _ViewImports.cshtml         # (model/namespace tweaks if needed)
    ├── Shared/_BrandHeader.cshtml  # NEW partial
    ├── Shared/_PartnerFooter.cshtml# NEW partial (replaces/extends _SupportFooter)
    ├── Shared/_CtaButton.cshtml    # NEW partial (bulletproof button + fallback link)
    ├── Shared/_StatusCard.cshtml   # NEW partial ("Detalle" card)
    ├── Shared/_DetailList.cshtml   # NEW partial (key/value)
    ├── Shared/_Hero.cshtml         # NEW partial (title block)
    ├── *.cshtml + *.text.cshtml    # ~20 outbox bodies refactored to compose partials + ALIA copy
    ├── Identity/InvitationEmail.cshtml, ForgotPasswordEmail.cshtml  # rebranded via shared shell
    ├── Identity/PasswordChangedEmail.cshtml + .text  # NEW
    ├── Stages/{T24,T72}ReminderEmail.cshtml, ExpiredEmail.cshtml    # rebranded via shared shell
    ├── Suppliers/ProviderCreatedAuditor.cshtml + .text             # rebranded
    └── Suppliers/CompanyForReviewAuditor.cshtml + .text            # NEW stub (FR-013), render-tested only

tests/
├── FundingPlatform.Tests.Unit/Notifications/        # binding totality (auto), storage-string, copy guards
├── FundingPlatform.Tests.Integration/Notifications/ # new-event recipient matrix + idempotency; password-changed send
└── FundingPlatform.Tests.E2E/Notifications/         # mail-capture: under-review + password-changed; redesigned-event sweep + render-test stub
```

**Structure Decision**: Existing 4-layer solution; no new projects. The one new cross-cutting seam is a shared `IEmailViewRenderer` (generalization of the Razor-view-to-string method already inside `RazorEmailRenderer`) so direct-send identity/stage emails render through the same `_EmailLayout` as the outbox emails — the mechanism that makes "one design system" real.

## Design overview (Phase 1 pointers)

1. **Design system** (US1, P1): rebuild `_EmailLayout` + partials; convert the ~20 outbox bodies to compose `_Hero`/`_StatusCard`/`_DetailList`/`_CtaButton`; rebrand identity/stage/supplier templates by routing them through the shared shell. Brand image URLs from `Notifications:BaseUrl`. ALIA copy + voseo + "Equipo Programa Semilla" sign-off + partner footer with phone. Plain-text twins updated in lockstep. This story is independently shippable and is the bulk of the value.
2. **Under-review email** (US2, P2): enum + storage map + binding + recipient buckets + enqueue (with `VersionHistory("StartReview")`) in `ReviewService`; HTML + text views; integration + E2E.
3. **Password-changed email** (US3, P2): `PasswordChangedEmailFactory` + view (shared shell) + send at the three success points; E2E via reset flow.
4. **Company-for-review stub** (US4, P3): branded template + notifier interface/impl seam, no live trigger (OQ-1); render-test only.

See `contracts/` for the design-system component contract, the event-catalog addition, and the identity-email render contract; `data-model.md` for entities/payload; `quickstart.md` for verification steps.

## Complexity Tracking

> No constitution violations — table intentionally empty.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| — | — | — |
