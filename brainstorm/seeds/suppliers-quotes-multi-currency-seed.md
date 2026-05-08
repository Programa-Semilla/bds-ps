You are Claude Code acting as a senior product engineer, solution architect, and specification facilitator for the FundingPlatform system.

Run a detailed brainstorm session for the following feature. Your output must be in English.

Feature: Suppliers Quotes Multi-Currency
System: FundingPlatform

## Current Context

FundingPlatform currently has only MVP-level multi-currency support.

When an applicant is creating a new funding request and generating a new supplier quote, the quote price field currently lets the user type/select a currency. This is the only current multi-currency behavior.

The real requirement is broader:

Administrators must be able to configure which currencies are available in the platform. For simplicity in this phase, the system will support only two currencies:

- Costa Rican colón, CRC
- United States dollar, USD

The system must also allow authorized administrators to periodically enter the reference exchange rate between these two currencies, including both:

- Buy rate
- Sell rate

When a user creates a supplier quote, the quote may be entered in CRC or USD. The user must choose the currency and enter the amount. If the selected currency is different from CRC, the system must immediately show the converted amount in CRC.

From that point forward, the system must preserve and display both:

- The original entered amount and currency
- The converted CRC amount, whenever applicable

Anywhere quote items, quote totals, request totals, or related financial summaries are displayed, the platform must show enough information to make multi-currency values clear.

The final agreement PDF is different: it only displays amounts in CRC. If any line item was originally entered in a currency different from CRC and converted using an exchange rate, the PDF must include an indicator/note showing that the amount was converted using an exchange rate. The PDF does not need to show full foreign-currency totals unless the brainstorm identifies a strong reason to do so.

## Goal of This Brainstorm

Produce a comprehensive product and technical brainstorming document that helps the team discover requirements, risks, edge cases, UX decisions, data-model changes, and implementation options before writing the final specification.

Do not jump directly into implementation. First explore the problem space thoroughly.

## Brainstorming Objectives

Cover at least the following areas:

### 1. Product Understanding

Clarify the business goal of supporting supplier quotes in multiple currencies.

Explore:
- Why CRC must remain the system/base currency
- Why USD quote entry is needed
- What users need to understand at the moment of quote entry
- What administrators need to configure
- What auditors, reviewers, approvers, or operations users may need to see later
- How this affects funding requests, supplier quotes, totals, approvals, agreements, and PDFs

### 2. Actors and Permissions

Identify all relevant actors and their likely permissions.

Consider:
- Applicant / end user creating a request
- Supplier quote creator
- Administrator managing enabled currencies
- Administrator entering exchange rates
- Reviewer / approver
- Operations / back-office user
- Auditor or compliance user

Brainstorm permission boundaries, such as:
- Who can create exchange rates
- Who can edit exchange rates
- Whether exchange rates can be deleted
- Whether historical rates can be corrected
- Whether users can manually override conversion results
- Whether applicants should see buy rate, sell rate, both, or only the applied rate

### 3. Currency Configuration

Brainstorm how currency configuration should work.

At minimum, consider:
- The initial supported currencies: CRC and USD
- Whether the system should store enabled/disabled currencies
- Whether CRC should be mandatory and non-disableable
- Whether USD should be configurable but initially enabled
- Whether currency metadata should include code, symbol, name, decimal precision, enabled flag, display order, and base currency flag
- Whether future expansion beyond CRC/USD should influence the design

### 4. Exchange Rate Management

Explore requirements for exchange rates.

Consider:
- Buy rate and sell rate
- Effective date and time
- Who enters rates and how frequently
- Whether there can be multiple rates per day
- Whether a rate is valid until superseded
- Whether rates require approval
- Whether draft/published states are needed
- Whether rates should be immutable once used by a quote
- Whether old rates must remain visible for auditability
- What happens if no exchange rate exists
- Which rate should be used for converting supplier quote amounts
- Whether the system should use buy rate, sell rate, or a configured conversion direction
- Whether conversion should be based on the rate at quote creation time, quote submission time, approval time, or agreement generation time

Explicitly identify open questions where the business must decide.

### 5. Quote Entry UX

Brainstorm the quote-entry user experience.

