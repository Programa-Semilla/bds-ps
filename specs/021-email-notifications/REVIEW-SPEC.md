# Spec Review: Email Notifications System (021)

**Spec:** specs/021-email-notifications/spec.md
**Date:** 2026-05-11
**Reviewer:** Claude (speckit-spex-gates-review-spec)

## Overall Assessment

**Status:** SOUND

**Summary:** Spec is implementable as-is. Eight prioritized user stories cover the five-event v1 workflow plus operational edge cases (provider outage, allowlist guard, role-change predicate). FRs and NFRs are concrete; SCs are measurable. Ten open questions are correctly scoped as planning-time pins, not implementation blockers. No `[NEEDS CLARIFICATION]` markers remain.

## Completeness: 5/5

### Structure
- All required sections present: Purpose (Input paragraph), User Scenarios & Testing, Edge Cases, Functional Requirements, Non-Functional Requirements, Success Criteria, Assumptions, Dependencies, Out of Scope, Open Questions.
- Repo convention sections (Event Catalog, Recipient Rules, Key Entities) included and aligned with the patterns established by specs 011 / 014 / 017 / 019.
- No placeholder text. No TBDs.

### Coverage
- Every event in §Event Catalog is covered by at least one user story (US1–US5).
- Every recipient bucket has explicit acceptance scenarios (applicant, reviewer, participating-admin).
- Operational paths (retry, dead-letter, allowlist) covered by US6–US7.
- Edge-case enumeration (EC-001..EC-015) is exhaustive for v1.

**Issues:** None.

## Clarity: 4.5/5

### Language Quality
- Requirements use `MUST` consistently. No `should` / `might` / `could` ambiguity in FRs.
- Edge cases enumerated with expected behavior, not just symptoms.
- Subject templates are quoted verbatim es-CR strings; no paraphrased descriptions.
- Bucket priority `applicant > reviewer > admin` stated explicitly in FR-012 and §Recipient Rules.

**Ambiguities Found:**

1. NFR-002 — "P95 time-to-send (...) MUST be under 30 seconds under normal load."
   - **Issue**: "normal load" is undefined. The platform has no prod traffic baseline yet.
   - **Why this is acceptable here**: Pre-production v1; no load profile exists. The plan phase will define "normal load" once the worker poll interval + provider RTT are pinned. SC-009 ratifies the metric against the actual E2E suite, which serves as the de-facto load profile for v1.
   - **Suggestion**: Plan phase clarifies "normal load = the load produced by a full E2E suite run with the default `Notifications:Worker:PollIntervalSeconds=5`."

2. FR-026 — "No new MVC routes are introduced. Access control MUST be enforced server-side by the existing authorize attributes on the target controllers."
   - **Issue**: Assertion that `/Reviewer/Applications/Details/{id}` and `/Applications/Details/{id}` already exist is implicit.
   - **Why this is acceptable here**: Specs 001 / 002 / 004 ship the applicant + reviewer detail surfaces; the routes are presumed by Assumptions. Plan phase verifies by `grep`-ing the controller-attribute set.
   - **Suggestion**: Plan phase adds a one-line confirmation that the routes exist; otherwise an evolution gate fires.

3. EC-002 — "demoted admin who *did* take an explicit action stays in the participating-admin bucket."
   - **Issue**: The exact tables consulted by the resolver are not named in the spec.
   - **Why this is acceptable here**: FR-013 says "existing reads (`Application.VersionHistory` + existing audit)"; the plan phase pins the precise table list once spec 002's audit shape is re-read.
   - **Suggestion**: Plan phase enumerates the predicate's exact SQL / EF query.

## Implementability: 5/5

### Plan Generation
- Every FR is locatable to a layer (Domain workflow hook → Application outbox writer / resolver → Infrastructure email sender → Web template).
- Dependencies on specs 002 / 004 / 016 / 019 are explicit; no unknown / speculative dependencies.
- Constraints (zero EF migrations, no inline `<img>`, brand-grep gate green) are enforceable and CI-verifiable.
- Scope is well-bounded: 5 events, 8 user stories, 2 new tables, 9 templates.

