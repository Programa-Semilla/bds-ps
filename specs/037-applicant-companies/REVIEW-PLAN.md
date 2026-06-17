# Review Guide: Applicant Companies (037)

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-17

---

## What This Spec Does

Applicants currently type their company name as free text when starting a funding application, which produces messy, unvalidated data. This feature replaces that free-text box with a dropdown limited to companies an **administrator** has assigned to that applicant. It introduces a small new admin-managed "Company" concept (just a name, for now) and makes sure each application keeps showing the company name it was created with, even after an admin later corrects that name.

**In scope:** a `Company` record per applicant (admin-only); the applicant company dropdown on creation (auto-select when one, choose when many, block when none); admin create-with-≥1 / add / rename / soft-archive / unarchive with a "can't archive the last one" floor; a required company column on the bulk-import CSV; a per-application name snapshot for history; server-side ownership checks.

**Out of scope** (see [spec Out of Scope](spec.md#out-of-scope)): company attributes beyond a name; applicant self-service; any backfill of existing data; changes to reviewer/admin application screens beyond still showing the snapshot; linking Company to suppliers or to the legal ID. The backfill exclusion is the boundary most worth a second look — it means existing applicants start with zero companies and can't submit until an admin adds one.

## Bigger Picture

This sits directly on top of three recent features: spec 029 (the Group→Fund anchor, whose 0/1/many selection UX this feature copies for the company dropdown), spec 031 (the `data-searchable` dropdown enhancer it reuses), and spec 034 (the bulk-import CSV it extends). The `Company` aggregate is modeled on the spec 029 `Fund` and spec 036 `FundsUsageEvidence` patterns, and admin management mirrors `FundService`. So this is more "thread a new owned entity through proven seams" than "new subsystem." The one genuinely new bit of judgment is how history is preserved (a name snapshot, [research D2](research.md#d2--historical-preservation-snapshot--reference-reuse-companyname)) — everything else has a close precedent in the codebase.

---

## Spec Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [spec Purpose + User Story 1/2](spec.md#user-scenarios--testing) and [research D2](research.md#d2--historical-preservation-snapshot--reference-reuse-companyname). As you read, consider:

- The whole design hinges on reusing the existing `CompanyName` column as a frozen *snapshot* and adding a nullable `CompanyId` reference. Does storing the name per-application (rather than always resolving it live) match how you'd expect "preserve the name at time of submission" to behave for audit/legal purposes?
- Company choice is treated as a **foundational** attribute of an application (like its Group anchor) — chosen at creation, editable only while `Draft`, frozen at submit ([FR-015/016](spec.md#requirements-mandatory)). Is that the right mental model, or should company be freely editable like ordinary content?
- The feature is deliberately scoped to the Applicant role only. Anything about reviewer/admin application views you'd expect to change that the spec says stays the same?

### Key decisions that need your eyes (12 min)

**Greenfield, no backfill** ([research D9](research.md#d9--migration-safety-nullable-fk-no-anchor-script-needed), [plan Assumptions](spec.md#assumptions))
The `CompanyId` FK is nullable, so existing applications stay valid and there's no migration script. The cost: every *existing* applicant has zero companies and is blocked from new submissions until an admin adds one.
- Question: is that operationally acceptable, or do you want a one-time backfill (create a company from each applicant's past application names) tracked as a fast-follow?

**Soft archive + last-active floor** ([research D5](research.md#d5--last-active-company-floor-fr-008-lives-in-the-service))
Companies are never hard-deleted; admins archive/unarchive, and the system blocks archiving an applicant's last active company. The floor lives in the service (not the entity) because it must count *other* active companies.
- Question: is archive-only the right call, or do you also want hard-delete for a never-used, mistakenly-created company?

**Per-applicant name uniqueness is partly app-enforced** ([research D3](research.md#d3--per-applicant-active-name-uniqueness-fr-003))
A filtered unique index backstops exact/case duplicates, but **accent-insensitivity** is enforced in application code (the index alone is accent-sensitive under the DB collation). This matches the spec 029/030 precedent where the duplicate-race path is E2E-only.
- Question: is app-level accent handling acceptable, or do you want a persisted normalized column + index for a hard DB guarantee?

**Two admin write seams** ([research D4](research.md#d4--admin-management-write-paths-two-seams))
At-creation companies are attached inside `CreateUserAsync` (same `SaveChanges` as the Applicant, forced by the retrying-execution-strategy constraint), while post-creation add/rename/archive/unarchive go through a separate `CompanyAdministrationService`.
- Question: does splitting create-time vs. post-creation across two code paths read as clean, or would you prefer one service owning both (accepting the transaction constraint)?

**Admin UI placement** ([research D12](research.md#d12--admin-ui-placement-spec-open-question-resolved))
Resolved to live on the existing user Create (repeatable inputs) and Edit ("Empresas" card) pages rather than a dedicated company sub-page.
- Question: is co-locating company management on the user Edit page the right ergonomics for admins who manage many applicants?

### Areas where I'm less certain (5 min)

- [tasks T005/T006](tasks.md#phase-2-foundational-blocking-prerequisites): the `Application` constructor change (`companyName` → `companyId, snapshot`) is a compile-driven ripple across `src/` and `tests/`. I've scoped it as one task, but the true blast radius (how many `new Application(...)` builders exist in unit tests) isn't fully enumerated until the compiler runs — it could be larger than it looks.
- [FR-020 at submit](spec.md#requirements-mandatory) ([tasks T035](tasks.md#phase-5-user-story-3--historical-company-names-are-preserved-priority-p2)): I placed the "selected company still active at submit" check in `ApplicationService` (keeping domain `Submit()` pure). That's a judgment call about where a cross-aggregate check belongs — a reviewer favoring rich-domain purity might want it modeled differently.
- The autosave field-key swap from `CompanyName` to `CompanyId` ([tasks T033/T034](tasks.md#phase-5-user-story-3--historical-company-names-are-preserved-priority-p2)) removes a field-key. If anything outside this feature posts `CompanyName` via autosave, it would now 400 — I believe the Edit page is the only producer, but that's an assumption.

### Risks and open questions (5 min)

- If an admin archives a company while an applicant has it selected on a `Draft`, the snapshot is preserved but the applicant must re-pick before submitting ([FR-020](spec.md#requirements-mandatory)). Is forcing a re-pick (vs. silently allowing the archived choice to submit) the behavior you want?
- The seed gives the demo applicant **two** companies so the multi-select path is the default in dev/E2E ([data-model Seed](data-model.md#seed-data-demo--e2e)). Tests that need the single/zero paths SQL-seed throwaway applicants — is that acceptable test ergonomics, or should there be a dedicated single-company demo user?
- Batch import attaches exactly one company per row from `Nombre de la empresa` ([contracts batch](contracts/interfaces.md#batch-csv-contract-fr-009)). Is one-company-per-imported-applicant sufficient, or will real client files ever carry multiple companies per applicant?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [research](research.md).*
