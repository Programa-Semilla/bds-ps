# Feature Specification: Suppliers Quotes Multi-Currency

**Feature Branch**: `feature/multi-currency`
**Created**: 2026-05-06
**Status**: Draft
**Input**: User description: "Suppliers Quotes Multi-Currency — Administrators can configure enabled currencies (CRC base, USD optional) and enter periodic CRC↔USD reference exchange rates with buy/sell values. Applicants can create supplier quotes in CRC or USD; non-CRC quotes show an immediate CRC conversion preview using the latest rate, and the applied rate is snapshotted onto the quote so historical values are immutable. The platform displays both original and converted CRC amounts everywhere quote/request totals appear. The final agreement PDF shows CRC only with a conversion indicator + applied rate/date when any line originated in non-CRC currency."

## Clarifications

### Session 2026-05-06

- Q: How is the CRC↔USD reference rate stored — CRC per 1 USD, or USD per 1 CRC? → A: CRC per 1 USD (Costa Rican banking convention; conversion of USD amount to CRC is `usd_amount × buy_rate`).
- Q: Are administrators allowed to publish a rate with an effective timestamp in the future? → A: No. Effective timestamp must be ≤ "now" at save time; future-dated rates are rejected. Rates take effect immediately upon save.
- Q: After an administrator disables USD, can users still edit existing USD quotes (e.g., change the amount)? → A: Yes. Disabling USD only blocks selecting USD on new quotes; editing the amount on existing USD quotes remains allowed and re-applies the original rate snapshot.
- Q: Can the currency be changed on a supplier quote after it has been saved (e.g., switch a CRC quote to USD or vice versa)? → A: No. Currency is fixed at save time. To "change currency", the user must delete the quote and create a new one. This preserves snapshot semantics and avoids retroactive conversion ambiguity.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Applicant creates a supplier quote in USD with deterministic CRC conversion (Priority: P1)

An applicant adding a supplier quote to a funding request can pick USD as the quote currency, enter the amount, and immediately see the converted CRC amount based on the most recent reference exchange rate. When they save the quote, the system snapshots the rate onto the quote so the converted CRC value is stable for the life of the request.

**Why this priority**: Core value of the feature. Without this, the platform has no usable multi-currency intake. Every other story (display, totals, PDF, admin) depends on quotes being captured correctly with locked-in conversion data.

**Independent Test**: With at least one published CRC↔USD reference exchange rate available, an applicant can pick USD on the quote form, type an amount (e.g., 1000 USD), see the CRC preview update in real time using the buy rate, save the quote, and reload the quote detail to confirm both 1000 USD and the converted CRC amount are persisted along with the rate snapshot.

**Acceptance Scenarios**:

1. **Given** USD is enabled and at least one CRC↔USD rate is published, **When** an applicant selects USD on the quote form and enters an amount, **Then** the CRC preview updates immediately using the latest buy rate, and the applied rate (value, type, effective timestamp) is shown read-only next to the preview.
2. **Given** the applicant saves a USD quote, **When** the system persists it, **Then** it stores the original amount + USD currency, the converted CRC amount, and a snapshot of the applied rate (value, type=Buy, effective timestamp, rate record id).
3. **Given** an existing USD quote with a rate snapshot, **When** an administrator publishes a new CRC↔USD rate later, **Then** the existing quote's converted CRC amount and snapshotted rate do not change on read.
4. **Given** USD is enabled but no CRC↔USD rate has ever been published, **When** an applicant selects USD on the quote form, **Then** the form blocks saving with the message "No reference exchange rate is configured. Contact an administrator." and no quote is created.

---

### User Story 2 - Applicant creates a supplier quote in CRC (Priority: P1)

An applicant can still create a supplier quote in CRC (the base currency) without ever interacting with conversion logic. Existing CRC-only flows must keep working.

**Why this priority**: Backwards compatibility for existing flows. CRC is the dominant case today and must not regress.

**Independent Test**: An applicant picks CRC on the quote form, enters an amount, saves, and confirms the quote is stored as currency=CRC with original = converted, no rate snapshot indicator, and no conversion-applied note in any downstream display.