**Issues:** None.

## Testability: 5/5

### Verification
- Each FR has a corresponding SC or acceptance scenario.
- SC-001..SC-009 are automatable; SC-010 is the one qualitative criterion and is explicitly marked as such.
- Acceptance scenarios use Given/When/Then format throughout.
- Idempotency, retry, and allowlist behavior all have explicit test recipes in the user stories.

**Issues:** None.

## Constitution Alignment

- **§I Clean Architecture**: Implicit but consistent — recipient resolver is an Application-layer interface; `IEmailSender` lives in Infrastructure; Razor templates live in Web; Domain transition methods stay the trigger. No inverted-direction reference.
- **§II Rich Domain Model**: Workflow transitions (`Submit`, `SendBack`, `Resubmit`, `Approve`, `Reject`) on the `Application` aggregate stay the canonical trigger; the outbox row is written via a unit-of-work hook, not by a controller.
- **§III E2E Mandatory**: FR-031 + FR-032 + SC-001 + SC-005 enforce E2E coverage; AspireFixture extension is the net-new infrastructure.
- **§IV Schema-First (Dacpac)**: NFR-005 + SC-008 enforce dacpac-only schema with a CI grep gate over `**/Migrations/**`. No EF migrations introduced.
- **§V Specification-Driven Development**: User stories are priority-ordered and independently testable per principle.
- **§VI Simplicity / YAGNI**: Domain-event dispatcher abstraction was explicitly rejected (see `implementation-notes.md`); i18n key system rejected; multi-replica worker design deferred; in-app channel out of scope. All YAGNI'd with rationale.

**Violations:** None.

## Cross-Artifact Consistency

Plan and tasks do not yet exist (planning phase pending). Cross-artifact consistency check via `/speckit-analyze` is not yet applicable.

## Recommendations

### Critical (Must Fix Before Implementation)
- None.

### Important (Should Fix)
- None.

### Optional (Nice to Have)
- Plan phase: define "normal load" for NFR-002 in concrete terms (a default poll interval + expected concurrent outbox-row count).
- Plan phase: enumerate the exact tables consulted by the participating-admin predicate (FR-013, EC-002).
- Plan phase: confirm `Application.Folio` (or equivalent) field name and population path; otherwise EC-009's fallback applies.
- Plan phase: pin the exact existing routes referenced by FR-026 (`/Reviewer/Applications/Details/{id}`, `/Applications/Details/{id}`).
- Plan phase: ratify OQ-001..OQ-010 with explicit decisions on each.

## Conclusion

The specification is sound and ready to advance to `/speckit-plan`. The open questions list is appropriately scoped — they are planning-time decisions, not implementation blockers, and the spec carries recommended defaults for each.

**Ready for implementation:** Yes (after `/speckit-plan` ratifies the planning-pin items).

**Next steps:**
1. User reviews the spec in-place (gate per `speckit-spex-brainstorm` skill).
2. Generate `review_brief.md` for stakeholder review (deferred until user OKs spec).
3. Proceed to `/speckit-plan`. The plan-phase pre-hook `speckit.spex-teams.research` is registered as optional; running it in parallel with planning will accelerate the open-question ratification.

---

## Re-Review 2026-05-12 (Post-Clarify)

**Date:** 2026-05-12
**Reviewer:** Claude (speckit-spex-gates-review-spec)
**Trigger:** Re-validation after a `/speckit-clarify` pass on 2026-05-12 closed five OQs (OQ-001, OQ-002, OQ-005, OQ-006, OQ-008) and rewrote FR-007, FR-014, FR-023, FR-024, FR-030, §Event Catalog, §Recipient Rules, §Key Entities, §Dependencies, §Assumptions, and §Open Questions.

### Overall Assessment

**Status:** SOUND

**Summary:** The five clarifications are internally consistent and propagated cleanly across every dependent section. The `APPLICATION_SUBMITTED` enum split is the most invasive change — it touches FR-007, FR-024, §Event Catalog, §Recipient Rules, §Key Entities, US1's independent-test recipe, and OQ-006 — and the propagation is correct in all six locations. No critical or important issues surfaced; three minor cosmetic inconsistencies are noted below as Optional fixes (none block planning).

