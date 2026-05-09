# (HISTORICAL — see /BRAND-VOICE.md)

# Brand Voice — Forge

**Spec**: 011-warm-modern-facelift
**Status**: Draft (placeholder name *Forge* until FR-072 sign-off)
**Source**: research.md §1.5

This is the binding voice guide for every user-facing string in the swept views.
Every copy decision in spec 011 references back to one of the rules below. Voice-guide
compliance is one of the seven swept criteria (FR-017 #4, SC-019).

---

## Tone

Warm, plain, confident. Never corporate-jargony. Sound like a senior peer who is on
the entrepreneur's side and respects their time.

- **Warm**: Acknowledge the human moment when there is one (welcome strip, ceremony hero, awaiting-action callout). Don't fake warmth in transactional contexts — KPI labels stay terse.
- **Plain**: Prefer the everyday word. *Send*, not *Submit*. *We made a decision*, not *A decision has been issued*.
- **Confident**: Make claims, not hedges. *We'll email you when funds clear*, not *You will be notified at some point*.

## Person

- **You / your** addresses the user. Never *the user*, *the applicant*, *the entrepreneur* in microcopy.
- **We / we'll / our** describes the platform when describing platform behavior. *We'll send you...*. Avoid *the system* / *the platform* in microcopy.
- Third person is allowed in headings and labels for *other* people in the flow (e.g., *Reviewer notes*, *Funder signature recorded*).

## Stage-aware copy patterns

Each lifecycle stage has a tone register:

| Stage | Register | Examples |
|-------|----------|----------|
| Draft | Encouraging | *Tell us about your project.* / *Save and keep going.* |
| Submitted | Reassuring | *We've received your application.* / *We'll review it within {sla}.* |
| Under Review | Factual | *Your application is being reviewed.* / *We're checking the details.* |
| Decision | Clear-eyed | *We've made a decision.* / *We've decided to fund this.* / *We've decided not to fund this. Here's why.* |
| Sent back | Constructive | *We need a few more details before we can decide.* — never *Your application was incomplete.* |
| Agreement Generated | Action-forward | *Your funding agreement is ready to sign.* |
| Signed | Celebratory but dignified | *You're signed.* / *Your funding is locked in.* (the only place exclamation marks are permitted is the locked-in headline.) |
| Funded | Confirming | *Funds were transferred on {date}.* |

## Banned constructs

- **ALL CAPS shouting** — never. Title-case headings only. (`SUBMIT YOUR APPLICATION` → `Send your application`.)
- **Exclamation marks** — banned everywhere except the signing-ceremony hero ("Your funding is locked in." stays without; "Welcome aboard!" — banned).
- **"Submit" CTAs** — banned. Replace with: *Send*, *Sign*, *Confirm*, *Continue*, *Save and continue*, *Start*.
- **Passive voice in microcopy** — banned. Rewrite: *Your application has been received* → *We've received your application*.
- **Jargon for jargon's sake** — *facilitate*, *leverage*, *in order to*, *at this point in time*. Use plain English.
- **Apologetic empty states** — *Oops*, *Sorry*, *Nothing here* (without a constructive next step). Empty states must orient and offer one CTA.
- **System-pretending-to-be-human** — never *I* / *me*. The platform is *we*, not a single voice.

## Do / Don't pairs

| Don't | Do |
|-------|----|
| `Submit your application` | `Send your application` |
| `Your application has been received successfully!` | `We've received your application.` |
| `An error occurred. Please try again later.` | `Something went wrong. Try again — your draft is saved.` |
| `No items found.` | `Nothing here yet — start your first one.` |
| `CONGRATULATIONS! YOU ARE FUNDED!` | `Your funding is locked in.` |
| `Click here to sign` | `Sign your agreement` |
| `Please be advised that your draft expires in 7 days` | `Your draft expires in 7 days — we'll remind you the day before.` |
| `Submitter:` | `Sent by:` |
| `No applications.` | `Ready to apply for funding?` |
| `User has been registered.` | `Welcome — your account is ready.` |

## Microcopy patterns

### Awaiting-action callout (US1)

> Your funding agreement for *{project name}* is ready to sign.
> [Sign your agreement →]

### Empty welcome scene (US1, zero applications)

> **Ready to apply for funding?**
> Tell us about your project — we'll guide you the rest of the way.
> [Start a new application]

### Reviewer queue empty state (US4)

> All clear — nothing's awaiting your review.

### Ceremony — both signed

> **Your funding is locked in.**
> Funds will be transferred by *{date}*.

### Ceremony — applicant signed first

> **You're signed.**
> We're waiting on the funder. We'll email you when it's complete.

### Ceremony — funder signed first (no confetti, dignified)

> **Funder signature recorded.**
> The applicant has been notified.

## Verification

- **Greppable smell-tests**: `\bsubmit\b`, `\bplease\b`, `!`, `\boops\b`, `\bsorry\b`, `\bclick here\b` — every match in a swept view requires justification or rewrite.
- **SWEEP-CHECKLIST.md** carries one voice-guide column per view; tick when the strings on that view pass the rules above.
- **Constitution III** still holds: every story has E2E coverage, but the voice gate is human review, not automated assert.
