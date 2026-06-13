# Phase 0 Research: Batch user creation

All decisions below are grounded in a codebase survey (see file:line references). No NEEDS CLARIFICATION remain.

## D1 — Reuse the single-create + invitation seam (do not re-implement)

**Decision**: The batch creates each valid row by calling the existing `UserAdministrationService.CreateUserAsync(CreateUserRequest, actorId, ct)` and then, for each created row, the existing controller helper `AdminUsersController.IssueAndSendInvitationAsync(email, ct)`. The batch actions live **on `AdminUsersController`** so the invitation helper is reused as-is.

**Rationale**:
- Spec 033 already removed the password from `CreateUserRequest` and made `CreateUserAsync` create a no-password account (`src/FundingPlatform.Infrastructure/Identity/UserAdministrationService.cs`, `_userManager.CreateAsync(user)` with no password; `MustChangePassword = false`). So a batch row needs no password — it is identical to single-create.
- All the guards we need are already inside `CreateUserAsync`: `AT_LEAST_ONE_GROUP`, `GROUP_NOT_FOUND`, Identity `EMAIL_IN_USE`, `LEGAL_ID_IN_USE`, `USER_CODE_IN_USE` (service pre-check + filtered-unique-index backstop). Reusing it means the batch inherits identical validation and the spec-032 uniqueness semantics for free.
- The invitation issuance (`IssueAndSendInvitationAsync`) composes an absolute `/Account/ResetPassword` link using `Url.Action` + `Request.Scheme/Host` (HTTP-context-bound) and best-effort sends via `_emailSender.SendAsync` with a 10s timeout, swallowing transport errors. This exactly matches FR-011 ("a failure to *send* must not fail the row"). Keeping the batch on the same controller avoids extracting HTTP-context-bound link building into a lower layer.

**Alternatives considered**:
- *Extract invitation issuance into an Application/Infrastructure service.* Rejected for v1: link composition needs `IUrlHelper`/`Request` (Web concern); extraction would force a `LinkGenerator`+scheme/host plumbing refactor with no current second consumer beyond batch. YAGNI (Constitution VI). Can revisit if a third caller appears.
- *New standalone `AdminUserBatchController`.* Rejected: it would need its own copy of (or a shared base for) `IssueAndSendInvitationAsync`. Same-controller actions are simpler.

**Per-row atomicity falls out for free**: `CreateUserAsync` performs its own `SaveChangesAsync` per call (and compensating `DeleteAsync` on partial failure). Looping it row-by-row gives "valid rows created, invalid skipped, never all-or-nothing" (FR/US2) without an explicit transaction span.

## D2 — In-house CSV parsing (no new dependency)

**Decision**: Add a minimal RFC-4180 reader `CsvParser` in `FundingPlatform.Application/Admin/Users/Batch/`. It returns a header row + data rows of `string[]`. It handles: comma delimiter, double-quote-wrapped fields, escaped `""` inside quoted fields, embedded commas/newlines inside quotes, CRLF/LF line endings, and a leading UTF-8 BOM (Excel "CSV UTF-8" export). Trailing empty lines are ignored.

**Rationale**: FR-014 forbids new NuGet packages. The repo has CSV *writing* only (`AdminReportsService.CsvLine`/`EscapeCsv`, RFC-4180 quoting) but **no parser** and no CsvHelper reference in any `.csproj`. A bounded ≤200-row admin upload does not justify a dependency; a small, unit-tested parser is sufficient and pure (Constitution I/VI).

**Alternatives considered**:
- `string.Split(',')`. Rejected: breaks on quoted fields containing commas (names, group labels) and BOM. The intake spreadsheet is Excel-exported, so quoting/BOM are realistic.
- Add CsvHelper. Rejected: violates FR-014 / Constitution tech-standards gate.

**Watch-items (from REVIEW-SPEC)**: BOM strip on the first field of the header; quoted fields with embedded commas/newlines; tolerate a trailing newline. These become explicit `CsvParserTests` cases.

## D3 — File-level vs row-level validation boundary

**Decision**: File-level checks reject the whole upload with one es-CR message and create nothing (FR-003): not a `.csv` / unreadable, header columns missing or not matching the template (order + names, case/accent-insensitive, BOM-tolerant), zero data rows, or > 200 data rows. A simple in-memory byte cap (e.g., reject absurdly large uploads before parsing) guards memory — the CSV is **not** stored, so no `FileCategory`/object-storage/`UploadSizeGuard` machinery is needed. Everything else is row-level (skip-and-report).

**Rationale**: A malformed file can't be meaningfully partitioned per row, so it fails as a unit. Row data problems must not block siblings (US2). Avoiding object storage keeps this simple (the file is transient).

**Decision on FR-003 message granularity** (REVIEW-SPEC optional item): report the **first** failing file-level condition with its specific es-CR message (e.g., "more than 200 rows" vs "columns don't match the template") rather than enumerating all file-level failures. Simpler and unambiguous; the conditions are mostly mutually exclusive.

## D4 — Phone normalization algorithm (FR-005)

**Decision**: New pure helper `PhoneNormalizer.Normalize(string? raw) -> string?` in Application:
1. If null/blank → return null (phone is optional).
2. Split the cell on common multi-number separators (`/`, `,`, `;`, `|`, and runs of whitespace between digit groups) and take the **first** non-empty token.
3. Strip all non-digit characters from that token.
4. If the result starts with `506` **and** is longer than 8 digits, drop the leading `506` (Costa Rica country code).
5. Return the remaining digits (empty → null).