**Acceptance Scenarios**:

1. **Given** CRC is selected on the quote form, **When** the applicant enters an amount, **Then** no CRC conversion preview appears (because it is identical to the original) and no rate snapshot is shown.
2. **Given** a saved CRC quote, **When** anyone views the quote detail, request summary, or final agreement PDF, **Then** the displayed CRC value equals the original entered amount and no "conversion applied" indicator appears.

---

### User Story 3 - Administrator manages enabled currencies and exchange rates (Priority: P1)

An administrator can: (a) enable or disable USD (CRC is permanently enabled and cannot be disabled); (b) enter a new CRC↔USD reference exchange rate with buy and sell values and an effective timestamp. New rates immediately supersede the previous one for new quotes. Rates that have ever been used by a quote cannot be edited or deleted; they can only be superseded by entering a new rate.

**Why this priority**: Without an administrator path to publish rates, no USD quote can be created. This is a precondition for User Story 1.

**Independent Test**: An administrator opens the currency-configuration screen, toggles USD off then on, then opens the exchange-rate screen, creates a new rate (buy=520.00, sell=525.00, effective=now), and observes the new rate becomes the active rate while the previous rate is preserved as historical.

**Acceptance Scenarios**:

1. **Given** an administrator on the currency-configuration screen, **When** they attempt to disable CRC, **Then** the action is blocked with a message indicating CRC is the system base currency.
2. **Given** an administrator on the exchange-rate screen, **When** they enter buy=0, sell=0, or any negative value, **Then** the form blocks save with a validation error.
3. **Given** an administrator submits a new valid CRC↔USD rate, **When** the rate is saved, **Then** every new USD quote created afterwards uses this rate, and the previously active rate remains visible in the rate history list with its effective period.
4. **Given** a rate that is referenced by at least one saved quote, **When** an administrator attempts to edit or delete it, **Then** the system rejects the action with an "in use, supersede only" message.
5. **Given** any rate change, **When** it is saved, **Then** the audit log records who entered/changed it, when, and the before/after values.

---

### User Story 4 - Reviewers, approvers, and dashboards display multi-currency clearly (Priority: P2)

Anywhere quote items, quote totals, request totals, or related financial summaries are displayed, the platform shows the original amount + currency alongside the converted CRC amount, with a conversion indicator (icon/tooltip) carrying the applied rate and effective date. Totals and rollups across a request are always presented in CRC. This applies to: supplier quote list, quote detail, funding request summary, applicant dashboard, reviewer dashboard, approval screens, and admin reports.

**Why this priority**: Without consistent display rules, reviewers cannot trust the figures and approvals become risky. P2 because the data is correct under P1; this story makes it legible.

**Independent Test**: A reviewer opens a funding request with mixed CRC and USD supplier quotes and confirms each line shows the original amount + currency, a converted-CRC value with a conversion icon/tooltip, and that the request total is the CRC sum.

**Acceptance Scenarios**:

1. **Given** a request with one CRC quote and one USD quote, **When** a reviewer opens the request summary, **Then** each quote line shows original currency + amount, the CRC equivalent, and the request total equals the sum of CRC equivalents.
2. **Given** a USD quote line is rendered in any list or detail, **When** the user hovers/focuses the conversion indicator, **Then** the tooltip shows the applied rate value, rate type ("Buy"), and effective date.
3. **Given** a quote that was created in CRC, **When** it is rendered anywhere, **Then** no conversion indicator appears.

---

### User Story 5 - Final agreement PDF shows CRC with conversion indicator (Priority: P2)

When a funding agreement PDF is generated for a request that includes any line originally entered in a non-CRC currency, the PDF displays only CRC amounts but appends a conversion indicator/note containing the applied rate value, rate type, and effective date so a third party reading the PDF can audit how each CRC amount was derived. CRC-only requests continue to render PDFs identical to today's behavior. Already-generated historical PDFs are NOT regenerated.

**Why this priority**: PDF is the legally meaningful artifact. Must be auditable and unambiguous. P2 because it depends on P1 conversion data being captured correctly.