### Clarification → Section Coverage Audit

| Clarification | FR / Section anchors | Internal consistency |
|---|---|---|
| OQ-006 — split `APPLICATION_SUBMITTED` | FR-007 (two outbox rows); FR-024 (variants enumerated); §Event Catalog (6 rows); §Recipient Rules (6 rows); §Key Entities `NotificationEvent` enum; US1 independent-test; OQ-006 marked Resolved | Consistent — every reference uses `_REVIEWER` / `_APPLICANT` suffixes; idempotency key `(EventType, ApplicationId, VersionHistoryId, RecipientUserId)` dedupes cleanly because each enum value carries exactly one template variant. |
| OQ-002 — smtp4dev pin | FR-030 (Docker image `rnwood/smtp4dev`, port 25 + REST API); §Dependencies (Aspire resource); OQ-002 marked Resolved | Enforceable + testable: `MailCaptureClient` polls smtp4dev's documented `/api/Messages` REST endpoint. CI / E2E can grep for the image tag. |
| OQ-005 — MailKit v3 (MIT) | FR-014 (impl name); §Assumptions; §Dependencies (CLAUDE.md managed-NuGet rule); OQ-005 marked Resolved | Enforceable: pin in `Directory.Packages.props` (or csproj) verifiable by static scan; CLAUDE.md update is in NFR-008's config table scope but should also list the dependency. Minor: §Dependencies says "MailKit **v3 (MIT)**" but does not name a specific 3.x line (3.x is still receiving security backports). Plan phase will pin the floor. |
| OQ-001 — static support footer | FR-023 (verbatim string + plain-text equivalent); §Assumptions (ToS-safe default); OQ-001 marked Resolved | Enforceable + testable: a grep gate on `_EmailLayout.cshtml` for `soporte@programa-semilla.cr` confirms the static line; the `List-Unsubscribe` header absence is verifiable by inspecting captured smtp4dev message headers in E2E. |
| OQ-008 — retention policy | §Key Entities (per-table breakdown); OQ-008 marked Resolved | Enforceable: a future cleanup task can codify the policy. Cleanup-job-out-of-scope is explicit, so no implementation work is owed by this spec. |

### Re-Scoring

**Completeness: 5/5** (unchanged) — All clarifications integrated; no new TBDs introduced.

**Clarity: 4.5/5** (unchanged) — Three minor cosmetic items below; same NFR-002 "normal load" ambiguity remains (already a planning-pin).

**Implementability: 5/5** (unchanged) — Every clarification ties to a concrete artifact path (a Razor partial, a Docker image, an NuGet package version, a SQL column).

**Testability: 5/5** (unchanged) — Each new pin has an enforcement vector (grep gate, version assertion, header inspection, SQL retention query).

### Open Questions Status

| OQ | Status | Planning blocker? |
|---|---|---|
| OQ-001 | Resolved (Clarifications) | No |
| OQ-002 | Resolved (Clarifications) | No |
| OQ-003 — Real Mailtrap as opt-in override | Open / planning-pin | No — default-sidecar path already pinned; override path is config-only |
| OQ-004 — Sender email per env | Open / ops-pin | No — config knob exists in NFR-008; ops decision, not a spec decision |
| OQ-005 | Resolved (Clarifications) | No |
| OQ-006 | Resolved (Clarifications) | No |
| OQ-007 — Folio source-of-truth | Open / planning-pin | No — EC-009 carries an explicit fallback to `Solicitud #{ApplicationId}` |
| OQ-008 | Resolved (Clarifications) | No |
| OQ-009 — Multi-replica worker scaling | Open / deferred | No — FR-004 + FR-020 + EC-008 cover correctness; scaling is post-v1 |
| OQ-010 — Brand-grep gate render-time vs source-time | Open / planning-pin | No — `.cshtml` source layer recommendation is sufficient |

Five OQs remain open. All five are planning-time pins, ops-pins, or deferred items with documented fallbacks in the spec. None block `/speckit-plan`.

### Suggestions Closed By This Pass

