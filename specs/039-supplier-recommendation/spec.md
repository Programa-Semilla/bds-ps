# Feature Specification: Supplier Recommendation Algorithm Rewrite

**Feature Branch**: `039-supplier-recommendation`
**Created**: 2026-06-18
**Status**: Draft
**Input**: Feedback-3 slice B — Supplier recommendation algorithm rewrite. Master sections §14, §22.7/22.8, §6 (item-line fields), §28.2/28.3/28.13. Depends on shipped slice A (spec 038). Source: `seeds/feedback-3/00-decomposition.md` + `seeds/feedback-3/AI_Coding_Agent_Unified_Requirements.md`.

## Overview

Today the reviewer's supplier recommendation is a 4-point score (three regulatory-favorability checks plus lowest price). The lowest price dominates and the criteria are coarse. This feature replaces that score with the client's explicit, **deterministic and explainable** multi-criterion algorithm: seven criteria — price, delivery lead time, warranty time, Hacienda status, CCSS status, SICOP status, and PME/PYME flag — each contributing a transparent per-criterion score that sums to a total. The provider with the highest total is recommended; the lowest price no longer automatically wins.

Two new quote-level fields (delivery lead time, warranty time) are introduced and made mandatory. One regulatory status — **CCSS `sin inscripción`** — acts as a hard block that disqualifies a provider from recommendation and prevents the application from advancing while that provider is in use. The applicant's item-line creation form is reordered so the product name comes first. The existing AI quote-comparison aid (spec 020) is retained unchanged as a separate, optional deeper-analysis tool.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Explainable multi-criterion recommendation (Priority: P1)

A reviewer opens an application item that has two or more supplier quotations. Instead of a single price-driven score, the reviewer sees each eligible provider scored across all seven criteria, the per-criterion points, the underlying values (price, delivery, warranty, regulatory statuses, PME/PYME), and a total. The provider with the highest total is clearly marked as recommended, and the reviewer can read *why* it won.

**Why this priority**: This is the core deliverable — the change in the recommendation logic and its explainability. Without it, the feature has no value.

**Independent Test**: Seed an item with three quotations whose data is arranged so the lowest-price provider does **not** have the highest total (e.g., a higher-priced provider has shorter delivery, longer warranty, and favorable regulatory statuses). Open the reviewer surface and confirm the higher-total provider is recommended and the seven per-criterion scores and raw values are displayed.

**Acceptance Scenarios**:

1. **Given** three providers quoting an item where provider B has a higher price than A but shorter delivery, longer warranty, and more favorable regulatory statuses, **When** the reviewer views the item, **Then** provider B is marked Recomendado because its total score is highest, and A is not.
2. **Given** an item with provider quotations, **When** the reviewer views the recommendation, **Then** each eligible provider shows seven per-criterion scores, the total score, and the raw values used (price, delivery value+unit, warranty value+unit, Hacienda/CCSS/SICOP status, PME/PYME flag).
3. **Given** a provider that wins no individual criterion, **When** scores are shown, **Then** that provider still shows at least 1 point for every criterion and a total of at least 7.
4. **Given** two providers tie for the shortest delivery lead time, **When** delivery is scored, **Then** both receive 2 points for delivery and any others receive 1.
5. **Given** two providers tie for the lowest price, **When** price is scored, **Then** all tied providers receive 1 point for price (none receive 2).
6. **Given** quotations expressed in different currencies, **When** price is compared, **Then** the comparison uses the normalized CRC amount, not the raw quoted amount.
7. **Given** delivery or warranty values expressed in different units (days vs. months), **When** they are compared, **Then** values are normalized to days before comparison and the original value+unit is shown for display.

---

### User Story 2 - Capture delivery lead time and warranty on every quote (Priority: P1)

When an applicant adds (or edits) a supplier quotation, they must enter the provider's delivery lead time and warranty time, each as a numeric value plus a unit of days or months. Both fields are mandatory; a quote cannot be saved without them.

**Why this priority**: The algorithm depends on these quote-level values. They are introduced here and required going forward (no backward-compatibility — existing seed data is updated to include them).

**Independent Test**: Open the add-quotation form, attempt to save with delivery lead time and/or warranty blank, and confirm the save is rejected with a clear message. Save with valid values and confirm they persist and display.

**Acceptance Scenarios**:

1. **Given** the add-quotation form, **When** the applicant submits without a delivery lead time, **Then** the save is rejected with a clear es-CR validation message and the quote is not created.
2. **Given** the add-quotation form, **When** the applicant submits without a warranty time, **Then** the save is rejected with a clear es-CR validation message.
3. **Given** valid delivery and warranty values with units, **When** the quote is saved, **Then** both values and units persist and are shown on the quotation and the recommendation breakdown.
4. **Given** a value of zero or negative for either field, **When** the applicant submits, **Then** the save is rejected (values must be greater than zero).
5. **Given** the quotation-edit affordance (existing per-quote edit), **When** the applicant edits a quote, **Then** delivery lead time and warranty remain editable and required.

