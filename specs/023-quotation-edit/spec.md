# Feature Specification: In-place Quotation Field Edit

**Feature Branch**: `023-quotation-edit`
**Created**: 2026-05-20
**Status**: Draft
**Input**: Per-quotation in-place edit for Application owners while the Application is `Draft`. Editable fields: Price, Currency, ValidUntil, SupplierBranch (same supplier only). Reuse the existing Supplier/Add quote form as a shared partial. Surfaced after the May-13 stakeholder session uncovered that `/Application/Edit/{id}` exposes no per-quotation edit affordance.

## User Scenarios & Testing *(mandatory)*

<!--
  User stories below are ordered by importance. Each is independently testable and delivers
  applicant-facing value on its own slice of the surface.
-->

### User Story 1 — Applicant fixes a typo on a draft-stage quotation (Priority: P1)

An applicant has attached a supplier quotation to an Item on a Draft Application and notices a typo in the price (or the ValidUntil date) before submitting. Today the only recovery is Delete + re-attach the same PDF, which loses the original `CreatedAt`, breaks audit continuity, and forces re-uploading a document the applicant already has. With this feature, the applicant clicks an *Editar* button on the quotation row, lands on a form pre-populated with the current values, corrects the field, and saves — the quotation keeps its identity and history.

**Why this priority**: This is the highest-frequency case revealed during the May-13 feedback session and the originating bug report (URL `/Application/Edit/1003`). It blocks first-time applicants from completing a clean submit on their own.

**Independent Test**: Seed an Applicant, an Application in `Draft`, an Item, and one Quotation. Click *Editar* on the quotation row, change Price from `1500` to `1750`, save. Assert the same `Quotation.Id` is intact, `Price = 1750`, `CreatedAt` unchanged, `ConvertedCrcAmount` recomputed for CRC.

**Acceptance Scenarios**:

1. **Given** a Draft Application I own, with one quotation at Price `1500`, **When** I open *Editar* on that quotation, change Price to `1750`, and save, **Then** I return to the Application page, the row shows `1750`, the CRC subtotal updates, and `CreatedAt` is unchanged.
2. **Given** the same draft, **When** I edit *only* ValidUntil to a future date, **Then** the change persists with no exchange-rate or comparison-cache churn.
3. **Given** the same draft, **When** I submit the Edit form with Price `0`, **Then** the page re-renders with a field-level es-CR error and no DB write occurs.

---

### User Story 2 — Applicant applies a reviewer-requested correction (Priority: P1)