The 2026-05-11 review listed five Optional plan-phase items. Three of those are now closed by the clarify pass:

- ✅ "Plan phase: ratify OQ-001..OQ-010 with explicit decisions on each." — Five resolved in §Clarifications; the other five are explicitly tagged as planning-pin / ops-pin / deferred with documented defaults.
- ✅ "Pin the SMTP-capture sidecar choice (OQ-002)." — smtp4dev pinned in FR-030.
- ✅ "Pin MailKit license posture (OQ-005)." — v3 MIT pinned in FR-014, §Assumptions, §Dependencies.

Two suggestions remain for the plan phase:

- "Define 'normal load' for NFR-002." — Still pending; plan-phase decision.
- "Enumerate the exact tables consulted by the participating-admin predicate (FR-013, EC-002)." — Still pending; plan-phase decision.
- "Confirm `Application.Folio` exists and is populated by `Submit()`." — Still pending (OQ-007 planning-pin).
- "Pin the exact existing routes referenced by FR-026." — Still pending; plan-phase verification.

### New Issues Surfaced by the Clarify Pass

**Critical:** None.

**Important:** None.

**Optional (cosmetic, do not block planning):**

1. **Input paragraph (line 6)** still lists "five workflow events (`APPLICATION_SUBMITTED`, ...)" and "smtp4dev / MailHog" — the historical user description was not rewritten by the clarify pass (which is expected — `Input:` is a historical record of the original prompt). The §Clarifications block + the canonical §Event Catalog supersede it. No action required, but flag for newcomers.

2. **§Assumptions line 284** carries the legacy "(smtp4dev / MailHog) container image" phrasing. The "/ MailHog" is now stale; smtp4dev is pinned. Suggested edit: drop "/ MailHog" to read "The smtp4dev (`rnwood/smtp4dev`) container image is reachable...". Trivial.

3. **FR-024 variant count vs partial enumeration.** FR-024 says "**eight** body variant partials covering the six enum values" but the prose enumerates: 1 (`_APPLICANT`) + 1 (`_REVIEWER`) + 1 (RETURNED applicant) + 1 (RESUBMITTED) + 1 (APPROVED applicant) + 1 (REJECTED applicant) + 1 (RETURNED admin) + 1 (APPROVED admin) + 1 (REJECTED admin) = **9** partials, OR a smaller count if participating-admin reuses the applicant body (US2 scenario 2 says "the same email body as the applicant variant (or a participating-admin variant)"). Either the "eight" count or the partial enumeration is off-by-one. Plan phase should pin the exact partial count and whether participating-admin variants are distinct partials or share the applicant body. Does not block planning — the plan can resolve by either renumbering to 9 (separate admin partials) or to 6 (admin shares applicant body).

4. **§Key Entities NotificationOutbox retention** (line 254) says "90 days for `Status IN (Done)`" — correct for Outbox (which has only `Pending | Dispatching | Done | DeadLetter`). The Clarifications block bundled both tables into one answer (which included `BlockedByAllowlist` / `Skipped` — those are Delivery-only statuses). The per-entity breakdown in §Key Entities is correct, but the clarify-block sentence could read as if Outbox also has those statuses. Cosmetic; the canonical FR-002 (Outbox status enum) is unambiguous.

None of these introduce a new ambiguity that blocks planning. Items 1–2 are stale-prose cleanups; item 3 is an off-by-one that planning will resolve in the partial enumeration; item 4 is a phrasing collision between the clarify summary and the per-entity contract.

### Re-Review Verdict

**Status:** SOUND (re-affirmed)

**Ready for `/speckit-plan`:** Yes. No blockers introduced by the clarify pass; five OQs resolved; the remaining five are correctly tagged as planning-pin / ops-pin / deferred and carry documented defaults or fallbacks.

**Go/no-go for `/speckit-plan`:** GO. The plan phase should explicitly resolve the four remaining Optional planning-pin items (NFR-002 "normal load", FR-013 predicate tables, OQ-007 folio confirmation, FR-026 route existence) plus pick up the three Optional cosmetic notes above as it builds the contracts and tasks artifacts.
