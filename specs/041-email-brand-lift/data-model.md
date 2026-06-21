# Phase 1 Data Model: ALIA Transactional Email Brand UI-Lift

**Feature**: 041-email-brand-lift · **Date**: 2026-06-19

This feature is template/copy-centric. There is **no database schema change** (Constitution IV satisfied trivially). The "entities" below are code-level constructs and one new string-stored event value.

## 1. NotificationEvent (Domain enum) — additive

`src/FundingPlatform.Domain/Notifications/NotificationEvent.cs`

| New member | Ordinal | Storage string | Notes |
|---|---|---|---|
| `ApplicationUnderReviewApplicant` | next free (20) | `APPLICATION_UNDER_REVIEW_APPLICANT` | Applicant learns a reviewer began review. Ordinal cosmetic; storage string is the durable form. Add to both `ToStorageString` and `FromStorageString`. |

No member is added for "nueva empresa para revisión" — that email is a **notifier**, not an outbox event (it has no `ApplicationId`; see §4).

## 2. NotificationPayload — unchanged

`NotificationPayload(ApplicationId, ApplicantUserId, ApplicantDisplayName, StageGroupIds, OutcomeCode?, ActorUserId?)` is sufficient for the under-review event (application-keyed, applicant recipient, reviewer as actor). **No new fields.**

## 3. NotificationTemplateBindings entry — additive

`src/FundingPlatform.Application/Notifications/Templates/NotificationTemplateBindings.cs`

```
[ApplicationUnderReviewApplicant] = new(
    Event:              ApplicationUnderReviewApplicant,
    SubjectTemplate:    "Tu solicitud está en revisión — Solicitud #{ApplicationId}",
    HtmlViewName:       "ApplicationUnderReviewApplicant",
    TextViewName:       "ApplicationUnderReviewApplicant.text",
    TemplateVariantKey: "applicant-under-review",
    CtaRouteTemplate:   "/Application/Details/{id}")
```

Recipient buckets (`NotificationRecipientResolver`): `IncludesApplicantBucket → true`, `IncludesReviewerBucket → false`, `IncludesAdminBucket → true` (default; confirm applicant-only vs +admin in tasks — research Decision 3).

## 4. Email render models

### 4a. EmailRenderModel (outbox) — additive fields
`src/FundingPlatform.Web/Services/RazorEmailRenderer.cs`

Add brand-image URLs so templates/partials never hard-code hosts:
```
EmailRenderModel(EventType, Recipient, Payload, Subject, CtaUrl, SenderName, SenderEmail,
                 string LogoUrl, string PartnerStripUrl)   // NEW two fields
```
Composed in the renderer via `Combine(baseUrl, "/lib/brand/programa-semilla-horizontal.png")` and `Combine(baseUrl, "/lib/brand/partners-footer.png")`.

### 4b. Identity/stage email view model (NEW, small record)
For the direct-send emails now rendered through `_EmailLayout`:
```
IdentityEmailModel(string DisplayName, string Subject, string? CtaUrl, string? CtaLabel,
                   string? ExpiresAtLocal, string LogoUrl, string PartnerStripUrl,
                   /* per-email extras as needed */)
```
Built by each factory; passed to `IEmailViewRenderer.RenderViewAsync`.

## 5. Brand asset references (no new files expected)

| Logical asset | Source path (served anonymously) | Used by |
|---|---|---|
| Header logo | `wwwroot/lib/brand/programa-semilla-horizontal.png` | `_BrandHeader` |
| Partner strip | `wwwroot/lib/brand/partners-footer.png` | `_PartnerFooter` |

Implementation-time check: verify `partners-footer.png` matches the 5-partner set in `seeds/emails/Fooder-general.png`; if not, add `wwwroot/lib/brand/email-partners-footer.png` and reference it (the only possible new asset).

## 6. VersionHistory row (existing table) — new usage

The `Submitted→UnderReview` transition adds a `VersionHistory(reviewerUserId, "StartReview", …)` row (action code already known to `StageMappingProvider`/`ActivityActionCopy`). Its `Id` supplies the `VersionHistoryId` component of the outbox dedup key `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)`. No schema change; mirrors how `SendBack`/`Finalize`/`ReviewItem` already write `VersionHistory` rows in `ReviewService`.

## 7. State / transition impact

No new application state. The under-review email hangs off the **existing** `Submitted → UnderReview` transition (`Application.StartReview()`), enqueued only when the state actually changes (guarded by the existing `Submitted` precondition; dedup index is the backstop against a reviewer re-opening the page).
