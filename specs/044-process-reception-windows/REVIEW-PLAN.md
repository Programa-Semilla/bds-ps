# Review Guide: Fund Process Reception Windows + Applicant Timing UX

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-22

---

## What This Spec Does

Fund administrators will configure absolute-date "reception windows" on a Process (e.g. *Mar 1–Jun 1* and *Aug 1–Sep 1*), and applicants will only be able to submit during those windows — with a clear reason whenever they can't, plus a countdown/notice telling them when submission opens or closes. This replaces today's "you have N days after starting a draft" model with a fixed-calendar model the client explicitly asked for. It's feedback-3 Slice E (the next slice after the shipped A–D).

**In scope:** admin window CRUD ([US1](spec.md#user-story-1---admin-configures-reception-windows-priority-p1)); submission gating in Costa Rica time ([US2](spec.md#user-story-2---submission-gated-by-reception-windows-priority-p1)); applicant countdown/notice ([US3](spec.md#user-story-3---applicant-timing-notices--countdown-priority-p1)); a guard against starting drafts that can never be submitted ([US4](spec.md#user-story-4---draft-creation-guarded-against-dead-ends-priority-p2)); storing windows as general `ProcessEvent`s for future calendar items ([US5](spec.md#user-story-5---future-proof-event-model-priority-p3-schema-only)).

**Out of scope:** the reviewer (Revisión) and signing (Facturación) stage timers stay exactly as they are; informational/deadline/milestone events get a schema slot but no behavior; per-user funding limits (Slice F) and applicant timeline/% (Slice G) are separate; no per-fund timezones. The most reviewable boundary is probably **dropping the global "process period"** — see below.

## Bigger Picture

This is a deliberate divergence from the written requirement. The source doc ([decomposition §3.2](../../seeds/feedback-3/AI_Coding_Agent_Unified_Requirements.md)) describes a Process as having a *global start/end* plus windows inside it; during brainstorming the user called that a misreception and cut it — the Process is now defined *only* by its windows. The plan also rips out a real piece of the current system: the `SolicitudWindowDays` duration gate, which today lives in three places (submit, autosave, stage-expiry). Getting the removal right matters because it touches the hot submission path that every prior application spec depends on. Timezone context for reviewers: Costa Rica is UTC−6 **with no daylight saving**, which is why the plan can treat gating as a pure UTC instant comparison and confine timezone math to admin input/display only ([research D1](research.md#d1--timezone-handling-reduces-to-utc-instant-comparison-for-gating)).

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read the [spec summary](spec.md#summary) and [research D1–D4](research.md#d1--timezone-handling-reduces-to-utc-instant-comparison-for-gating). As you read, consider:

- Is "a Process is *only* its reception windows, with no global envelope" the right call, or will operations miss being able to state an overall process span? (The plan can derive a span from min/max windows if needed — is that enough?)
- The gate is a pure UTC instant comparison, with CR time used only at input/display. Does that division feel correct, or is there a scenario where the *comparison itself* needs a timezone?
- Submission was previously bounded by "N days after you started the draft." After this change there is **no** upper bound on how long a draft can sit before submitting (only the window matters). Is removing that duration ceiling acceptable?

### Key decisions that need your eyes (12 min)

**No-windows-means-open** ([FR-007](spec.md#submission-gating-us2), [research D3](research.md#d3--submission-gate-pure-domain-evaluation-enforced-in-the-handler))
Chosen for backward compatibility — every existing Process has zero windows and must keep working. The alternative (mandatory windows + a backfill) was rejected.
- Question: is "no windows = unrestricted" ever surprising to an admin who simply *forgot* to add a window? Should the admin UI nudge when a Process has none?

**Removing `SolicitudWindowDays` now** ([research D4](research.md#d4--remove-the-solicitud-duration-gate-from-both-submit-and-autosave)/[D5](research.md#d5--drop-solicitudwindowdays-via-the-established-column-drop-pattern), tasks [T009–T014](tasks.md#phase-2-foundational-blocking-prerequisites))
The column + its submit/autosave/stage-expiry consumers are deleted, not left dormant.
- Question: the planning sweep found the autosave handler *also* enforced this window — removing it makes draft editing always-allowed ([FR-015](spec.md#draft-creation-guard-us4)). Is "edit a draft anytime, even outside windows" definitely the intended behavior?

**Inclusivity = start-inclusive / end-exclusive with an explicit close time** ([Assumptions](spec.md#assumptions), [SC-002](spec.md#measurable-outcomes))
The admin enters a precise closing instant; the UI shows date **and** time so "through June 1" can't be misread.
- Question: is forcing admins to think in exact close-instants (rather than "the whole of June 1") a usability cost worth the disambiguation?

**Audit routed under the `process.` prefix** ([research D7](research.md#d7--admin-crud-mirrors-the-process-servicecontroller-pattern))
Window CRUD audits as `process.reception_window.*` to avoid adding a new audit target type.
- Question: is folding window events under the Process audit target the right granularity, or should reception windows be their own auditable target?

### Areas where I'm less certain (5 min)

- [FR-017](spec.md#data-model-us5) (non-retroactivity): structurally guaranteed (the gate runs only at submit time), now also pinned by an explicit integration assertion in [T028](tasks.md#phase-4-user-story-2--submission-gated-by-reception-windows-priority-p1) (submit → deactivate/delete window → submitted application unchanged). Worth a glance that the assertion captures your intent.
- [research D8](research.md#d8--applicant-notice-replaces-the-solicitud-countdown-on-the-draft-editor): I assumed the Solicitud `_StageCountdownBanner` on the draft editor should be fully *replaced* by the new notice. If anyone still relies on that Solicitud banner, this removes it — did I read the intent right?
- "Live countdown" ([FR-011](spec.md#applicant-notices--countdown-us3)): I left client-side ticking vs. server-rendered-only as an implementation choice ([T033](tasks.md#phase-5-user-story-3--applicant-timing-notices--countdown-priority-p1)). The data is fully server-pinned, but if the client doesn't tick, "open" pages go stale until reload. Is a static remaining-time acceptable for v1?

### Risks and open questions (5 min)

- The biggest blast radius is the legacy-gate removal: if any consumer of `SolicitudWindowDays` was missed, submission breaks. The sweep found only submit/autosave/stage-expiry/tests ([research D5](research.md#d5--drop-solicitudwindowdays-via-the-established-column-drop-pattern)) — is that exhaustive enough to drop the column in the same slice, or should the column be left dormant for one release?
- The `ProcessEventType` TINYINT must map `HasConversion<byte>()` ([T008](tasks.md#phase-2-foundational-blocking-prerequisites)) — prior specs (035/040) shipped this bug because EF-InMemory hid it and only E2E caught it. Are the planned integration/E2E tests (real SQL) sufficient to catch a regression of that exact failure?
- E2E seeds windows relative to real `UtcNow` rather than freezing the clock ([research D2](research.md#d2--time-source-reuses-the-existing-istageexpiryclock)). Does avoiding clock control in E2E risk flakiness near boundaries, or is pushing the exact-second case down to unit/integration ([T006](tasks.md#phase-2-foundational-blocking-prerequisites)/[T028](tasks.md#phase-4-user-story-2--submission-gated-by-reception-windows-priority-p1)) the right split?

---
*Full context in linked [spec](spec.md), [plan](plan.md), [research](research.md), and [tasks](tasks.md).*