Include:
- Currency selector
- Amount input
- Immediate CRC conversion preview
- Formatting for CRC and USD
- Validation messages
- Missing exchange-rate states
- Exchange-rate stale warning, if applicable
- Read-only display of the applied exchange rate
- Whether the user sees buy/sell wording or simpler wording such as “Reference exchange rate”
- Whether quote items can mix currencies
- Whether the quote-level currency applies to all items, or each item has its own currency
- How to handle taxes, discounts, fees, shipping, or other quote components if present

### 6. Display Rules Across the Platform

Brainstorm where and how amounts should be displayed after quote creation.

Consider:
- Supplier quote list
- Supplier quote detail
- Funding request summary
- Applicant dashboard
- Reviewer dashboard
- Approval screens
- Internal reports
- Notifications
- Audit logs
- Agreement preview
- Final agreement PDF

For each area, brainstorm whether it should show:
- Original amount and currency
- Converted CRC amount
- Exchange rate used
- Conversion indicator
- Tooltip or footnote
- Only CRC amount

### 7. Final Agreement PDF Behavior

Focus specifically on the PDF requirement.

Known requirement:
- The final agreement PDF only shows CRC amounts.
- If any amount originated in USD or another non-CRC currency, the PDF must include an indicator/note that an exchange rate was applied.

Brainstorm:
- What the indicator should say
- Whether the indicator appears per line item, per section, or as a general note
- Whether the PDF should include the applied exchange rate
- Whether the PDF should include the exchange-rate effective date
- Whether legal/compliance review is needed
- Whether converted values should be rounded before appearing in the PDF
- Whether the PDF must preserve historical conversion values even if exchange rates later change

### 8. Data Model and Persistence

Brainstorm data-model implications.

Consider entities such as:
- Currency
- ExchangeRate
- SupplierQuote
- SupplierQuoteItem
- FundingRequest
- Agreement
- AuditLog

Explore fields such as:
- Original amount
- Original currency code
- Converted CRC amount
- Applied exchange rate
- Exchange rate type: buy/sell/reference
- Exchange rate effective timestamp
- Exchange rate record ID
- Conversion timestamp
- Conversion direction
- Rounding metadata
- Conversion source: system/manual/imported
- Whether the quote stores a snapshot of the rate or only a reference to the rate record
- How to protect historical quotes from later exchange-rate changes

### 9. Conversion Rules

Brainstorm deterministic conversion rules.

Cover:
- CRC to CRC behavior
- USD to CRC behavior
- Decimal precision
- Rounding mode
- Whether calculations are done with integer minor units, decimal values, or database numeric types
- Whether totals are calculated from rounded line values or from unrounded values
- How taxes and discounts are converted
- Whether rate precision must be configurable
- How to avoid floating-point errors
- What happens when amounts are edited after creation
- Whether changing the currency resets the amount or recalculates the preview
- Whether an existing quote can be re-priced using a newer rate

### 10. Validation and Error States

Brainstorm validations and errors.

Include:
- Missing amount
- Invalid amount
- Unsupported currency
- Disabled currency
- Missing exchange rate
- Expired/stale exchange rate
- Exchange rate entered as zero or negative
- Duplicate exchange rate for same currency pair and effective timestamp
- Editing a quote after exchange rate has changed
- Race conditions when exchange rates are updated while a quote is being created
- Permissions failures
- PDF generation with missing conversion metadata

### 11. Auditability and Compliance

Brainstorm audit requirements.

Consider:
- Who entered each exchange rate
- When each rate was entered
- When each rate became effective
- Who changed or corrected it
- Which quotes used which rate
- Whether used rates can be modified or only superseded
- How to show rate history
- How to investigate a historical agreement
- What data must be included in logs
- Whether audit logs need before/after values

### 12. Migration and Backward Compatibility

Current system may already have quote records with a typed/selected currency but without full conversion metadata.

Brainstorm:
- How to migrate existing quotes
- What to do with quotes in CRC
- What to do with quotes in USD without a historical exchange rate
- Whether migrated records need a “legacy” conversion status
- Whether old PDFs need regeneration
- Whether existing workflows break if converted CRC amount is required
- Whether the system should backfill conversions or require manual review