**Independent Test**: Generate a PDF for a request with one USD quote — verify all amounts show in CRC, a conversion note appears next to the converted line(s) listing rate value + "Buy" + effective date. Generate a PDF for a CRC-only request — verify no conversion note appears.

**Acceptance Scenarios**:

1. **Given** a request whose quotes include at least one originally USD line, **When** the agreement PDF is generated, **Then** all amounts in the PDF are CRC, and the PDF includes a conversion indicator/note for the converted lines listing rate value, "Buy" rate type, and effective date.
2. **Given** a request whose quotes are all CRC, **When** the agreement PDF is generated, **Then** the PDF contains no conversion indicator/note and is visually identical to today's CRC-only output.
3. **Given** a saved USD quote whose snapshot was set at creation, **When** the agreement PDF is regenerated months later (after rates have changed), **Then** the PDF reflects the original snapshotted CRC amount and rate, not the latest rate.

---

### User Story 6 - Legacy USD quotes are flagged and quarantined until reviewed (Priority: P3)

The system already contains supplier-quote records that captured a typed/selected currency without full conversion metadata. CRC legacy quotes are auto-stamped (currency=CRC, original=converted, no rate snapshot needed). USD legacy quotes (or any non-CRC legacy currency) without conversion metadata are flagged as "legacy — needs review" and continue to render only their original USD amount until an administrator manually attaches a historical rate to each.

**Why this priority**: Migration concern; doesn't block the new feature path but must not silently corrupt historical data. P3 because it can ship after the core feature.

**Independent Test**: After deploying the migration, query existing USD quotes — confirm they carry the "legacy — needs review" flag and continue to display correctly (original USD only, no conversion indicator). After an administrator attaches a historical rate to one, confirm it then shows alongside CRC.

**Acceptance Scenarios**:

1. **Given** a pre-existing CRC quote, **When** the migration runs, **Then** it is stamped as currency=CRC with original = converted CRC and no legacy flag.
2. **Given** a pre-existing USD quote without conversion metadata, **When** the migration runs, **Then** it is marked "legacy — needs review", continues to display only USD in lists/details, and is excluded from cross-currency request totals.
3. **Given** an administrator attaches a historical CRC↔USD rate to a legacy USD quote, **When** the assignment is saved, **Then** the quote behaves as a normal USD quote (snapshot is set, CRC equivalent appears, totals include it).

---

### Edge Cases

- **Stale rate**: USD quote created against a rate published days/weeks ago. Allowed — the snapshot is taken at quote creation time. No staleness warning in MVP (out of scope), but the applied rate's effective date is always visible.
- **Rate change between preview and save**: User opens the form, sees rate R1 in the preview, an admin publishes R2 before the user clicks save. The server re-reads the latest applicable rate at save time and uses R2 for the snapshot. The frontend displays the final snapshotted rate after save (may differ from the preview seen earlier).
- **Edit an existing USD quote**: Re-uses the original snapshotted rate; converted CRC value is recomputed only if the original USD amount changes, applying the same snapshotted rate. Out-of-scope for MVP: explicit "re-price using current rate" action.
- **Disabled currency on existing quotes**: If an admin disables USD after USD quotes already exist, the existing quotes remain valid, display normally, and remain editable (amount edits re-apply the original snapshot per FR-017b); only the currency selector for **new** quotes is restricted.
- **Duplicate effective timestamp**: Admin attempts to save a new rate with an effective timestamp identical to an existing rate. Blocked with a "rate at this timestamp already exists" validation error.
- **PDF generation when a quote has no snapshot but is non-CRC** (legacy not yet reviewed): The request cannot proceed to agreement until the legacy quote is either reviewed (rate attached) or removed. The PDF generator refuses to render with "missing conversion metadata" rather than guessing.
- **Concurrent admin edits**: Two admins try to create a new rate for the same currency pair within the same effective second. Last-writer-wins is acceptable as long as both attempts are recorded in the audit log; the rejected one returns a duplicate-timestamp error.

## Requirements *(mandatory)*

### Functional Requirements

#### Currency configuration

