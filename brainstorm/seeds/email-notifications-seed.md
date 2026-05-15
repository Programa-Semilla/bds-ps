# Email Notifications System (Brainstorm Session)

> **Context**: The system currently has **no notifications**. The first notification channel will be **email only**.  
> **Environments**: **Local development uses Mailtrap**; **all other environments use Mailgun**.  
> **Branding**: Emails must match the **look & feel** previously used for the **Agreement generation email**.
> **Branching**: Use 021 as consequetive, as parallel work already using 010.

---

## 1) Problem Statement (What we’re solving)

We need an email notification mechanism that informs the right stakeholders when **important workflow events** occur across multiple stages (e.g., application submitted, sent back to applicant, resubmitted, approved, etc.). The notification logic must respect role-based interest and participation rules, especially for admins.

Key requirement: **Admins only receive notifications if they have participated (now or in the past) in any stage of the relevant application/workflow.**

---

## 2) Goals and Non-Goals

### Goals
- Deliver **event-driven email notifications** for workflow/stage transitions.
- Ensure recipients are **correct, minimal, and explainable** (avoid spamming).
- Support **local Mailtrap** and **non-local Mailgun** seamlessly.
- Provide a consistent **email UI** aligned with the Agreement email branding.

### Non-Goals (for now)
- In-app notifications (bell, toast, feeds).
- SMS / Push / Slack.
- End-user notification preference UI (unless already exists).
- Advanced digests, batching, ML-based “importance”.

---

## 3) Actors and Recipient Rules

### Primary roles
- **Applicant**: submits and responds to reviewer feedback.
- **Reviewer** (member of a reviewer group): reviews applications at certain stages.
- **Admin**: elevated role with visibility, but notifications must follow participation constraint.

### Recipient selection rules (draft)
1. **Stage reviewers**:  
   - When an event affects a stage that has an assigned reviewer group, **all reviewers in that group** receive a notification.
2. **Applicant**:  
   - Receives notifications for events that require action or reflect status changes relevant to them (e.g., “Returned for changes”, “Approved”, “Rejected”, “Need more info”).
3. **Admins**:  
   - Receive notifications **only if** they **have participated** in the application’s history (any stage) as reviewer/approver/commenter/assignee (define participation precisely).
4. **De-duplication**:
   - If a user belongs to multiple recipient buckets for the same event, send only **one** email.
5. **Opt-outs / preferences**:
   - Not in scope unless already present. If already present, apply them consistently.

**Brainstorm question**: Is “admin participation” defined as *ever assigned*, *ever commented*, *ever changed status*, or *ever viewed*?  
Recommendation: define participation by **explicit actions** (assigned / reviewed / approved / commented / changed stage) rather than passive views.

---

## 4) Important Events to Notify (Candidate Event Catalog)

Below is a proposed initial catalog. Each event should have:
- **Trigger** (what action/transition)
- **Primary recipients** (who needs to know)
- **Call to action** (what should they do next)
- **Deep link** to relevant UI entity

### Stage: Application Intake / New Submission
- **Event**: `APPLICATION_SUBMITTED`
  - Trigger: new application created/submitted
  - Recipients: reviewers of intake stage group; admins who participated (likely none on day 0); optionally applicant gets confirmation
  - CTA: “Review application”
  - Notes: include applicant name/id, submission timestamp, and summary fields

### Stage: Reviewer Requests Changes / Returns to Applicant
- **Event**: `RETURNED_TO_APPLICANT`
  - Trigger: reviewer returns application with requested changes
  - Recipients: applicant; participating admins
  - CTA: “Update and resubmit”
  - Notes: include a short reason snippet + link to full comments

### Stage: Applicant Resubmits / Replies
- **Event**: `RESUBMITTED_BY_APPLICANT`
  - Trigger: applicant resubmits after changes
  - Recipients: reviewers of the next/relevant stage group; participating admins
  - CTA: “Review updated submission”
  - Notes: highlight what changed if possible, else include “changes available in app”

### Stage: Reviewer Approves / Advances Stage
- **Event**: `STAGE_APPROVED` / `MOVED_TO_NEXT_STAGE`
  - Trigger: stage decision recorded and/or stage advanced
  - Recipients: reviewers of the next stage group; applicant (status update); participating admins
  - CTA: reviewers: “Start next stage review”; applicant: “Track status”

### Stage: Final Decision
- **Event**: `APPLICATION_APPROVED`
  - Trigger: final approval
  - Recipients: applicant; participating admins; internal stakeholders if needed
  - CTA: applicant: “Next steps”
- **Event**: `APPLICATION_REJECTED`
  - Trigger: final rejection
  - Recipients: applicant; participating admins
  - CTA: applicant: “View decision details”

### Stage: Reviewer Assignment Changes
- **Event**: `REVIEWER_ASSIGNED` / `REVIEWER_UNASSIGNED`
  - Trigger: assignment updates
  - Recipients: affected reviewers; participating admins
  - CTA: “Open assigned application”
  - Notes: reduces “I didn’t know it was mine” cases

### Comments / Mentions (Optional but high leverage)
- **Event**: `COMMENT_ADDED`
  - Trigger: comment posted
  - Recipients: @mentioned users; assigned reviewer; applicant if visible to them; participating admins
  - CTA: “Reply”

---

## 5) Workflow-to-Email Mapping (Example)