**Rationale**: The spreadsheet sometimes carries a `506` prefix and sometimes multiple numbers; FR-005 says strip the prefix and keep the first. No server-side phone validator exists today (phone is stored as a free string on `Applicant.Phone`/`ApplicationUser.PhoneNumber`), so we only normalize — we do **not** reject a row for phone shape (phone is optional and non-identifying). Unit-tested with cases: `"8888-1111"`, `"506 8888 1111"`, `"+506 88881111"`, `"8888-1111 / 7777-2222"`, blank.

**Alternatives considered**: Reusing a spec-026 server normalizer — none exists (the spec-026 phone mask is client-side JS only). Strict E.164 validation — out of scope and would reject valid local-format numbers.

## D5 — Identification: validate + canonicalize as cédula física (FR-006)

**Decision**: For each row, validate the `Cédula` value with `Identification.TryFrom(IdentificationType.CedulaFisica, raw, out var id)` (`src/FundingPlatform.Domain/ValueObjects/Identification.cs`). On success, pass `id.Value` (canonical `#-####-####`) as `CreateUserRequest.LegalId` with `IdentificationType.CedulaFisica`. On failure, the row is errored with an es-CR reason.

**Rationale**: The cédula física regex is `^\d-\d{4}-\d{4}$` after `Canonicalize` regroups digits 1-4-4, so `"112345678"`, `"1 1234 5678"`, `"1-1234-5678"` all normalize identically — matching how the single-create form stores it. Reusing the value object guarantees batch and form parity (Constitution II). The requester fixed the type to cédula física for the whole batch (no type column).

## D6 — Group/Process/Fund name resolution is deterministic (FR-009)

**Decision**: Resolve each name with a single `FirstOrDefaultAsync` by `Name`:
- `Fondo` → `_dbContext.Funds.FirstOrDefault(f => f.Name == fondo)`
- `Proceso` → `_dbContext.Processes.FirstOrDefault(p => p.Name == proceso)`
- `Grupo` → `_dbContext.Groups.FirstOrDefault(g => g.Name == grupo)`

Then validate the chain: `group != null && process != null && fund != null && group.ProcessId == process.Id && process.FundId == fund.Id`. Any null or any broken link → row errored with an es-CR reason (unknown name vs chain-mismatch distinguished in the message). The resolved `group.Id` becomes the single entry in `CreateUserRequest.GroupIds`.

**Rationale**: `Funds.Name` (`UX_Funds_Name`), `Processes.Name` (`UX_Processes_Name`), and `Groups.Name` (`UX_Groups_Name`, column collation `Latin1_General_CI_AI` = case+accent-insensitive) are **all globally unique**. Therefore a name resolves to **0 or 1** row — FR-009's "ambiguous" cannot occur and collapses to "not found". Name comparison uses each column's DB collation (Group is CI+AI; Funds/Processes are CI by SQL Server default). Cell values are trimmed before matching.

**Decision on status gating** (REVIEW-SPEC item): v1 validates **structural coherence only** — it does **not** reject a row because the Fund is Archived or the Process is Closed. This matches single-create, which gates membership on group existence (`GROUP_NOT_FOUND`) but not on fund/process status. Gating batch membership on Active status is deferred (note in Out-of-Scope-adjacent assumptions); if the org needs it, it is a small follow-up.

## D7 — In-file duplicate handling (FR-008, "first wins")

**Decision**: Before creation, pre-scan the parsed rows and mark a row errored when its (case-insensitively normalized) `Email`, canonical `Cédula`, or trimmed `Código de usuario` was already seen in an **earlier** row of the same file. The first occurrence proceeds to creation; later duplicates are errored with an es-CR "duplicado en el archivo" reason.

**Rationale**: Although sequential `CreateUserAsync` calls would *also* reject a later in-file duplicate (the first row commits, so the second trips `EMAIL_IN_USE`/`LEGAL_ID_IN_USE`/`USER_CODE_IN_USE`), an explicit pre-scan yields a clearer, intent-specific es-CR reason ("duplicated in the file" vs "already in use in the system") and does not depend on commit ordering. Duplicates **vs. existing DB records** are caught by `CreateUserAsync`'s own pre-checks and mapped to their es-CR reasons.

## D8 — Result model + report

**Decision**: `CreateUsersBatchAsync` returns a `BatchUserCreateResult` partitioning rows into `Succeeded` (row number + email) and `Errored` (row number + key field + es-CR reason). The controller, after creating, issues invitations for succeeded rows, then renders `BatchResult.cshtml` showing both lists with counts. No CSV download, no invitation links shown (FR-012, v1).

**Rationale**: Matches FR-012 and the constitution "collect all validation errors and display at once" gate. Keeping invitation links out of the report is the requester's explicit v1 choice; a dropped email is recovered through the existing per-user resend (spec 033 `ResendInvitation`).

## Open questions

None. The three REVIEW-SPEC optional watch-items are resolved in D2 (CSV edge cases → parser tests), D3 (file-level message granularity → first failing condition), and D6 (name determinism + status-gating policy).