- **FR-001**: System MUST maintain a configurable list of supported currencies. For this MVP, that list is exactly two: CRC (base) and USD.
- **FR-002**: System MUST treat CRC as the base currency. CRC MUST be permanently enabled and MUST NOT be disable-able through any UI or API.
- **FR-003**: Administrators MUST be able to enable or disable USD. Disabling USD MUST block selection of USD on new quote forms but MUST NOT alter existing USD quotes.
- **FR-004**: Each currency record MUST carry, at minimum: ISO code (e.g., "CRC", "USD"), display symbol, human-readable name, decimal precision, enabled flag, base-currency flag, and display order.

#### Exchange rate management

- **FR-005**: Administrators MUST be able to enter new CRC↔USD reference exchange rates. Each rate record MUST include both a buy rate and a sell rate (each expressed as **CRC per 1 USD**), the effective timestamp, and the actor + timestamp who created the record.
- **FR-006**: System MUST reject saving any rate with a buy or sell value of zero or negative.
- **FR-007**: System MUST reject any new rate whose effective timestamp duplicates an existing rate for the same currency pair.
- **FR-007a**: System MUST reject any new rate whose effective timestamp is in the future relative to the server "now" at save time. Future-dated rates are not permitted in MVP; a saved rate becomes immediately active.
- **FR-008**: Once a rate has been referenced (snapshotted) by at least one saved quote, the rate record MUST be immutable. It MUST NOT be editable nor deletable. It can only be superseded by entering a newer rate.
- **FR-009**: System MUST preserve all historical rate records indefinitely and MUST expose a rate-history list visible to administrators showing each rate's buy/sell, effective period, creator, and created-at.
- **FR-010**: System MUST record an audit-log entry for every rate creation and every attempted modification (including those that were blocked), capturing actor, timestamp, before/after values where applicable, and the outcome.
- **FR-011**: New rates MUST become effective immediately upon save. There is no draft/published/approval workflow in MVP.

#### Quote creation and conversion

- **FR-012**: When creating a supplier quote, the user MUST select a currency from the enabled list. The currency selector MUST default to CRC.
- **FR-013**: System MUST display a real-time CRC-conversion preview when the selected currency is anything other than CRC, using the most recent applicable reference exchange rate at the moment the form is rendered/edited.
- **FR-014**: System MUST apply the buy rate when converting USD → CRC. Conversion formula: `crc_amount = round_half_away_from_zero(usd_amount × buy_rate, 2)` where `buy_rate` is expressed as CRC per 1 USD. The sell rate is captured for audit/future use but is not applied to user-facing conversions in MVP.
- **FR-015**: On quote save, the server MUST compute the converted CRC amount using the latest applicable rate at save time (not at form-render time) and MUST persist a snapshot of the applied rate (rate value, rate type=Buy, effective timestamp, and the source rate record id) onto the quote.
- **FR-016**: Once snapshotted, the conversion data MUST be immutable for that quote unless the original entered amount changes. The snapshotted rate is re-applied (not refreshed) when the entered amount is edited.
- **FR-017**: Quote-level currency MUST apply to the entire quote. Mixed-currency line items within a single quote are out of scope for MVP.
- **FR-017a**: A supplier quote's currency MUST be fixed at save time. The system MUST NOT permit changing the currency on an existing saved quote. Users may edit the original amount; to change the currency they MUST delete the quote and create a new one.
- **FR-017b**: Editing the amount on an existing supplier quote MUST re-apply the originally snapshotted rate rather than re-fetching the current latest rate. This guarantee MUST hold even if the quote's currency is currently disabled (FR-003).
- **FR-018**: When the user attempts to select a non-CRC currency for which no published rate exists, the form MUST block save with the message "No reference exchange rate is configured. Contact an administrator."
- **FR-019**: System MUST NOT permit any user to manually override the conversion result. Conversion is server-computed and deterministic.
- **FR-020**: Calculations MUST be performed using fixed-point decimal arithmetic. Floating-point arithmetic MUST NOT be used. CRC and USD amounts MUST be stored with 2 decimal places. Exchange-rate values MUST be stored with 6 decimal places. Final converted CRC amounts MUST round half-away-from-zero. Totals MUST be summed from rounded line values.

#### Display rules