A reviewer returns an application with feedback identifying an error on a specific quotation (wrong amount, wrong validity window, wrong supplier branch). The reviewer `SendBack` path transitions the Application **back to `Draft`** (the codebase has no distinct `ReturnedForChanges` state — see the lifecycle note under FR-008). The applicant must be able to fix the offending quotation *without* deleting it (which would orphan the reviewer's notes that referenced it) and resubmit. With this feature, the Edit surface is available because the returned Application is once again in `Draft`, so the correction is in-place.

**Why this priority**: The May-13 stakeholders explicitly called out this loop ("reviewer finds an error, we have no way to fix it without losing the quote"). This is the second-highest priority because corrections gate the resubmit path.

**Independent Test**: Seed an Application that a reviewer returned via `SendBack` (now back in `Draft`) with two quotations. Edit one quotation's branch to a different branch of the same supplier. Assert the change persists; assert the reviewer's existing feedback on that quotation is preserved (no soft-delete cycle).

**Acceptance Scenarios**:

1. **Given** my Application was returned by a reviewer and is now back in `Draft`, **When** I open *Editar* on a quotation and change the SupplierBranch to a different branch of the **same** Supplier, **Then** the change persists and the quotation row reflects the new branch.
2. **Given** the same state, **When** I try to submit the Edit form with a branch belonging to a different Supplier, **Then** the server rejects with a generic *"Sucursal no válida para este proveedor."* validation error.
3. **Given** the same state, **When** the reviewer's prior comments on this quotation exist, **Then** they remain attached after the edit.

---

### User Story 3 — Applicant changes currency on an attached quotation (Priority: P2)

A quotation was attached in CRC but the applicant realises the underlying supplier invoice is in USD (or vice versa). They edit the Currency field, the system attaches a fresh exchange-rate snapshot, marks the source rate as used (spec 015), and recomputes the CRC-equivalent. The applicant sees the live conversion preview immediately, the same way they do on the create form.

**Why this priority**: Less frequent than P1 typo fixes, but the alternative (Delete + re-Convert) is materially worse because it loses the original `CreatedAt`, breaks audit continuity, and consumes a new rate snapshot anyway. So we want to support it cleanly, but it does not block any submit on its own.

**Independent Test**: On a Draft Application with one CRC quotation at Price `100`, edit Currency to `USD` and Price to `100`. Assert a fresh `ExchangeRateSnapshot` is attached, `ConvertedCrcAmount` reflects the current published USD→CRC rate, `LegacyNeedsReview = false`, and the consumed `ExchangeRate.IsUsed = true`.

**Acceptance Scenarios**:

1. **Given** a CRC quotation, **When** I change the currency to USD and save, **Then** the system snapshots the current published USD→CRC rate, persists it on the quotation, and shows the new CRC-equivalent on the row.
2. **Given** there is no published rate for the requested currency at save time, **When** I submit, **Then** the server responds with the existing missing-exchange-rate user-facing message and the quotation is unchanged.
3. **Given** an existing artifact cached the AI comparison for this Item (spec 020), **When** my edit succeeds, **Then** the cache key for this Item is silently invalidated and the next reviewer-side *Generar todo* run regenerates.

---

### Edge Cases

- The quotation was deleted (by the applicant in another tab) between rendering the Edit form and submitting — the POST resolves to a 404; the form re-renders with an es-CR notice.
- The Application transitioned out of `Draft` (e.g., the applicant submitted it, or a reviewer pulled it under review) between GET and POST — the POST returns HTTP 422 with es-CR copy *"El estado de la solicitud cambió, recarga la página."*; no state mutation.
- Two browser tabs submit conflicting edits — last write wins. Optimistic concurrency is out of scope (matches existing Item/Edit behavior).
- Currency change AND price change in the same POST — processed atomically: snapshot is reset to the fresh rate first, then the new price is applied against that snapshot.
- The quotation is flagged `LegacyNeedsReview = true` (spec 015 legacy data) — the Edit affordance is hidden in the UI, and a direct POST is rejected with HTTP 422. The admin-fix path under spec 015 remains the only resolution.
- Double-click on Save — anti-forgery validation + idempotent server logic prevent duplicate state changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose a per-quotation Edit affordance reachable from each quotation row on `Application/Edit` (and on `Item/Edit` if it lists quotations).
- **FR-002**: The Edit form MUST pre-populate with the quotation's current Price, Currency, ValidUntil, and SupplierBranch.
- **FR-003**: The Edit form MUST reuse the same set of Price / Currency / ValidUntil input controls used on the Supplier/Add create-quote surface, via a shared template fragment. Refactoring Supplier/Add to consume the fragment is in scope; behavioral changes to Supplier/Add are out of scope.
- **FR-004**: The Branch picker on the Edit surface MUST list only branches belonging to the quotation's current Supplier. Switching to a different Supplier is not permitted via Edit — applicants Delete + re-attach for that case.
- **FR-005**: On save, the system MUST enforce: Price > 0, Currency is a 3-letter ISO code present in the enabled `Currencies` catalog, ValidUntil ≥ today (es-CR calendar), SupplierBranchId belongs to the quotation's current Supplier.
- **FR-006**: Persistence routing MUST honor the multi-currency snapshot rules from spec 015:
  - Currency unchanged → re-apply the existing pinned snapshot (CRC short-circuits to `ConvertedCrcAmount = Price`; non-CRC re-multiplies by the pinned rate).
  - Currency changed → reset the snapshot, take a fresh one from the current published rate, mark that rate as used, and recompute `ConvertedCrcAmount`.
  - Branch and ValidUntil persist independently with no exchange-rate side effects.
  - The attached PDF is never replaced via this surface — the existing file-Replace endpoint remains the only path for that.
- **FR-007**: Only the Application owner (Applicant role) MAY edit. Non-owner requests MUST be rejected with HTTP 403.
- **FR-008** *(evolution 2026-05-22 — state reconciliation)*: Edits are permitted iff the Application is in `Draft`. Any other state MUST reject the POST with HTTP 422 and an es-CR error message. The Edit affordance MUST NOT render in the UI when the Application is outside `Draft`. **Lifecycle note**: earlier drafts of this spec named a `ReturnedForChanges` state, but `ApplicationState` (`Draft, Submitted, UnderReview, Resolved, AppealOpen, ResponseFinalized, AgreementExecuted`) has no such member. The reviewer return path (`SendBack`) transitions the Application back to `Draft`, so the reviewer-return → applicant-fix loop (US2) is fully covered by the single `Draft` gate; the implementation gates on `application.State == ApplicationState.Draft`.
- **FR-009**: Successful edits MUST invalidate the AI comparison cache (spec 020) keyed against this Item. No applicant-facing notice is shown; the reviewer's next *Generar todo* picks up the cache miss and regenerates.
- **FR-010**: The live conversion-preview behavior already used on Supplier/Add MUST be available on the Edit form unchanged — on Currency / Amount blur, the server-computed preview updates the displayed CRC-equivalent.
- **FR-011**: Quotations whose `LegacyNeedsReview` flag is set MUST NOT expose the Edit affordance, and the POST MUST reject with HTTP 422. (Spec 015 admin-only path remains the resolution.)
- **FR-012** *(evolution 2026-05-20)*: The per-quotation row on `Application/Edit` MUST surface the existing `Replace` (Reemplazar) and `Delete` (Eliminar) affordances alongside the new `Edit` (Editar) button, with the same lifecycle gate (Application in `Draft` — see FR-008) and the same `LegacyNeedsReview` hiding rule. The endpoints themselves (`POST …/Quotation/{id}/Replace`, `POST …/Quotation/{id}/Delete`) are pre-existing; this requirement is solely about restoring the row-level UX that the prior in-place-edit rollout dropped.
- **FR-013** *(evolution 2026-05-20)*: The Application owner (Applicant role) MUST be able to download the PDF attached to any of their own quotations at any time, regardless of Application state. The Edit, Details, and (future) Review applicant-facing surfaces MUST each expose a `Descargar` affordance on every quotation row. The endpoint MUST reject non-owner Applicants with HTTP 403 and unknown quotations with HTTP 404. Downloads are routed through the existing spec-014 `IObjectStorage` resolver (signed URL when configured; backend stream fallback), and the returned filename MUST be the `Document.OriginalFileName`.
- **FR-014** *(evolution 2026-05-20)*: A Reviewer scoped to the Application (per spec 016 group overlap) and an Admin user MUST be able to download the PDF attached to any quotation on that Application from the reviewer screen (`Review.cshtml`) at any time, irrespective of whether the AI comparison has run. The endpoint reuses the same spec-014 storage rails as FR-013 and the same group-overlap auth predicate as the spec-020 citation download. Non-scoped Reviewers MUST receive HTTP 403.

### Non-Functional Requirements

- **NFR-001 i18n.** All copy in es-CR; field labels and validation messages mirror the Supplier/Add surface for consistency.
- **NFR-002 Accessibility.** Form is keyboard-navigable; required-field markers per the spec 021 convention; labels are programmatically associated with inputs.
- **NFR-003 Performance.** Edit-form render p50 ≤ 200 ms server-side; save round-trip p50 ≤ 500 ms (no PDF stream involved).
- **NFR-004 Idempotency.** A repeat POST with identical values is a no-op — no second exchange-rate snapshot is taken, no audit duplication.
- **NFR-005 No new managed dependencies.** Reuses the existing Supplier/Add JS bundle and existing domain primitives (`EditAmount`, `ChangeCurrencyAsync`).

### Key Entities

- **Quotation** *(existing, spec 013/015)*: gains a per-row Edit affordance on this spec. No schema change. Existing domain primitives `EditAmount(price)` and `ChangeCurrencyAsync(currency, conversion)` are the persistence rails.
- **SupplierBranch** *(existing, spec 013)*: branch-supplier invariant is enforced on the Edit POST; cross-supplier branch IDs are rejected.
- **ExchangeRateSnapshot** *(existing, spec 015)*: a new snapshot is taken on every currency change; the source `ExchangeRate.IsUsed` flag flips to `true` per spec 015 FR-008.
- **ComparisonArtifact** *(existing, spec 020)*: the cache key resolves over the Item's quotations; any successful Edit on a member quotation invalidates the cache for that Item.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Applicant on Application 1003 in `Draft` edits a quote's price from `1500` to `1750`; the row reflects `1750` and the recomputed CRC-equivalent; `Quotation.Id` and `CreatedAt` are unchanged.
- **SC-002**: Applicant on a `Draft` Application changes a quote's currency from CRC to USD; a fresh `ExchangeRateSnapshot` is attached; `LegacyNeedsReview = false`; the consumed `ExchangeRate.IsUsed = true`.
- **SC-003**: A non-owner Applicant who hits the Edit URL receives HTTP 403; an audit trail entry exists per the existing 016-pattern.
- **SC-004**: A POST to Edit when the Application is `UnderReview` is rejected with HTTP 422 and es-CR copy; the Edit affordance is not rendered on that surface.
- **SC-005**: The existing Supplier/Add end-to-end suite remains green after extraction of the shared quote-fields fragment (zero regression).
- **SC-006**: On a successful Edit against an Item that had a cached `ComparisonArtifact`, the next reviewer *Generar todo* run reports a cache miss, regenerates, and writes a new artifact hash.
- **SC-007**: Submitting the Edit form with a SupplierBranchId that belongs to a different Supplier returns HTTP 400 / a validation error; no state mutation occurs.
- **SC-008**: Submitting the Edit form with Price `0` or a negative number returns a field-level es-CR error; no DB write.

## Assumptions

- The Application owner is the only actor who needs Edit; Admin, Reviewer, and SupplierAdmin do not need this affordance in v1 (confirmed during brainstorm).
- AI-comparison cache invalidation is *silent* — the reviewer regenerates on demand. We accept the trade-off that a reviewer reading a stale comparison artifact before regeneration will not see a freshness banner.
- Quotations carry no optimistic-concurrency token in v1 (last-write-wins). Matches the prevailing pattern on Item/Edit and other applicant-facing surfaces.
- Branch within the same supplier covers the user need surfaced on May-13. Cross-supplier "swap" is intentionally deferred (Delete + re-Convert remains the path).
- All quotation field changes are auditable through the existing application-event stream; no new admin-audit event type is introduced in v1.

## Dependencies

- **Spec 013** — Supplier / SupplierBranch invariant (`branch.SupplierId == quotation.SupplierId`).
- **Spec 015** — Multi-currency snapshot primitives (`EditAmount`, `ChangeCurrencyAsync`, `IConversionService`, `ExchangeRate.MarkUsed`).
- **Spec 020** — `ComparisonArtifact` cache-key recompute hook (silent invalidate).
- **Spec 021** — Lifecycle state `Draft` (reviewer return via `SendBack` transitions back to `Draft`; no distinct `ReturnedForChanges` state exists — see FR-008) and the on-blur autosave + required-marker UI conventions.

## Out of Scope

- Editing the PDF document via this surface — the existing Replace endpoint remains the file-swap path.
- Switching to a different Supplier on the Edit form (applicant Deletes + re-Converts).
- Editing on `UnderReview`, `Approved`, or `FundingAgreement-issued` states.
- Admin or Reviewer or SupplierAdmin editing of applicant quotations.
- Optimistic concurrency tokens / multi-tab conflict resolution beyond last-write-wins.
- Editing quotes flagged `LegacyNeedsReview = true` (admin-only path per spec 015 stays).
- Foreign supplier addresses; cross-province branch changes outside the CR catalog.
- New `AdminAuditEvent` instrumentation for applicant-initiated quotation edits.

## Open Questions

- **OQ-1**: Should the Edit surface also expose a *Replace file* affordance for one-stop editing, or keep file-Replace strictly on the Application/Edit row? Default: keep Replace on the row.
- **OQ-2**: Should a future iteration emit an `AdminAuditEvent` for applicant-initiated quotation edits (parity with admin user-management mutations), or stay silent like Item/Edit? Default: silent for v1.
- **OQ-3**: When the reviewer's returned-for-changes email cites a specific quotation, should the email CTA deep-link to `Quotation/{id}/Edit` directly? Defer to the spec 021 email-template touch-up.
