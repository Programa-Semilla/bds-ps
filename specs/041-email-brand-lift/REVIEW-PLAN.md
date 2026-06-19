# Review Guide: ALIA Transactional Email Brand UI-Lift

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-19

---

## What This Spec Does

Every email the platform sends today shares a deliberately bare, text-only layout. This feature rebrands all of them into one Programa Semilla design system — logo header, brand-teal palette, a partner-logo footer strip, clearer call-to-action buttons — and adopts a polished Costa Rican-Spanish (voseo) voice that renames the product "ALIA" in body copy while keeping *Programa Semilla* as the institution. It also adds three emails the platform doesn't send yet.

**In scope:** a shared branded shell + reusable partials; rebrand of all ~20 outbox emails plus the identity, stage-reminder, and supplier templates; ALIA copy + support phone; and three new emails (an applicant "in review" notice, a password-changed confirmation, and a deferred-trigger "new company for review" stub). See [Scope](spec.md#scope).

**Out of scope:** the `From:` sender display config, the outbox/worker/allowlist *mechanics*, the PDF templates, and any non-email web UI. The boundary worth poking at: this is a **template + copy** change that reuses the existing delivery pipeline — is that the right line to draw, or should anything about delivery change too?

## Bigger Picture

This is the email counterpart to two shipped efforts: spec 037 (web brand alignment) and spec 016/018 (PDF template lift). It deliberately **reverses** an earlier decision: spec 021 (FR-023/NFR-001) made emails text-only with no inline images, for deliverability. The plan re-introduces hosted images, mitigated by an images-blocked-degradation requirement ([NFR-004](spec.md#non-functional-requirements)). The reviewer most familiar with why 021 went text-only is the right person to sanity-check that reversal.

Email HTML is its own dialect — table layout, inline CSS, no flexbox/grid, bulletproof (VML) buttons for Outlook. The plan commits to authoring this by hand (no MJML/build step) to honor the repo's "no new deps / no build step / vendored-only" posture. That's a maintainability trade-off worth a glance.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [research Decision 1](research.md#decision-1--single-design-system-shell-shared-by-both-render-paths) and the [contracts/email-design-system](contracts/email-design-system.md). The central architectural bet is that **both** delivery paths — the spec-021/028 outbox emails *and* the spec-021/033 direct-send identity/stage emails — render through **one** `_EmailLayout`, via a new shared `IEmailViewRenderer`.

- The direct-send emails today use plain-text token substitution, not Razor. Converting them to render Razor views (tasks [T017](tasks.md), [T018](tasks.md), [T030](tasks.md)) is real churn — is "one shared shell" worth changing how invitation/forgot-password/stage emails are produced, versus letting them keep token templates with duplicated brand HTML?
- Is hand-authored table/inline-CSS email HTML a reasonable long-term maintenance burden for this team, or would a build-step generator have paid off?

### Key decisions that need your eyes (12 min)

**Reversing the no-image rule** ([Overview](spec.md#overview), [NFR-004](spec.md#non-functional-requirements))
Emails go from text-only to hosted logos + a 5-logo partner strip on *every* message. Question: is a partner-logo strip on high-frequency transactional mail (e.g. every appeal message) the right call, or does it add weight/noise the reviewer would scope down to milestone emails only?

**"Solicitud en revisión" recipient = applicant only** ([T026](tasks.md), [research Decision 3](research.md#decision-3--fr-011-solicitud-en-revisión-is-a-real-non-redundant-outbox-event))
This event fires at the `Submitted→UnderReview` transition, which happens whenever *any* reviewer first opens the application. I set recipients to **applicant-only** (not the spec-028 "+admins" convention) specifically to avoid spamming admins on every routine reviewer page-open. Question: do you agree applicant-only is right, or do admins/reviewers want this signal?

**Firing a notification from a GET** ([T027](tasks.md))
The `Submitted→UnderReview` transition currently happens lazily inside `ReviewService.GetApplicationForReviewAsync` — a read that already has a write side-effect. The plan adds a `VersionHistory("StartReview")` row + an outbox enqueue there. Question: is hanging an email off a page-open transition acceptable, or should "review started" be an explicit reviewer action instead? (See [OQ-2](spec.md#open-questions).)

**Password-changed also fires on first invite-set** ([research Decision 4](research.md#decision-4--fr-012-tu-contraseña-fue-actualizada-is-a-direct-send-identity-email))
Because invite first-set flows through the same reset handler, a brand-new user setting their initial password will receive "Tu contraseña fue actualizada." I judged that acceptable (and security-useful) rather than thread a flag through. Question: is that confusing right after a "set your password" invite, or fine?

**FR-013 modeled as a notifier, not an outbox event** ([contracts/notification-events](contracts/notification-events.md))
The outbox is application-keyed (its dedup key needs an `ApplicationId`); a "new company" has none. So FR-013 mirrors spec-038's `IProviderCreatedNotifier` instead. The spec text was formally evolved on 2026-06-19 to model FR-013 as a notifier (see the [Evolution Log](spec.md#evolution-log)). Question: do you agree the notifier seam is the right home, or is there a reason to force this into the outbox anyway?

### Areas where I'm less certain (5 min)

- [contracts/email-design-system](contracts/email-design-system.md): I assumed the existing `wwwroot/lib/brand/partners-footer.png` shows the same 5 partners as the seed `Fooder-general.png`. [T001](tasks.md) verifies this; if they differ, a new asset is added. I have not visually confirmed the match.
- [data-model §6](data-model.md#6-versionhistory-row-existing-table--new-usage): I'm relying on the dedup index plus a state-change guard to prevent duplicate "in review" emails. If two reviewers open the same just-submitted application near-simultaneously, the guard + index should still yield one email — but that concurrency path deserves a skeptical read.
- [tasks T014–T016](tasks.md): each bundles ~7 templates into one task for parallelism. That's coarse — fine for a single implementer, but a reviewer may prefer finer-grained tasks for tracking.

### Risks and open questions (5 min)

- **[OQ-1](spec.md#open-questions):** the "nueva empresa para revisión" trigger + recipient are a genuine product decision, unresolved. US4 ships a render-tested stub with no live trigger. Is shipping a dormant template acceptable, or should US4 wait until OQ-1 is answered?
- **Dark mode ([NFR-005](spec.md#non-functional-requirements)):** validated only by manual inspection ([T039](tasks.md)) — there's no objective automated check. Acceptable for a visual property, but worth acknowledging.
- **Deliverability regression:** if hosted images or the heavier markup hurt inbox placement vs. the old text-only emails, would we know? There's no deliverability metric in scope — is that a gap given the 021 reversal?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [research](research.md).*
