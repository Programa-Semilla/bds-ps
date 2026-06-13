# Review Guide: Batch user creation (bulk applicant provisioning via CSV)

**Spec:** [spec.md](spec.md) | **Plan:** [plan.md](plan.md) | **Tasks:** [tasks.md](tasks.md)
**Generated:** 2026-06-12

---

## What This Spec Does

It lets an administrator upload one CSV — the org's existing intake spreadsheet — and provision up to 200 applicant (Solicitante) accounts at once, instead of re-keying them one by one. Each valid row becomes an account that receives the same emailed set-password invitation introduced in spec 033 (so admins never handle passwords). Invalid rows are skipped and reported, so one bad cell never blocks a whole cohort.

**In scope:** an admin-only `/Admin/Users/Batch` upload + a downloadable template; per-row validation/normalization; reusing the existing single-create path and invitation; a succeeded/errored report; name-resolving the `Grupo` to a group membership while using `Proceso`/`Fondo` only to validate the spec-029 chain.

**Out of scope (worth a reviewer's eye):** native `.xlsx`; a downloadable result report; invitation links in the report; batch edit/delete; non-applicant roles; async/background processing. These are deliberate v1 cuts — see [Out of Scope](spec.md#out-of-scope). Do any of them feel like they *must* be in v1 for the org's real workflow?

## Bigger Picture

This is the third step of an admin-provisioning arc: [spec 032](spec.md#dependencies) closed public self-registration and added the unique applicant User Code; [spec 033](spec.md#dependencies) replaced the admin-typed temp password with an emailed 72h set-password invitation. 034 simply scales 033's single-create to a batch. Because it reuses `CreateUserAsync` + the invitation helper verbatim, its risk surface is mostly *input handling* (CSV parsing, normalization, name resolution), not account mechanics. The intake format is real — it's the spreadsheet the program already maintains (`brainstorm/seeds/csv_screenshot.png`).

---

## Plan Review Guide (30 minutes)

### Understanding the approach (8 min)

Read [plan.md Summary](plan.md#summary) and [research.md D1](research.md#d1--reuse-the-single-create--invitation-seam-do-not-re-implement). The core bet is "reuse, don't rebuild": loop the existing single-create path per row, then issue the existing invitation per created user.

- Is looping `CreateUserAsync` per row (one commit each) the right way to get "per-row atomic, never all-or-nothing", versus a single batched transaction? (It trades a little efficiency for isolation and reuse — see [research.md D1](research.md#d1--reuse-the-single-create--invitation-seam-do-not-re-implement).)
- The batch actions live **on `AdminUsersController`** so the HTTP-context-bound invitation link builder is reused as-is, rather than extracted to a lower layer. Reasonable, or would you rather see `IssueAndSendInvitationAsync` extracted now? (See the alternative in [research.md D1](research.md#d1--reuse-the-single-create--invitation-seam-do-not-re-implement).)

### Key decisions that need your eyes (12 min)

**In-house CSV parser, no new dependency** ([research.md D2](research.md#d2--in-house-csv-parsing-no-new-dependency))
FR-014 forbids new NuGet packages, and the repo only has CSV *writing* today. The plan hand-rolls an RFC-4180 *subset* reader.
- Is a hand-rolled parser the right call for an Excel-exported file, or is the no-deps rule worth revisiting for a vetted CSV library? The risk lives entirely in [T003/T012](tasks.md#phase-2-foundational-blocking-prerequisites) — are the enumerated edge cases (BOM, quoted commas/newlines, CRLF) enough?

**`Proceso`/`Fondo` are validation-only; only `Grupo` is persisted** ([data-model.md](data-model.md#reused-persisted-entities-unchanged))
The chain columns guard against filing someone under a group that belongs to the wrong process/fund, but nothing about them is stored.
- Given all three names are globally unique in the DB, the spec's "ambiguous name" case collapses to "not found" ([research.md D6](research.md#d6--groupprocessfund-name-resolution-is-deterministic-fr-009)). Does that simplification hold for your data, or do you expect name collisions the index would actually reject at creation time?

**Chain validates structure, not status** ([research.md D6](research.md#d6--groupprocessfund-name-resolution-is-deterministic-fr-009))
v1 does **not** reject a row because its Fund is Archived or Process is Closed — it matches single-create, which only checks the group exists.
- Is it acceptable to batch-add members to a group under an archived fund, or should v1 gate on Active status? This is the decision most likely to need a product call.

**"First occurrence in the file wins" for in-file duplicates** ([research.md D7](research.md#d7--in-file-duplicate-handling-fr-008-first-wins))
- The requester confirmed this, but is "first wins, later errored" the behavior an operator expects when they accidentally paste a code twice — or would they rather the whole file be rejected until they fix it?

### Areas where I'm less certain (5 min)

- **Header matching strictness** ([T001](tasks.md#phase-1-setup-shared-infrastructure)): I assumed the template header must match by name + order (trim/case/accent-insensitive, BOM-tolerant). If operators reorder or rename columns in Excel, every upload would be a file-level rejection. Is order-strict matching too brittle, or is a fixed template the right discipline?
- **Phone normalization rules** ([research.md D4](research.md#d4--phone-normalization-algorithm-fr-005)): I inferred the separator set (`/ , ; |` + whitespace) and the "drop `506` only when length > 8" guard from one screenshot. Real cells may carry extensions or `+506` variants I haven't seen — worth confirming against a wider sample.
- **File-level message granularity** ([research.md D3](research.md#d3--file-level-vs-row-level-validation-boundary)): I chose to report the *first* failing file-level condition, not all of them. Fine, or do operators need to see (say) both "wrong columns" and ">200 rows" at once?

### Risks and open questions (5 min)

- If an admin uploads 200 rows and 60 are valid, 200 sequential `CreateUserAsync` calls + up to 60 best-effort emails run inside one request ([FR-001](spec.md#requirements)). Is the synchronous-within-request choice safe under the real environment's email-send latency, or should there be a guard (the invitation send already has a 10s per-message timeout)?
- Invitation emails to real applicant addresses are dropped by the non-Production allowlist ([dependencies → spec 021](spec.md#dependencies)). In dev/test the report will say "succeeded" but no mail arrives — is that surprising enough to warrant a note in the UI, given the v1 report intentionally omits the per-user link?
- The report omits invitation links by request ([FR-012](spec.md#requirements)). If a cohort's emails silently fail in prod, recovery is per-user resend across (up to) 200 users. Is that operationally acceptable for v1?

---
*Full context in linked [spec](spec.md), [plan](plan.md), and [tasks](tasks.md).*