- **FR-021**: Anywhere a quote line, quote total, request total, supplier-quote list, quote detail, applicant dashboard, reviewer dashboard, approval screen, or admin report displays a monetary amount that originated in a non-CRC currency, the UI MUST show the original amount + currency, the converted CRC amount, and a conversion indicator/tooltip carrying the applied rate value, rate type, and effective date. Admin CSV exports MUST include both the original currency/amount columns and the converted CRC column for non-CRC lines (CRC-only rows leave the original-currency columns blank or set to CRC).
- **FR-022**: Cross-line and cross-quote totals (request totals, dashboard totals, report totals) MUST be displayed in CRC and computed by summing converted CRC line values.
- **FR-023**: Pure CRC quotes/lines MUST NOT show a conversion indicator anywhere.

#### Final agreement PDF

- **FR-024**: The final agreement PDF MUST display all amounts in CRC.
- **FR-025**: When any line in the request originated from a non-CRC currency, the PDF MUST include a conversion indicator/note for the affected line(s). The note MUST list the applied rate value, the rate type, and the rate's effective date.
- **FR-026**: The PDF MUST always reflect the snapshotted rate on each affected line, never the latest rate. PDFs regenerated months later MUST be **value-stable** — every monetary value, applied rate, and effective date in the regenerated PDF MUST be identical to the original. Non-monetary differences (signatures, font/CSS revisions, layout adjustments) are permitted.
- **FR-027**: PDF generation MUST refuse to render and MUST surface a "missing conversion metadata" error when any non-CRC line in the request lacks a rate snapshot (e.g., an unreviewed legacy quote). The error MUST be displayed inline on the request/agreement page to the user who triggered the PDF action AND MUST be written to the application log with the offending quote id(s) for operator follow-up.

#### Permissions

- **FR-028**: Currency configuration (enable/disable) and exchange-rate creation MUST be restricted to users with the Administrator role.
- **FR-029**: Quote creators (applicants and other quote-creating roles) MUST be able to select an enabled currency and enter the amount but MUST NOT be able to publish or edit exchange rates.
- **FR-030**: Reviewers and approvers MUST be able to read both original and converted values on every screen they currently access.

#### Migration / backward compatibility

- **FR-031**: On migration, every existing supplier-quote record currently in CRC MUST be stamped as currency=CRC, original = converted, with no rate snapshot and no legacy flag.
- **FR-032**: On migration, every existing supplier-quote record in a non-CRC currency without conversion metadata MUST be marked "legacy — needs review", continue to display only its original amount + currency, and be excluded from cross-currency request totals until a rate is attached.
- **FR-033**: Administrators MUST be able to attach a historical rate to a flagged legacy quote, after which the quote behaves as a normal non-CRC quote (snapshot set, CRC equivalent appears, totals include it).
- **FR-034**: Already-generated historical agreement PDFs MUST NOT be auto-regenerated by the migration.

#### Auditability

- **FR-035**: Every quote MUST carry, in addition to its monetary fields: original currency code, original amount, converted CRC amount (when applicable), and a snapshot block (rate value, rate type, effective timestamp, source rate record id). These fields MUST be visible on the quote detail to users with reviewer/approver/admin access.
- **FR-036**: The audit log MUST be queryable to answer: "which rate was applied to quote X" and "which quotes used rate R".

### Key Entities