**Example**: “New application arrives”  
- Trigger: application enters stage `INTAKE_REVIEW`
- Recipients: reviewers in intake group + participating admins  
- Email subject: “New application ready for review: {ApplicantName}”
- Body: summary + stage name + link

**Example**: “Reviewer returns to applicant”  
- Trigger: stage status changes to `NEEDS_CHANGES` and ownership returns to applicant  
- Recipients: applicant + participating admins  
- Subject: “Action required: update your application”
- Body: what to do + due date (if exists) + link

---

## 6) Email Content Requirements (Look & Feel)

**Constraint**: Must match the Agreement email styling.

### Practical interpretation
- Use the same:
  - Header / logo placement
  - Typography scale
  - Button style (primary CTA button)
  - Footer structure (support email, legal text)
  - Spacing and “card-like” layout

### Email structure template
- **Header**: brand + short title
- **Summary line**: what happened
- **Context block**: application identifier, stage, actor, timestamp
- **Primary CTA button**: “Open application”
- **Secondary info**: reason/comments snippet if applicable
- **Footer**: standard links + “You’re receiving this because …”

**Deliverable suggestion**: copy the Agreement email HTML as a base and parameterize:
- title
- summary
- context rows
- CTA label + URL
- optional comment section

---

## 7) Technical Architecture (Brainstorm)

### A) Event-driven approach (recommended)
- Emit domain events when:
  - stage transitions occur
  - submission/resubmission occurs
  - decisions are recorded
- Notification service consumes events and:
  - determines recipients
  - selects template
  - sends via provider
  - logs delivery result

**Pros**: decoupled, testable, extensible  
**Cons**: introduces event plumbing + idempotency requirements

### B) “Inline send” inside stage transition code (fast but risky)
- When stage status changes, directly call email sender.

**Pros**: quick to ship  
**Cons**: tangles business logic, hard to evolve, poor retry story

### C) Hybrid
- Inline enqueue (transactional outbox) + async worker does send.

**Pros**: reliability + transactional safety  
**Cons**: more components

**Recommendation**: Hybrid with **transactional outbox** if you have any reliability constraints (and you probably do in fintech).

---

## 8) Provider Strategy (Mailtrap vs Mailgun)

### Environment routing
- `LOCAL` → Mailtrap (SMTP or API)
- `DEV/QA/STAGING/PROD` → Mailgun (API)

### Guardrails
- Prevent accidental sending to real users from non-prod:
  - allowlist domains in dev/staging, **or**
  - prefix subjects with “[STAGING]”, **or**
  - route to a catch-all recipient list in non-prod

---

## 9) Data Model / Persistence (for audit + dedupe)

You will regret not doing minimal persistence.

### Minimum recommended tables
- `notification_event`
  - id, event_type, entity_id (application_id), payload_json, created_at
- `notification_delivery`
  - id, event_id, recipient_user_id/email, provider, status, provider_message_id, attempts, last_error, sent_at

### Why this matters
- Dedupe: avoid resending on retries
- Audit: “who got notified and why”
- Debugging: “Mailgun accepted but user claims no email”

---

## 10) Idempotency and Retry Strategy

Notifications are the classic “double send” trap.

- Use a deterministic idempotency key:
  - e.g., `{event_type}:{application_id}:{stage_id}:{version}` or event UUID
- Store deliveries and do **at-least-once** sending with idempotency guard.
- Retry on transient failures (provider timeouts, 5xx) with backoff.
- Do not retry hard bounces.

---

## 11) Security, Privacy, and Content Risk

Email is leaky. Treat it accordingly.

- Do not include sensitive PII beyond what is necessary.
- Avoid embedding internal-only comments if applicant emails could receive them.
- Keep “reason snippets” short and ensure they’re safe for email.

---

## 12) Metrics (Don’t fly blind)

At minimum:
- sends attempted
- sends succeeded
- failures by reason/provider
- bounce rate (Mailgun)
- time-to-send (from event creation)

---

## 13) Open Questions (Decisions to force)

1. **Admin participation**: what counts as “participated”?
2. Should applicants get a confirmation on initial submission?
3. Should reviewers get notified on every micro-change, or only on “action-required” transitions?
4. What is the canonical deep link URL format and access control expectations?
5. Non-prod safety: allowlist or catch-all?

---

## 14) Next Concrete Steps (Action Plan)

1. Define event catalog v1 (3–6 core events max).
2. Define recipient rules precisely (especially admin participation).
3. Implement provider abstraction with environment routing (Mailtrap/Mailgun).
4. Build one email template base derived from Agreement email.
5. Ship v1 with logging + idempotency + minimal persistence.

---

## 15) Red-Team Risks (What will bite you)

- You will spam people if “important event” is not tightly defined.
- Admin participation rule will be misinterpreted without an explicit definition.
- Without an outbox/idempotency, you will double-send on retries.
- Without non-prod guardrails, you will email real users from staging.
- Without persistence, you will have no answer when leadership asks “who got what email?”

---

## 16) If this were my call (Strong stance)

Ship a **small v1**:
- Events: `APPLICATION_SUBMITTED`, `RETURNED_TO_APPLICANT`, `RESUBMITTED_BY_APPLICANT`, `APPLICATION_APPROVED/REJECTED`
- Provider: Mailtrap local, Mailgun elsewhere
- Architecture: outbox + worker
- Email template: Agreement email as base

Everything else can iterate.