---

### User Story 3 - CCSS "sin inscripción" disqualifies a provider and blocks progress (Priority: P2)

A provider whose CCSS regulatory status is `sin inscripción` is treated as ineligible: it is excluded from scoring and never recommended, and is shown as *bloqueado* with the reason. A reviewer may still select such a provider's quotation, but the application cannot advance past the reviewer step while a `sin inscripción` provider is the selected provider for any item.

**Why this priority**: A targeted compliance safeguard the client explicitly requested. It builds on the algorithm (US1) but is a distinct, separately testable behavior.

**Independent Test**: Seed an item where the lowest-scoring math would otherwise pick a provider whose CCSS is `sin inscripción`; confirm that provider is excluded from scoring, flagged blocked, and that the reviewer cannot advance the application while it is selected.

**Acceptance Scenarios**:

1. **Given** an item with three providers, one of which has CCSS `sin inscripción`, **When** the recommendation is computed, **Then** the `sin inscripción` provider is excluded from the candidate set (not scored, not recommended) and the remaining two are scored among themselves.
2. **Given** a `sin inscripción` provider in the breakdown, **When** the reviewer views it, **Then** it is shown as *bloqueado* with the reason indicated, distinct from a merely low-scoring provider.
3. **Given** the reviewer has selected a `sin inscripción` provider's quotation for an item, **When** the reviewer attempts to advance/approve the application, **Then** the advance action is blocked with a clear es-CR message naming the offending item and provider, and the application does not move forward.
4. **Given** a `sin inscripción` provider's selection is changed to an eligible provider, **When** the reviewer attempts to advance, **Then** the application can advance.
5. **Given** an item where *every* candidate provider has CCSS `sin inscripción`, **When** the recommendation is computed, **Then** the item shows "ningún proveedor elegible" and the application cannot advance.
6. **Given** any other regulatory status (any Hacienda value, any SICOP value, or any non-`sin inscripción` CCSS value), **When** scoring runs, **Then** it affects only the score and never disqualifies or blocks the provider.

---

### User Story 4 - Final-score tie requires manual selection (Priority: P3)

When two or more eligible providers tie for the highest total score, the system does not invent a winner. No provider is auto-marked recommended; instead the tied set is flagged and the reviewer is told a manual selection is required.

**Why this priority**: An edge of the algorithm that must be defined, but lower frequency than the core flow.

**Independent Test**: Seed an item where two eligible providers reach the same highest total; confirm neither is auto-marked recommended and a "selección manual requerida" message names the tied set.

**Acceptance Scenarios**:

1. **Given** a single eligible provider with a strictly highest total, **When** the recommendation is shown, **Then** that provider is marked Recomendado.
2. **Given** two or more eligible providers tied for the highest total, **When** the recommendation is shown, **Then** no provider is auto-marked recommended, the tied providers are flagged, and a "selección manual requerida" message is shown.
3. **Given** a tie at the top, **When** the reviewer proceeds, **Then** the reviewer's own quotation selection remains the means of choosing (the recommendation engine declines to break the tie).

---

### User Story 5 - Item-line field order: product name first (Priority: P3)

When an applicant adds an item line, the first field is the product name, followed by the category, then the dynamic category-specific fields, then the remaining item/quotation fields.

**Why this priority**: A UI ordering requirement (§6/§24.4) explicitly called out by the client; shipped here because slice B precedes slice H.

**Independent Test**: Open the add-item form and confirm the product-name field renders before the category selector, and category-specific dynamic fields render only after a category is selected.

**Acceptance Scenarios**:

1. **Given** the add-item form, **When** it renders, **Then** the product-name field appears first, before the category selector.
2. **Given** the applicant has not yet selected a category, **When** viewing the form, **Then** category-specific dynamic fields are not shown.
3. **Given** the applicant selects a category, **When** the selection is made, **Then** the category-specific dynamic fields appear after the category selector, followed by the remaining item/quotation fields.

---

### Edge Cases