- **Currency**: A supported currency in the platform. Attributes: ISO code, symbol, display name, decimal precision, enabled flag, base-currency flag, display order. CRC is base and permanently enabled; USD is the only other supported currency in MVP.
- **ExchangeRate**: A reference exchange rate between two currencies (CRC and USD in MVP). Attributes: source currency, target currency, buy rate, sell rate, effective timestamp, created-by, created-at, immutable-once-used flag (derived).
- **SupplierQuote (extended)**: An existing entity gaining new attributes: original currency code, original amount, converted CRC amount, applied-rate snapshot (rate value, rate type, effective timestamp, source rate record id), and a "legacy — needs review" flag for migrated records lacking conversion metadata. Quote-level currency applies to the whole quote.
- **AuditLog (extended)**: Existing audit infrastructure gains new event types: currency enabled/disabled, exchange rate created, exchange rate edit/delete attempt blocked, legacy quote rate attached. Every event records actor, timestamp, before/after values, outcome.
- **FundingAgreement / PDF (read model only)**: No new persistence; the PDF generator reads quote snapshots and renders CRC plus a conversion indicator/note where any line was originally non-CRC.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of newly-created USD supplier quotes carry a complete rate snapshot (rate value, type, effective timestamp, rate-record id) at the moment they are persisted.
- **SC-002**: After a new exchange rate is published, 100% of pre-existing USD quotes display unchanged converted CRC values.
- **SC-003**: For any request containing at least one USD quote, 100% of generated agreement PDFs render exclusively in CRC and include a conversion indicator listing the applied rate value, rate type, and effective date for each converted line.
- **SC-004**: For any request whose quotes are all CRC, 100% of generated agreement PDFs are visually identical to today's CRC-only PDF (no conversion indicator).
- **SC-005**: An administrator can publish a new CRC↔USD rate (open the screen, enter buy + sell + effective timestamp, save, see it become the active rate) in under 2 minutes.
- **SC-006**: 0 instances of float-based monetary calculation in the conversion path (verifiable via code review and unit tests asserting decimal-only arithmetic).
- **SC-007**: 0 attempts to edit or delete a used exchange rate succeed; every blocked attempt is recorded in the audit log.
- **SC-008**: After migration, 100% of pre-existing CRC quotes are auto-stamped without admin intervention; 100% of pre-existing non-CRC quotes without conversion metadata are flagged "legacy — needs review" and continue to render in lists/details without breaking.
- **SC-009**: For any auditable request, an investigator can trace from a request total back to the per-line snapshotted rate and the source rate record using only data visible on quote detail and rate history (no engineer assistance needed).

## Assumptions

- The applicable conversion direction in MVP is USD → CRC using the buy rate. The sell rate is captured for audit/future use but is not applied to any user-facing conversion in this phase.
- Conversion is locked at quote creation/save time — not at quote submission, approval, or agreement-generation time. Re-pricing an existing quote with a newer rate is explicitly out of scope.
- Rates do not carry an explicit expiration. A rate remains the "latest applicable rate" until the next rate is published. Stale-rate warnings are out of scope for MVP.
- Quote-level currency applies to all lines within a single quote. A request may contain multiple quotes in different currencies, but a single quote is single-currency.
- Existing CRC-only flows (UI, PDF, reports) must continue to behave identically when no non-CRC data is involved. Net new behavior is additive only.
- The default culture remains es-CR. UI copy for currency, conversion notes, and validation messages will be added in es-CR primary, with localization following the project's existing localization story (spec 012).
- The current Administrator role is sufficient to gate currency-configuration and exchange-rate management. No new roles are introduced.
- Currency precision is fixed at 2 decimals for both CRC and USD; exchange-rate precision is fixed at 6 decimals. These are not per-currency configurable in MVP.
- Reference exchange rates are stored as **CRC per 1 USD**. A buy rate of `520.000000` means "1 USD ↔ 520.00 CRC at the buy rate". USD → CRC conversion multiplies the USD amount by the buy rate; the inverse direction is unused in MVP.
- New exchange rates take effect immediately on save; future-dated effective timestamps are rejected (no scheduled-rate workflow in MVP).
- A quote's currency is immutable after save. Currency changes require deleting and recreating the quote. Amount edits on existing quotes re-apply the original rate snapshot.
- Decimal arithmetic is performed using the platform's existing fixed-point decimal type; floating-point conversion code is forbidden in this path.
- All rate creation and currency-configuration audit hooks integrate with the existing audit-log infrastructure; no new audit storage is introduced.
- Old already-generated PDFs are not regenerated by migration. Future PDF regenerations of historical requests rely on the snapshotted rate on each line.
- Out of scope for this MVP: more than two currencies, currency pairs other than CRC/USD, manual per-quote conversion override, re-pricing existing quotes against a newer rate, approval workflow for rates, stale-rate notifications, mid-quote currency switching that keeps the entered amount unchanged.