### 13. API and Backend Design

Brainstorm backend requirements.

Consider:
- Currency listing endpoint
- Admin currency configuration endpoint
- Exchange-rate CRUD or publish endpoints
- Quote creation/update endpoints
- Conversion preview endpoint
- Agreement PDF generation data contract
- Validation responsibilities
- Idempotency and concurrency
- Service boundaries
- Domain service for currency conversion
- Whether conversion should happen on the server only, with frontend preview calling backend
- Testability of conversion logic

### 14. Frontend Design

Brainstorm frontend implications.

Consider:
- Quote form changes
- Admin exchange-rate management screens
- Display components for money values
- Reusable currency amount component
- Reusable converted amount component
- Tooltips and notes
- Loading/error state for conversion preview
- Accessibility and localization
- Spanish UI copy if the product UI is Spanish
- Preventing user confusion around buy/sell rates

### 15. Reporting and Totals

Brainstorm how totals should behave.

Consider:
- Quote total
- Request total
- Totals across multiple supplier quotes
- Totals across mixed-currency line items
- Filtering or sorting by amount
- Export behavior
- Reports that currently assume one currency
- Whether reports should use stored converted CRC values
- Whether financial summaries should display only CRC or both original and converted values

### 16. Testing Strategy

Brainstorm testing needs.

Include:
- Unit tests for conversion
- Unit tests for rounding
- Backend validation tests
- Frontend form tests
- Admin exchange-rate tests
- Permission tests
- PDF generation tests
- Migration tests
- Regression tests for existing CRC-only behavior
- Edge cases around stale/missing rates
- Snapshot tests for display formatting, if useful

### 17. Security and Abuse Cases

Brainstorm potential risks.

Consider:
- Unauthorized exchange-rate changes
- Manipulation of quotes by changing exchange rates
- Tampering with converted amounts
- Hidden financial discrepancies in PDF
- Race conditions
- Audit gaps
- Inconsistent frontend/backend calculations
- Injection or formatting issues in PDF notes
- Privilege escalation in admin screens

### 18. Non-Functional Requirements

Brainstorm:
- Accuracy
- Auditability
- Performance
- Availability of exchange-rate data
- Maintainability
- Future extensibility to more currencies
- Localization
- Accessibility
- Observability and logging
- Backward compatibility

### 19. Open Questions

Produce a clear list of open business, product, legal, UX, and technical questions.

Make sure to include at least:
- Which exchange rate should be applied: buy, sell, or another reference rate?
- At what point is the exchange rate locked?
- Are quote line items allowed to use different currencies?
- Are exchange rates editable after being used?
- Should the final PDF show the applied rate or only a conversion indicator?
- What should happen to legacy quotes without conversion metadata?
- How should stale exchange rates be handled?
- Who is authorized to manage currencies and exchange rates?

### 20. Recommended MVP Scope

After brainstorming broadly, recommend a practical MVP scope.

Separate:
- Must-have for this phase
- Should-have
- Could-have
- Out of scope for now

Keep the MVP aligned with the known constraint that only CRC and USD are supported for now.

### 21. Acceptance Criteria Draft

Draft high-level acceptance criteria in Gherkin-style or clear bullet format.

Include scenarios for:
- Admin enables supported currencies
- Admin creates exchange rate
- Applicant creates CRC quote
- Applicant creates USD quote and sees CRC conversion
- Quote details show original and converted amount
- Totals show CRC amounts correctly
- PDF shows CRC only with conversion indicator
- Missing exchange-rate error
- Historical quote keeps original conversion after exchange rate changes

### 22. Risks and Tradeoffs

Identify important product and technical risks.

For each risk, include:
- Description
- Impact
- Mitigation
- Decision needed, if any

### 23. Suggested Specification Outline

End with a proposed outline for the final feature specification that could be used in a later `/speckit` command.

## Output Format

Structure your response with clear headings.

Use tables where helpful, especially for:
- Actors and permissions
- Display rules
- Data model candidates
- Validation/error states
- MVP scope
- Risks and mitigations

Be explicit about assumptions.

Clearly mark:
- Confirmed requirements
- Reasonable assumptions
- Open questions
- Recommended decisions