- **Single quotation on an item**: with only one provider, that provider wins every quote-level criterion it has a value for; it is recommended unless it is CCSS `sin inscripción` (then blocked).
- **All providers blocked**: handled in US3 #5 — item shows no eligible provider, application cannot advance.
- **Mixed units across providers** (days vs. months): normalized to days before comparison (US1 #7).
- **Mixed currencies across providers**: price compared on normalized CRC amount (US1 #6).
- **Tie for lowest price**: all tied get 1 point, none get 2 (US1 #5) — distinct from delivery/warranty ties where all tied get 2.
- **Provider with an unfavorable but non-blocking status** (e.g., Hacienda `moroso`): scored 1 on that criterion, still eligible and recommendable.
- **Tie at the top total** including the case where the tie involves the only eligible providers: manual selection (US4).
- **Existing AI comparison artifact**: continues to render independently; its freshness/staleness behavior is unaffected by this feature.

## Requirements *(mandatory)*

### Functional Requirements

**Quote-level data**

- **FR-001**: Each supplier quotation MUST capture a delivery lead time as a numeric value greater than zero plus a unit of `días` or `meses`.
- **FR-002**: Each supplier quotation MUST capture a warranty time as a numeric value greater than zero plus a unit of `días` or `meses`.
- **FR-003**: Delivery lead time and warranty time MUST be required on every new quotation and on quotation edits; a quote MUST NOT be saveable without both. There is no backward-compatibility path — existing seed data is updated to populate them.
- **FR-004**: The system MUST normalize delivery lead time and warranty time to days for comparison while retaining and displaying the original value and unit.

**Scoring algorithm**

- **FR-005**: The recommendation MUST be computed per application item, across the provider quotations for that item, as a deterministic function of stored data (no AI involvement).
- **FR-006**: For each scored criterion, every eligible provider MUST receive a base of 1 point, and the winning provider(s) MUST receive 2 points. There MUST be no separate standalone base score added on top of the criterion scores.
- **FR-007**: The algorithm MUST score exactly these seven criteria: price, delivery lead time, warranty time, Hacienda status, CCSS status, SICOP status, and PME/PYME flag.
- **FR-008**: **Price** — the provider with the lowest normalized-CRC price MUST receive 2 points and others 1. If two or more providers tie for the lowest price, all tied providers MUST receive 1 point and no provider receives 2.
- **FR-009**: **Delivery lead time** — the provider(s) with the shortest normalized delivery lead time MUST receive 2 points and others 1. Ties for shortest: all tied providers receive 2.
- **FR-010**: **Warranty time** — the provider(s) with the longest normalized warranty time MUST receive 2 points and others 1. Ties for longest: all tied providers receive 2. (Longer warranty is better — see Assumptions / §28.2.)
- **FR-011**: **Hacienda** — status `al día` MUST receive 2 points; every other Hacienda value receives 1.
- **FR-012**: **CCSS** — status `al día` MUST receive 2 points; every other CCSS value receives 1. (CCSS `sin inscripción` providers are excluded before scoring — see FR-016.)
- **FR-013**: **SICOP** — status `sin sanciones` MUST receive 2 points; every other SICOP value receives 1.
- **FR-014**: **PME/PYME** — a provider flagged PME/PYME MUST receive 2 points; otherwise 1.
- **FR-015**: The total score MUST be the sum of the seven criterion scores. The eligible provider with the strictly highest total MUST be the recommended provider.

**Eligibility and progression block**

- **FR-016**: A provider whose CCSS status is `sin inscripción` MUST be excluded from the candidate set before scoring — it MUST NOT be scored and MUST NOT be recommended. Quote-level "winner" comparisons (price, delivery, warranty) MUST be evaluated only over the eligible providers.
- **FR-017**: An excluded (CCSS `sin inscripción`) provider MUST be shown as *bloqueado* with its reason, visually distinct from a low-scoring eligible provider.
- **FR-018**: No regulatory status other than CCSS `sin inscripción` MUST cause disqualification or a progression block; all other statuses affect scoring only.
- **FR-019**: A reviewer MUST be able to select a CCSS `sin inscripción` provider's quotation, but the application's advance/approve action MUST be blocked while any item's selected provider is CCSS `sin inscripción`. The block MUST present a clear es-CR message naming the offending item and provider, and MUST clear when the selection changes to an eligible provider.
- **FR-020**: When every candidate provider for an item is CCSS `sin inscripción`, the item MUST show that no eligible provider exists and the application MUST NOT be able to advance.

**Tie-breaking**

- **FR-021**: When two or more eligible providers tie for the highest total, the system MUST NOT auto-mark any provider as recommended; it MUST flag the tied set and indicate that manual selection is required.

**Output and explainability**

- **FR-022**: The recommendation surface MUST display, for each eligible provider: the total score, each of the seven per-criterion scores, and the raw values used — price, delivery value and unit, warranty value and unit, Hacienda status, CCSS status, SICOP status, and PME/PYME flag.
- **FR-023**: The recommendation display MUST replace the prior coarse fraction score display (the `/4`, and the stray `/5` in the supplier-selection control) with the total-plus-breakdown presentation.
- **FR-024**: The existing AI quote-comparison aid MUST remain available and unchanged as an optional, separate analysis tool; it MUST NOT be the source of the recommendation.

**Item-line field order**

- **FR-025**: The add-item-line form MUST present fields in the order: (1) product name, (2) category, (3) category-specific dynamic fields, (4) remaining item/quotation fields. Category-specific dynamic fields MUST appear only after a category is selected.

**Localization**

- **FR-026**: All new and changed user-facing copy (validation messages, block messages, tie messages, labels) MUST be in es-CR.

### Key Entities

- **Supplier Quotation (extended)**: an existing quotation for a provider on an application item. Gains a delivery lead time (value + unit) and a warranty time (value + unit), each with a normalized-to-days representation for comparison. Retains its existing price, currency, validity, document, and multi-currency CRC conversion.
- **Provider compliance attributes (consumed, not changed here)**: the provider-level Hacienda, CCSS, and SICOP regulatory statuses and the PME/PYME flag introduced in slice A (spec 038). This feature reads them; it does not modify them or their audit trail.
- **Recommendation result (computed, not persisted)**: a transient per-item, per-provider result holding the seven criterion scores, the total, and eligibility/recommended/blocked flags. It is recomputed on read from live quotation and provider data; it is not stored.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The recommendation reflects all seven criteria, not price alone — a higher-priced provider with superior delivery, warranty, and regulatory standing can be and is recommended over the lowest-price provider in the relevant test scenarios.
- **SC-002**: 100% of recommendation displays for an item with eligible providers show, for each eligible provider, all seven per-criterion scores, the total, and the raw values — so a reviewer can determine why a provider was recommended without leaving the screen.
- **SC-003**: A provider with CCSS `sin inscripción` is never marked recommended, and in 100% of attempts an application with such a provider selected cannot advance past the reviewer step.
- **SC-004**: 100% of new quotation saves are rejected when delivery lead time or warranty time is missing, zero, or negative.
- **SC-005**: When eligible providers tie for the highest total, no provider is auto-marked recommended and the reviewer is prompted to select manually — verified across single-winner and tie scenarios.
- **SC-006**: On the add-item-line form, the product-name field is presented before the category selector in 100% of renders.

## Assumptions

- **Warranty direction (§28.2)**: Longer warranty is better. Confirmed during brainstorming as the business intent.
- **Final-score tie (§28.3)**: Resolved to "manual selection" — the system surfaces the tie and declines to choose; the reviewer selects. (Not lowest-price tiebreak, which would reintroduce the price primacy this feature removes.)
- **Disqualification (§28.13)**: Only CCSS `sin inscripción` disqualifies/blocks; all other statuses affect scoring only. Confirmed during brainstorming.
- **Month-to-days normalization**: One month is treated as 30 days for the purpose of normalizing delivery lead time and warranty to days for comparison. This normalization constant is specific to scoring comparison and is independent of any "one-month" freshness rule in slice D.
- **Price normalization**: Price comparison uses the existing CRC-normalized amount from the multi-currency feature (spec 015); no new currency logic is introduced.
- **No persistence of scores**: The recommendation result is computed live on each read, matching the current value-object pattern; the §22.8 field list defines the shape of the computed result, not a database table. There is therefore no score-invalidation logic to maintain.
- **AI comparison retained (Approach A)**: The spec-020 AI quote comparison stays in place as an optional, separate analysis aid; this feature is additive to it, not a replacement.
- **Reviewer advance step**: The progression block is anchored at today's reviewer advance/approve step. When slice C reworks the workflow (auditor stage, PDF confirmation move), it re-anchors this block; slice B introduces no new workflow states.
- **Greenfield data**: All current data is seed data; it will be updated to include the new required fields. No production backfill is required.

## Dependencies

- **Slice A (spec 038, shipped)**: provides the Hacienda/CCSS/SICOP enumerated statuses, the PME/PYME flag, the provider warning, and the regulatory audit trail consumed by this algorithm.
- **Spec 015 (multi-currency)**: provides the CRC-normalized quotation amount used for price comparison.
- **Spec 020 (AI quote comparison)**: coexists unchanged; retained as a separate analysis aid.
- **Spec 035 (category field templates) / spec 023 (quotation edit)**: the item-line and quotation forms this feature reorders and extends.

## Out of Scope

- Persisting a recommendation-score history table (§22.8 as a stored entity).
- The Auditor workflow stage, checklist templates, and moving PDF confirmation from reviewer to auditor (slice C).
- Regulatory review freshness blocking and the Hacienda API sync job (slice D).
- Creating or governing provider warnings (slice A owns warning governance, §28.14).
- Any disqualification or progression block beyond CCSS `sin inscripción`.
- Re-anchoring the progression block into the new auditor workflow (slice C).
