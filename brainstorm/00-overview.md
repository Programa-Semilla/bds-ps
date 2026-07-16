# Brainstorm Overview

Last updated: 2026-07-15 (session #41)

## Sessions

| # | Date | Topic | Status | Spec |
|---|------|-------|--------|------|
| 01 | 2026-04-15 | core-model-submission | spec-created | 001 |
| 02 | 2026-04-15 | review-approval-workflow | spec-created | 002 |
| 03 | 2026-04-16 | supplier-evaluation-engine | spec-created | 003 |
| 04 | 2026-04-17 | applicant-response-appeal | spec-created | 004 |
| 05 | 2026-04-17 | document-generation | spec-created | 005 |
| 06 | 2026-04-18 | digital-signatures | spec-created | 006 |
| 07 | 2026-04-23 | signing-wayfinding | spec-created | 007 |
| 08 | 2026-04-25 | tabler-ui-strategy | spec-created | 008 |
| 09 | 2026-04-25 | admin-area | spec-created | 009 |
| 10 | 2026-04-26 | admin-reports | spec-created | 010 |
| 11 | 2026-04-27 | warm-modern-facelift | spec-created | 011 |
| 12 | 2026-04-29 | es-cr-localization | spec-created | 012 |
| 13 | 2026-04-30 | supplier-catalog | spec-created | 013 |
| 14 | 2026-05-07 | user-groups | spec-created | 016 |
| 15 | 2026-05-08 | admin-ux-facelift | spec-created | 017 |
| 16 | 2026-05-08 | pdf-template-lift | spec-created | 018 |
| 17 | 2026-05-09 | programa-semilla-brand | spec-created | 019 |
| 18 | 2026-05-11 | ai-quote-comparison | spec-created | 020 |
| 19 | 2026-05-11 | email-notifications | spec-created | 021-email-notifications; 028 (revisit 2026-05-27) |
| 20 | 2026-05-13 | feedback-session-may13 | spec-created | 021-feedback-session-may13 |
| 21 | 2026-05-20 | quotation-edit | spec-created | 023 |
| 22 | 2026-05-21 | applicant-delete-withdrawal | spec-created | 021-feedback-session-may13 |
| 23 | 2026-05-22 | toast-confirm-dialogs | spec-created | 024 |
| 24 | 2026-05-24 | input-masks | spec-created | 026 |
| 25 | 2026-05-26 | review-funding-ux | spec-created | 027 |
| 26 | 2026-06-09 | fund-entity | spec-created | 029 |
| 27 | 2026-06-10 | edit-process-name | spec-created | 030 |
| 28 | 2026-06-11 | searchable-dropdowns | spec-created | 031 |
| 29 | 2026-06-11 | admin-user-code | spec-created | 032 |
| 30 | 2026-06-12 | user-invite-email | spec-created | 033 |
| 31 | 2026-06-12 | line-item-category-templates | spec-created | 035 |
| 32 | 2026-06-16 | funds-usage-evidence | spec-created | 036 |
| 33 | 2026-06-17 | official-brand-alignment | shipped (PR #67) | 037 |
| 34 | 2026-06-17 | applicant-companies | shipped (PR #68) | 037 |
| 35 | 2026-06-17 | feedback-3-provider-compliance | shipped (PR #69) | 038 |
| 36 | 2026-06-18 | supplier-recommendation | shipped (PR #70) | 039 |
| 37 | 2026-06-18 | auditor-workflow-stage | shipped (PR #72) | 040 |
| 37 | 2026-06-19 | email-brand-lift | shipped (PR #73) | 041 |
| 38 | 2026-06-19 | evidence-inbox | shipped (PR #72) | 041 |
| 39 | 2026-06-21 | regulatory-freshness-hacienda-sync | shipped (PR #75) | 043 |
| 40 | 2026-06-21 | process-reception-windows | shipped (PR #77) | 044 |
| 41 | 2026-07-15 | financial-disbursement-platform | P1 shipped (PR #78) | 045 |

> Note: feature number **041** was used twice by parallel sessions —
> `041-email-brand-lift` (this session #37) and `041-evidence-inbox` (#38, shipped
> via PR #72). Disambiguate by slug, as with the two `021-` slugs.

## Open Threads

- **Financial-execution program (new, from #41):** 9-slice roadmap in `41-financial-disbursement-platform.md`. **P1 shipped (PR #78) → spec 045**; P2–P9 unspecified and documented for resume (P2 tranches/budget-lines, P3 evidence graph, P4 full reconciliation, P5 currency execution, P6 interest/fees/refunds/reversals, P7 reporting, P8 segregation-of-duties, P9 migration). Program-level threads: P2 anchor (budget-line = existing application `Item` vs new entity?); P4 balance-recognition revisit (official "available" off payment vs validation — currently payment); P6 ledger-vocabulary growth preserving the immutability boundary; P5 reuse-vs-extend of spec-015 multi-currency. (from #41)
- Spec-045 (financial-disbursement-core) plan-time thread: over-disbursement discrepancy data shape — attached-to-latest-disbursement vs. a distinct agreement-scoped record (deferred to `/speckit-plan`); optionally promote optimistic-concurrency from edge-case to a first-class FR. (from #41)
- Spec-041 (evidence-inbox) business decision: should admins retain write access to funds-usage evidence after a process closes, or be frozen read-only like reviewers? (Spec assumes frozen.) Plus inbox row ordering (deferred to plan). (from #38)
- Session bug fixed outside any spec: the reviewer queue (`ReviewerQueueProjection`) did not surface `ReturnedFromAudit` applications, so auditor-returned apps never reappeared in the reviewer worklist; fixed + unit/E2E covered. (from #38)
- Spec-041 (email-brand-lift) OQ-1 resolved: the "Nueva empresa para revisión" email was withdrawn as a duplication of spec 038's supplier→auditor notification (`IProviderCreatedNotifier`); no auditor group-scoping. (from #37)

- Feedback-3: A/B/C/D shipped (A #69, B #70, C #72, D #75); **E (fund process reception windows + applicant timing) spec-created (#40 → spec 044)** — also resolves the round's §28.11 (window inclusivity) and §28.12 (timezone). Slices **F** (per-user funding limit, §4), **G** (applicant timeline + % progress, §20), **H** (UX grab-bag) remain unspecified and are independent (no foundation dependency). Master doc + slice map: `seeds/feedback-3/` (from #35, #36, #37, #39, #40)
- Spec-044 (reception-windows) plan-time threads: confirm no residual reader of `SolicitudWindowDays` before dropping the column; decide client-tick vs server-rendered countdown (data pinned by FR-011/012); future slice for informational/deadline/milestone `ProcessEvent` *behavior* (schema ready, behavior deferred). (from #40)
- Spec-043 (regulatory-freshness-hacienda-sync) plan-time: (1) confirm the Hacienda→`HaciendaStatus` mapping for less-common `estado` (Desinscrito variants) + `omiso = SI` (not moroso) against the real value vocabulary, ideally by sampling live ids; (2) pin the exact "selected quotation" semantics for the referenced-provider set the freshness gate checks; (3) notification cadence — daily digest (proposed) vs once-on-threshold-crossing; (4) whether the per-provider last-sync metadata is columns on `dbo.Suppliers` vs a small related record (dacpac). (from #39)
- Spec-040 plan-time: seeded default checklist template's `appliesToStage` (recommend `both`); whom the agreement records as generating actor now that the auditor generates it; cross-cutting E2E ripple from removing the reviewer's direct generate-agreement path (existing funding-agreement/signing tests route through audit) (from #37)
- Spec-040 plan-time: where the two new application states (`PendingAudit`/`ReturnedFromAudit`) slot in the domain state machine + dacpac, and the new `ChecklistTemplate`/`ChecklistTemplateItem`/`ApplicationChecklistResponse` tables (§22.9–22.11) — greenfield, no backfill (from #37)
- Spec-039 plan-time: where the CCSS-`sin inscripción` progression-gate evaluation lives (a single advance-guard/eligibility service) so slice C re-anchors it with minimal churn (from #36)
- Spec-039 plan-time: total-score display treatment — raw total + breakdown vs. an "X/14" fraction (spec leaves presentation open) (from #36)
- Spec-039 plan-time: introduce the two new required quote fields via dacpac + post-deploy seed-data update; ensure every seeded quote is populated so existing seeds don't fail the new validation (Constitution IV) (from #36)
- Spec-039 business confirmation: warranty direction (longer = better) and month→days = 30 for scoring comparison (from #36)
- Long-term: revisit whether the AI quote comparison (spec 020) should be demoted/retired once the transparent deterministic score is in place (from #36; relates to #18)
- Spec-038 plan-time: audit-trail storage approach — extend generic `AdminAuditEvent` vs a dedicated `ProviderRegulatoryAuditEvent` table (seed wants richer fields: previous/new value, source, reviewedBy) (from #35)
- Spec-038 plan-time: es-CR Auditor role display label ("Auditor" vs "Auditoría"); whether "reviewed — no change" is available before a value is set; warning-note max length (from #35)
- Spec-038 plan-time: inventory every `SupplierAdmin` reference (auth checks, role seeds, E2E fixtures, demo `supplieradmin@…` account) for the role rename → Auditor with capability parity (from #35)

- Admin UI placement for the per-applicant company list (inline on user Edit vs. dedicated `/Admin/Users/{id}/Companies` sub-surface) — HOW, deferred to plan (from #34)
- Audit-event naming prefix for company create/rename/archive/unarchive (likely `company.*`, mirroring `fund.*`/`process.*`/`funds_evidence.*`) — pin in plan (from #34)
- Whether a one-time backfill of existing applicants/applications into the company model is wanted later (currently greenfield; pre-existing applicants can't submit until an admin adds a company) (from #34)
- Raster-as-provided vs. request a vector original if the auth-hero vertical logo renders soft (OQ-001) (from #33)
- Exact sidebar white-container treatment — full-bleed white pill vs. subtle off-white card (OQ-002) (from #33)
- Is `#F9A61C` orange wired to any existing status today, or held purely in reserve? (OQ-003) (from #33)
- Should the PDF partner-strip also adopt the new official partner set, or teal re-tint only? (OQ-004) (from #33)
- Page background shifts to off-white `#F6F8FA` (spec 019 chose pure white) — confirm it reads well across dense admin tables (from #33)
- E2E selector churn from the kebab + de-zebra + dark-sidebar restructure — POM rewrites budgeted; per-surface brand assertions replace per-sponsor-SVG footer assertions (from #33)

- Confirm `AgreementExecuted` is the right proxy for "funds disbursed" vs. a dedicated disbursement event (from #32)
- Curated file-type allow-list vs. genuinely-any type (raw ask said "all types") — confirm with stakeholders (from #32)
- Applicant visibility of funds-usage evidence — deferred this iteration; revisit (from #32)
- dacpac ordering for the new `dbo.FundsUsageEvidence` table (+ FK to `Applications`) — greenfield add, no backfill; confirm in plan (from #32)
- Audit-event verb names (`funds_evidence.uploaded`/`.note_edited`/`.deleted`) and es-CR rejection copy — pin during planning (from #32)
- Whether a per-application storage quota / max evidence count is ever needed (currently unbounded, 20 MiB/file only) — parked (from #32)

- Category-field values flowing into the AI quote-comparison context (spec 020) — subject them to the existing PII/redaction boundary? Pin in plan (from #31)
- Deactivating the *last* active impact template — guard it, given per-item impact is now required to submit? Pin in plan (from #31)
- dacpac ordering for new `CategoryField`/`CategoryFieldValue` tables + item-keyed impact relocation + drop of `PlantillaImpactTemplates` — greenfield (no backfill); confirm in plan (from #31)
- Domain placement of new invariants (clear category-field values on category change; per-item impact required; document-retain-until-last-reference) on `Item`/`Application` per Rich Domain Model — pin in plan (from #31)
- Reviewer/applicant detail layout — dedicated per-line "category fields" sub-section design vs reuse the impact-values render pattern — design in plan (from #31)

- Reuse the existing password-reset token (72h-parameterized) vs a dedicated invitation token for spec 033 — pin in plan; "resend supersedes prior unused" (FR-007) is the constraint (from #30)
- Spec 033 delivery path: direct-send (`ForgotPasswordEmail` pattern) vs spec-021 outbox — pin in plan, leaning direct-send (from #30)
- Spec 033 "no usable password" technique + the admin-create-then-login E2E ripple (SeedUser keeps passwords) — pin in plan (from #30)
- Whether to retire/convert the existing admin temp-password "reset password" action for coherence with the invite model — out of scope for 033; future pass (from #30)
- Long-term reconciliation of the two free-text codes `UserCode` (new, spec 032) vs `CodigoPersonal` (spec 021) — keep both or merge later (from #29; relates to #25)
- `UserCode` storage placement (Applicant vs account) — pin in plan; admin users list joins Applicant either way (from #29)
- Filtered unique index over nullable `UserCode` + es-CR duplicate-message path is E2E-only (in-memory won't enforce; mirrors spec 030 `UX_Processes_Name`) — pin in plan (from #29)
- Reviewer queue: visible User Code column vs match-only (FR-016 discretionary) — decide in plan (from #29)
- Re-grep for any additional people-search surface beyond the fixed three groups ("any other screen" guard) (from #29)

- Exact opt-in mechanism for searchable dropdowns (`data-searchable` attribute vs. auto-detecting data-driven selects) — deferred to plan.md (from #28)
- Whether affected Playwright page objects target the retained native `<select>` or the combobox input after enhancement — deferred to plan.md (from #28)


- RowVersion / optimistic-concurrency handling on the Process rename happy path — duplicate-name races covered by `UX_Processes_Name`; lost-update on the name field is the residual (low risk, admin-only single field) — pin in plan (from #27)
- Closed-Process rename policy — shipped as "allowed at any status"; revisit only if audit integrity of historical cycle names is challenged (mitigated by the `process.renamed` audit entry) (from #27)
- Stable `data-testid` hooks for the new inline rename form so the E2E rewrite has reliable selectors (from #27)


- Exact set of create/edit/submit/review actions disabled when a Fund is archived — deferred to plan; scoped to the Process state model (from #26)
- "Participant under a Fund" semantics — Group holds reviewers (spec 016), not applicants; a future Fund→participant report must define which population it counts (from #26)
- Whether Processes-per-Fund reporting is sufficient for v1, or stakeholders expect the seed's Fund→Groups/Participants drill-down (from #26)
- Process Fund selector ordering (by name?) and how archived-Fund Processes render in admin views — read-only badge vs hidden toggle (from #26)
- New `fund-regulations` spec-014 FileCategory size cap value — pin during planning (from #26)

- Per-applicant vs per-application scope for the reviewer-assigned code — spec 027 assumes per-applicant (reuses `CodigoPersonal`); confirm at planning (from #25)
- Final placement of sidebar items absent from the stakeholder example (Usuarios, Configuración, Plantillas de impacto, Cotizaciones pendientes) — confirm in plan (from #25)
- US4 single line-summary projection contract (fields + rejected-line per-supplier amounts) — define once, fan out to 5 surfaces (from #25)
- Tooltip copy: ship Claude's draft es-CR strings now and iterate vs wait for stakeholder copy — completes spec-021 OQ-8 / FR-020 (from #25)
- FR-022 zero-removal sidebar: encode an explicit before/after per-role destination table for mechanical verification (from #25)

- Domain placement of the identification type↔shape invariant: value object vs entity guard, ViewModel attributes echoing it — settle in planning (from #24)
- Profile identification editable vs display-only (Profile email is currently read-only / admin-managed) (from #24)
- Optional deferred soft hint: warn when a 10-digit ID's leading digit is atypical for the chosen type (jurídica usually starts `3`) (from #24)

- Should there be a maximum number of items per application? (from #01)
- Should there be a maximum number of suppliers per item beyond the minimum? (from #01)
- Retention policy for abandoned draft applications (from #01)
- Performance score on Applicant: manual, calculated, or deferred? (from #01)
- Constitution needs to be filled in after first implementation (from #01)
- Should pagination page size be configurable or fixed? (from #02)
- Does full item-status reset on send-back create unnecessary re-work for reviewers? (from #02)
- Persistence model for `ApplicantResponse`: durable snapshot vs. reconstructed from item-level state (from #04)
- Representation of `AppealMessage`: child entity with identity vs. value object in a collection on `Appeal` (from #04)
- Whether `ApplicantResponse` decisions should be visible to reviewers in read-only form before an appeal is opened (from #04)
- Terms & Conditions copy ownership and delivery path for the Funding Agreement template (from #05)
- Funder identity shape: single configuration block vs. richer `Funder` aggregate for multi-funder scenarios (from #05)
- Reviewer regeneration rights on the Funding Agreement — revalidate during planning when full role-scope is visible (from #05)
- Syncfusion HTML-to-PDF license acquisition and cost — planning/ops coordination prerequisite (from #05)
- Formal audit retention policy for generated Funding Agreement PDFs — deferred to a later compliance-driven spec (from #05)
- Side-by-side view of generated agreement vs. signed upload to aid reviewer visual verification (from #06)
- Execution banner or cover page on executed signed PDF (from #06)
- Final upload size limit value for signed PDFs; 20 MB default proposed (from #06)
- Verify spec 005 precision on which role (reviewer vs. approver) can trigger agreement regeneration, so FR-010 has no inherited ambiguity (from #06)
- Administrative back-out of the signing stage — deliberately out-of-scope gap at feature boundary; ops has no supported path until an admin tooling spec exists (from #06)
- Whether unlimited rejection cycles are acceptable long-term, or whether a future reporting/ops-visibility feature should pressure-test the assumption (from #06)
- Automated reviewer assist (content hash, version marker, or side-by-side diff) to catch mismatched signed uploads — currently purely visual (from #06)
- Should pending-count badges ship with the signing wayfinding, or remain a future polish item once inbox volume makes them valuable? (from #07)
- Should `AppealOpen` get its own banner string on the applicant response page, or remain silent as currently specified? (from #07)
- Is the two-click threshold (SC-001) the right long-term bar, or will future volume eventually justify a top-level nav entry for Signing alongside the sub-tabs? (from #07)
- The 006 signing panel partial is assumed to be shape-clean enough to embed on a second host page; if implementation discovers it is not, reshaping it is within 007 scope but may expose further 006 refactors worth tracking (from #07)
- Specific Tabler.io version pin — deferred to planning (latest stable at planning time) (from #08)
- Sidebar default-open vs. default-collapsed on first load — deferred to planning (from #08)
- Whether the absolute "no badges outside `_StatusPill`" rule should permit non-status badges (e.g., quantity counters) — to be revisited if the planning phase surfaces concrete cases (from #08)
- Whether to invest in visual-regression tooling (Playwright screenshot comparison or Percy) before the sweep, or leave manual side-by-side as the v1 visual gate (from #08)
- Future spec 009 (communication surface — unified messaging panel) needs its own brainstorm before any implementation (from #08)
- Future spec 010 (notifications & inbox) needs its own brainstorm — likely SignalR (from #08)
- Future spec 012 (admin/configuration surface polish) — likely needed once the 008 sweep lands (from #08)
- Applicant demotion in-flight applications: when an Applicant is demoted, what should the original applicant see for their existing applications? Most likely read-only, pin during planning (from #09)
- `ADMIN_DEFAULT_PASSWORD` configuration key shape and Aspire/user-secret wiring — settle precise key path during planning (from #09)
- Sentinel password rotation procedure (post first-deploy) — no in-product rotation in v1; operational runbook needed in the plan (from #09)
- Sentinel-password WARN-log emission ordering — a crash between user-row commit and log-flush could leave the password unrecoverable; plan must specify emit-before-commit or equivalent (from #09)
- Whether the expanded admin-edited-profile scope (first/last/phone for all roles, legal id for Applicants) is right v1 surface or should narrow back to identity-level only (from #09)
- Whether single-role-by-contract is the right call vs single-role-by-UX with a multi-role-capable data model (from #09)
- Future audit log of admin actions — deferred to a future compliance/reporting spec; needs to land when external audit pressure surfaces (from #09)
- Page-size convention reuse from the review queue — pin during planning (from #10)
- CSV export upper-bound numeric value (cited as `e.g., 50,000`) — pin during planning (from #10)
- `DefaultCurrency` configuration key shape and per-environment conventions (mirrors spec 009's `ADMIN_DEFAULT_PASSWORD` decision) — pin during planning (from #10)
- Spec 005 Funding Agreement PDF visual integrity after the one-token currency-code render change — verify with a PDF-snapshot regression or manual visual comparison during planning (from #10)
- `VersionHistory` adequacy for "approved-at" (US5) and "last actor" / "days in current state" (US6) — verify during planning; spec already specifies em-dash fallback if any field is absent (from #10)
- dacpac deployment-step ordering for the `Currency` column add → backfill → NOT NULL tightening — confirm during planning (from #10)
- Whether the per-currency-stack visual density on the dashboard is acceptable across 1–2 currencies, or if a default-currency headline + hover-collapse is preferable when the platform later supports ≥ 3 currencies (from #10)
- Whether the bundled `010 = currency + reports` framing is right, or if reviewers would prefer `010A = currency / 010B = reports` — outcome of formal stakeholder review on `review_brief.md` (from #10)
- Whether v1's four-report bundle (Applications / Applicants / Funded Items / Aging) is the right cut, or if Activity / Status-Transitions or Appeals should swap in for one of the four (from #10)
- Whether the absence of a read-only Auditor sub-role is acceptable for v1 — future spec can add Auditor without breaking 010 (from #10)
- ISO 4217 enforcement of currency codes — deferred; future spec (from #10)
- Historical snapshotting of supplier display names / applicant identities on report rows — deferred; reports always render current relational state (from #10)
- Exact hex values for the warm forest green primary + warm amber accent + warm neutrals + warm-retuned status palette — pinned during planning by designer pass (from #11)
- 8 px spacing scale ratios and full type-scale ramp — pinned during planning after density audit on densest surfaces (from #11)
- Tabler `--tblr-*` CSS-variable bridge aggressiveness — inventory pinned during planning (from #11)
- canvas-confetti (or equivalent ≤ 5 KB gz) exact dependency pin — pinned during planning (from #11)
- Visual-regression tooling adoption — recurring open question from #08; defer or now is reviewer feedback (from #11)
- Selector strategy precedence (role/aria vs. data-testid) — pin during planning so all POM rewrites are uniform (from #11)
- Designer source for the 9 illustration SVGs (in-team / commission / adapted-from-open) — affects timeline (from #11)
- Empty-state surface audit — verify the 9-scene set covers all current empty-state usages (from #11)
- Unified event source service vs. query-time stitching for the activity feeds and journey tooltips (from #11)
- Canonical journey-stage mapping owner — extend IStatusDisplayResolver vs. sibling IJourneyStageResolver (from #11)
- Multi-branch journey rendering (Send-back loop AND active Appeal in one application) — visual contract pinned during planning (from #11)
- Reviewer queue activity-feed positioning at ≥ 1440 px (above table vs. right rail) — defaults to "above" (from #11)
- Confirm removing the status-pill column from reviewer queue rows in favor of inline micro journey timeline loses no information (from #11)
- Signing ceremony view-vs-partial choice (FR-044) — pin during planning (from #11)
- Signing ceremony fresh-vs-bookmark mechanism (TempData / query / one-shot session token, FR-047) — pin during planning (from #11)
- Login/Register tone — clean single-CTA vs. light marketing hero — defaults to "clean" (from #11)
- Schema-unchanged constraint escape-hatch protocol via speckit-spex-evolve — protocol established; specific trigger not anticipated (from #11)
- Performance baseline (LCP / TBT) capture timing — must run as planning day-1 task before any code lands (from #11)
- Future notifications & inbox / SignalR spec needs its own brainstorm — spec 011 deliberately excludes real-time push (from #11)
- Future communication-surface (unified messaging panel) spec still pending its own brainstorm (from #11; carries forward from #08)
- Future public marketing surface spec — distinct workstream; spec 011 and spec 012 explicitly chose authenticated-only (from #11)
- Glossary finalization for CR-Spanish term mappings (application/review/funding agreement/send back) — voice guide owns the choice (from #12)
- Footer tagline exact Spanish phrasing for "built for entrepreneurs" — recommended `diseñado para emprendedores` (from #12)
- Designer SVG follow-ups — Capital Semilla wordmark rework + on-image text audit on the 9 empty-state illustrations; whether either blocks merge (from #12)
- Tabler vendor JS string audit — whether any in-use components carry built-in copy needing override (from #12; recurring from #08)
- Performance baseline (LCP/TBT) capture — pin if spec 011's planning-day-1 baseline wasn't taken (from #12; recurring from #11)
- Voice-guide reviewer — same designer/voice owner as spec 011 or new CR-region reviewer (from #12)
- Page-title direction — `[Page] - Capital Semilla` (matches today) vs. reversed (from #12)
- Hard-pin culture via constant in middleware vs. config-overridable hatch (from #12)
- JS namespace rename final identifier — `PlatformMotion` recommended vs. `AppMotion` / `SeedMotion` (from #12)
- Cascade-delete a Draft supplier when its parent application is deleted — spec assumes yes; pin during planning (from #13)
- Hard-block reviewer from picking a Rejected supplier vs. soft-discourage via banner — spec assumes soft (from #13)
- PendingReview suppliers when their parent application is sent back to draft — spec assumes they do not revert to Draft; admin retains control (from #13)
- Applicant notification when their draft supplier is verified or rejected — out of v1 scope; potential follow-up wow-moment per spec 011 patterns (from #13)
- Admin queue count badge on the admin dashboard — out of v1 scope; cheap follow-up if queue lag becomes a UX bottleneck (from #13)
- Audit mechanism for group lifecycle and user-group membership changes — reuse existing audit pathway or add minimal one; pin during planning (from #14)
- Demo seed group names — working list "Norte / Sur / Centro"; final names locked at plan time (from #14)
- FR-014 reviewer-facing search-surface inventory — confirm at plan time which existing search endpoints exist; if none, restate FR-014 as forward-looking (from #14)
- Domain-level placement of the "≥1 group for non-admin" invariant — currently form-level; consider lifting into `User.SetGroups(role, groups)` per Rich Domain Model (from #14)
- FR-015: pin "section header click target" vs "Panel sub-entry inside the section" — defer to planning (from #15)
- Pending-supplier failure-mode (zero count / "—" placeholder / error tile) when source is missing or stale — pin during planning (from #15)
- Pending-supplier source enum value — confirm spec-013 supplier-status mapping during planning (from #15)
- Governance FR/SC for "future admin specs must update the dashboard's capability cards" — open question; currently captured only as edge-case mitigation note (from #15)
- Naming the dashboard projection (e.g., `IAdminDashboardProjection`) in the spec vs deferring to plan (from #15)
- Section grouping cardinality — three sections vs four (split Catálogo into entity catalog vs config catalog) — defaults to three (from #15)
- Whether route normalization should also touch class names + namespaces — currently attribute-only; revisit only if a future "admin module reorganization" spec is queued (from #15)
- Whether sub-surfaces that already passed spec 011's sweep need a fresh manual checklist walk vs a quick re-grep — defaults to manual walk (from #15)
- Is the sworn-declaration copy on the seed Legal-approved canonical text, or is the seed itself a draft? Default = canonical; revisit if Legal pushes back (from #16)
- Should `Application.CompanyName` surface on existing list/detail admin/reviewer screens beyond the new applicant form? Defer to plan phase (from #16)
- Source of clean per-partner footer logo files (BancaDesarrollo / CROCUS / nexo / Programa Semilla / 10 años) — needed if we ever want per-logo edit beyond composite swap (from #16)
- Brand-guideline hex codes — sampled from PDF; if a real brand guideline exists with different values, NFR-001 needs revisit (from #16)
- Whether to add automated PDF visual-diff regression harness in a future spec (e.g., `pdfimages` + image-hash against golden-PDF fixture) — recurring from #08/#11 (from #16)
- Exact teal hex sampled `#1FA0A0` from PDF logo disc; designer override at SC-015 if Programa Semilla brand book differs (from #17)
- Sponsor logo source — extract from PDF (low fidelity) vs request originals from sponsors (from #17)
- Login hero — large seedling mark only vs commissioned scene; defaults to mark-only (from #17)
- Sidebar collapsed-state breakpoint — Tabler default 992 px vs custom (from #17)
- Confetti palette specifics — teal + yellow only vs include cream + danger-soft (from #17)
- 10 años badge graceful retirement plan when "10 años" stops being current — future spec (from #17)
- BRAND-VOICE.md canonical location — repo root, new spec dir, or replace spec 011's in place (from #17)
- Visual-regression tooling — continue Playwright snapshot comparison vs adopt Percy/Chromatic; defaults to Playwright (from #17; recurring from #08/#11)
- Spec 018 PDF type-stack (Fraunces) vs spec 019 web type-stack (sans-only Inter) — designer reconciliation at SC-015 if dual-stack reads as a problem (from #17)
- FR-014: pin display + heading weight floors (e.g., ≥ 700 / ≥ 600) during planning so spec is fully testable without depending on SC-015 sign-off (from #17)
- FR-021: pin yellow-badge dark-text contrast ratio (e.g., ≥ 4.5:1 against fill) during planning (from #17)
- Reviewer-surface sponsor-strip chrome density vs visual real-estate — confirm with reviewer feedback if available (from #17)
- Image-only PDF strategy — refuse with clear "envíe un PDF con capa de texto" message vs OCR-then-redact pre-pass (from #18)
- Final AI model picks — Sonnet 4.6 extract + Opus 4.7 compare default; reconsider after token-cost estimate against sample application (from #18)
- Spreadsheet (.xlsx/.csv) ingestion in MVP — currently deferred; confirm whether basic text conversion belongs in MVP (from #18)
- Polling vs SignalR for "Generar todo" — polling chosen for MVP; reconfirm at plan time once Aspire+SignalR overhead is measured (from #18)
- Citation marker style — numeric superscripts mimicking source image; final visual + interaction (hover preview vs click-through) deferred to design pass during plan (from #18)
- DB-vs-file discrepancy reconciliation — default is "comparator gets both + flags it"; alternatives are silent DB-wins or file-wins (from #18)
- "Forzar regeneración total" UX placement — two-step (toggle Override → click Generate all) chosen; single-click composite admin action rejected as too easy to mis-fire (from #18)
- Token-cost dashboard scope — out of MVP; FR-H3 promises audit shape supports it; confirm aggregation dimensions at plan time (from #18)
- SC-012 measurement protocol — define how the 70% task-time reduction is measured (sample selection, who runs it, baseline definition) during plan (from #18)
- Domain behaviour methods on `ComparisonArtifact` and `ComparisonJob` (`IsStaleAgainst(InputDescriptor)`, `Reap()`, `RecordSuccess(...)`, `RecordFailure(...)`) to satisfy Constitution Principle II — flagged in REVIEW-SPEC.md (from #18)
- History table for compliance — does the team need an append-only audit trail of every AI output beyond the latest cached artefact, or is "latest only" acceptable forever? (from #18)
- Redaction list completeness — should the deny-list expand to banking info, CCSS account numbers, fiscal IDs of third parties before MVP ships, or stay at the 5 fields and revisit? (from #18)
- Multi-provider AI posture — any near-term need for OpenAI / Azure / Gemini (data residency, cost, customer requirement) that would push multi-provider into MVP? (from #18)
- Polling-path covering index — pin during planning whether composite indexes on `(ApplicationItemId)` and `(ApplicationId, Status)` are sufficient for `dbo.ComparisonArtifacts` / `dbo.ComparisonJobs`, or whether a covering index on the polling read path is also needed (from #18)
- Anthropic.SDK NuGet version pin and transitive supply-chain notes — new managed dependency; this spec is the approval vehicle per CLAUDE.md; reconfirm at plan time (from #18)
- Mailgun ToS unsubscribe footer (List-Unsubscribe header vs static `mailto:soporte@…`) — pin with Mailgun account owner during planning (from #19)
- SMTP-capture sidecar choice: smtp4dev (.NET-native, lighter) vs MailHog (Go, broadly used) — pin during planning (from #19)
- Real Mailtrap stays an opt-in dev override; SMTP-sidecar is the default — confirm during planning (from #19)
- Production sender email address — `no-reply@programa-semilla.cr` recommended; pin with ops (from #19)
- `MailKit` license posture: v3 (MIT) vs v4 (commercial) — confirm during planning (from #19)
- `APPLICATION_SUBMITTED` enum split: two values for the two recipient buckets (clean idempotency) vs one value with two template variants (resolver fan-out) — recommended: split (from #19)
- Confirm `Application.Folio` field exists and is populated by `Submit()` — pin against spec 001 / data-model (from #19)
- `NotificationOutbox` retention — 90 days for `Done`, 1 year for `DeadLetter` recommended (from #19)
- Future multi-replica worker scaling — correctness covered today (FR-004 + FR-020 + EC-008); throughput tuning deferred (from #19)
- Brand-grep gate scope for new email templates: source-`.cshtml` layer (recommended) vs render-time scan (from #19)
- Future in-app notifications / bell icon / SignalR / push / SMS / Slack — spec 021-email-notifications ships email-only; in-app channel still pending (carries forward from #08 / #11)
- Future stage-granular notification events (`STAGE_APPROVED`, `REVIEWER_ASSIGNED`, `REVIEWER_UNASSIGNED`, `COMMENT_ADDED`) — out of scope for 021-email-notifications; eligible for v2 if reviewer churn proves a real signal (from #19)
- Future user-facing notification-preferences UI / opt-out flow — deferred; OQ-001 may force a static unsubscribe-mailto footer in the interim (from #19)
- Future Mailgun bounce-webhook ingestion + suppression-list sync — deferred until Mailgun delivery telemetry justifies the loop (from #19)
- Appeal-message email cadence at scale (debounce/digest) if high-volume threads prove noisy — spec 028 v1 sends one email per message; deferred (from #19 revisit / 028 OQ-001)
- Inherited spec-021 OQ-011 participating-admin role-change predicate limitation applies to all 12 spec-028 events; fixing it is out of scope for 028 (from #19 revisit)
- Planning-pin — exact `/Applications/{id}/FundingAgreement/` sub-route per applicant CTA, and whether `APPEAL_MESSAGE_*` carries a message snippet or a bare "new message" cue (NFR-003 leans cue+CTA) (from #19 revisit / 028)
- Plantilla cardinality per Process — one-to-one (default) vs many-to-one; pin in `/speckit-plan` (from #20, OQ-1)
- Process closure freeze semantics on `FundingAgreement` (default = freeze) (from #20, OQ-2)
- Stage-expiry override granularity — per-Process only (default) vs also per-Plantilla (from #20, OQ-3)
- PublicCode rendering on legacy Funding Agreement PDF template (spec 018) — template field swap vs footnote (from #20, OQ-4)
- Reglamento + ejemplo file content ownership and authoring source — admin team vs Programa Semilla operations (from #20, OQ-5)
- Email-reminder cadence (T-72h / T-24h / expiry) — fixed (default) vs admin-configurable (from #20, OQ-6)
- SupplierAdmin scope — full CRUD on suppliers (default) vs validate-only-existing (from #20, OQ-7)
- Hint copy authorship for FR-020's initial set — designer / copywriter delivery pending (from #20, OQ-8)
- Process audit-event coverage extends `AdminAuditEvent` (spec 016 pattern) — pin in plan (from #20, OQ-9)
- Provincia *"Otro/Extranjero"* handling — block in UI (default) vs catalog row; revisit if foreign suppliers surface (from #20, OQ-10)
- Admin-override path for expired stage-windows — whether the HTTP 422 hard-block should be overridable from the admin panel (from #20)
- BCCR exchange-rate auto-fetch + Tropic AI quotation extraction — research-only in 021-feedback-session-may13; needs future brainstorm / spec to productize (from #20)
- Single-spec scope vs. architectural / UX split — stakeholder picked single-shot; reviewer brief flags this for stakeholder pushback (from #20)
- Include "Replace file" affordance on the Quotation/Edit page (one-stop editing) vs keep Replace on the Application/Edit row — default: keep on row (from #21, OQ-1)
- Emit `AdminAuditEvent` for applicant-initiated quotation edits vs stay silent like Item/Edit — default: silent for v1 (from #21, OQ-2)
- Deep-link the `RETURNED_TO_APPLICANT` email CTA to `Quotation/{id}/Edit` — defer to spec 021 email-template touch-up (from #21, OQ-3)
- Constitution OC-gate posture on Quotation Edit — single-actor / two-tabs-same-user; justify last-write-wins in `plan.md` Complexity Tracking or add a rowversion token (from #21, R-1)
- Shared partial file name for the extracted quote-fields fragment — `_QuotationFieldsForm.cshtml` working name; pin during planning (from #21)
- AI-cache invalidation race window with in-flight `ComparisonJob` for the same Item — pin during planning (from #21)
- Withdrawal reviewer-notification trigger — `UnderReview`-only (default) vs also still-pending `Submitted` vs never; confirm with stakeholders (from #22, OQ-11)
- Idempotency-key shape for `APPLICATION_WITHDRAWN_BY_APPLICANT` so it's distinct from other reviewer-bucket events for the same Application — pin during planning (from #22)
- Whether withdrawal should leave an applicant-visible "Retirada" trace vs. silently vanishing like a soft-delete — parked (from #22)
- Exact enumeration of all `confirm()` call sites + TempData message surfaces into a coverage matrix so SC-001/SC-002 are mechanically verifiable — pin during planning (from #23)
- Whether to introduce toast/confirm tag-helpers or partials to keep call sites DRY (from #23)
- Confirm the ~5 s success/info auto-dismiss interval, and that top-right placement reads well on narrow viewports vs header actions (from #23)

## Closed Threads

- Will version history be sufficient for audit needs, or will the Appeal spec need a Resolution entity? (from #02) — **Closed by #04**: no `Resolution` entity needed; appeal resolution is a state transition + audit entry.
- Post-signature regeneration lockout on the Funding Agreement (from #05) — **Closed by #06**: resolved as "regeneration permitted until first signed upload; locked thereafter; administrative back-out explicitly out of scope for this feature."
- Operational visibility for stuck applications with no deadlines — likely future reporting spec (from #04) — **Closed by #10**: Aging Applications report (US6) ships in spec 010, with configurable threshold (default 14 days, range 1–365) and per-row drill-in including "days in current state" and "last actor".
- Specific default locale code for LatAm formatting (e.g., `es-CO`, `es-MX`) — to be pinned during planning (from #05) — **Closed by #12**: pinned to `es-CR` (FR-016). Funding Agreement PDF format-separator shifts from `1.234,56` (es-CO) to `1,234.56` (es-CR) per CR business convention.
- Future localization-layer spec — partials must be checked to ensure no UI copy was embedded during the 008 sweep (from #08; reaffirmed by #11) — **Closed by #12**: executed during the localization sweep (User Story 2 + NFR-004); the partial-parameterization rule from spec 008 was preserved.
- Future localization-layer spec — voice-guide rewrites in spec 011 must keep copy out of partials' code paths to remain compatible (from #11; carries forward from #08) — **Closed by #12**: validated during the spec 012 sweep; voice-guide rewrites in 011 stayed compatible with the inline-replace pattern.
- Display brand name selection — Forge / Ascent / keep FundingPlatform — user sign-off gate (from #11) — **Closed by #12**: **Capital Semilla** chosen (FR-006). Display brand only; code namespaces, project names, and config keys remain `FundingPlatform`. — **Reopened and re-closed by #17**: display brand pivots again to **Programa Semilla** (spec 019 FR-001) to align with the actual sponsor-program identity from the funding-agreement PDF. Code namespaces still remain `FundingPlatform`.
- Future spec 010 (notifications & inbox) needs its own brainstorm — likely SignalR (from #08) — **Partially closed by #19**: email channel ships in spec 021 with outbox + worker + Mailgun + spec-019-branded templates. In-app inbox / SignalR remains open for a future spec.
- Future notifications & inbox / SignalR spec needs its own brainstorm — spec 011 deliberately excludes real-time push (from #11) — **Partially closed by #19**: email channel ships in spec 021. Real-time push / in-app inbox remains open.
- Email signature layout — text-only vs inline seedling mark; defaults to text-only (from #17) — **Closed by #19**: spec 021 ships text-only email signatures with no inline `<img>` (FR-023 + NFR-001), preserving spec 019 NFR-005 email-client compatibility.
- Whether `_ConfirmDialog` for every destructive action (including draft-item deletes) is the right baseline, or whether specific exceptions should be enumerated (from #08) — **Closed by #23**: spec 024 establishes one reusable styled confirmation modal for **all** current `confirm()` sites + any destructive action (FR-006); no per-site exceptions — only the mechanism changes, not which actions are guarded.
- Future signing-stage notification events (`AGREEMENT_GENERATED`, `SIGNED_PDF_UPLOADED`) — out of scope for 021-email-notifications; eligible for a follow-up (from #19) — **Closed by #19 revisit / spec 028**: post-resolution increment adds `AGREEMENT_GENERATED_APPLICANT`, `SIGNED_UPLOAD_SUBMITTED/REPLACED/WITHDRAWN_REVIEWER`, `AGREEMENT_EXECUTED_APPLICANT`, `SIGNED_UPLOAD_REJECTED_APPLICANT` plus the applicant-response and full appeal-lifecycle events — every post-`Resolved` applicant↔reviewer interaction now notified.

## Parked Ideas

- **Multi-agency & tenant isolation** (seed FR-130–136, NFR-006) for the financial-execution platform. (#41)
  Reason: user scoped it out for now — the program extends single-tenant Capital Semilla; revisit if other operating agencies onboard.
- **Mentori synchronization** (seed FR-145–149) — participant/company master-data sync with an external system of record. (#41)
  Reason: parked entirely; no slice. Requires Mentori API + data-ownership decisions not yet available.
- **Participant self-service financial portal** (balance view, direct document upload) — seed Phase 2. (#41)
  Reason: deferred to a later phase; P1–P9 are operator/auditor/admin-facing.
- **SBD live API integration + OCR document parsing + in-platform digital signature** — seed Phase 2 candidates. (#41)
  Reason: dependent on external schemas/providers; architecture kept API-ready but not built.
