# Funding Platform — Unified AI Coding Agent Requirements Specification

**Language:** English  
**Purpose:** Unified requirements specification reconciled from two documents that captured the same client discovery session. This version preserves all requirements, resolves source conflicts explicitly, and is structured for an AI coding agent or software development team.  
**Important instruction:** The details below are intentionally preserved. They should not be treated as a summary. The client explicitly indicated that the details are part of the system requirements and must not be omitted or minimized.

---

## Non-Negotiable Instruction for the Coding Agent

Do not delete a requirement because it appears small, operational, duplicated, or implementation-specific. The client explicitly expressed concern that details were being minimized. When a rule is ambiguous, preserve it as an open decision rather than silently dropping it.

---
## 1. Context and Scope

The client provided a set of requirements for an existing funding platform. Some ideas are still broad and may need implementation-level decisions, while others are already explicit business rules. The goal of this document is to organize those ideas by module, workflow, data model, and acceptance criteria so that a development AI can understand what must be changed.

The changes affect at least the following areas:

1. Fund process configuration and application submission windows.
2. Applicant submission behavior and visual guidance.
3. User-to-process assignment and maximum allowed funding amount per person.
4. Impact templates and category templates.
5. Item line creation flow.
6. User creation form ordering.
7. Password recovery email validation.
8. New `Auditor` role.
9. Provider/supplier management.
10. Provider regulatory compliance status model.
11. Provider warning flags and notes.
12. Provider notification to auditors.
13. Recommendation/scoring algorithm for suppliers.
14. Provider audit trail and regulatory review freshness control.
15. Automated Hacienda status synchronization through an API.
16. Reviewer-to-auditor workflow before agreement/PDF generation.
17. Reviewer and auditor checklist templates.
18. Applicant-facing timeline/progress visualization.

---

## 2. Terminology

### 2.1 Applicant / Starter / Submitter
The user who creates and submits funding applications. The client used terms like applicant, starter, and the person who sends submissions. In the system, this should map to the applicant-facing experience.

### 2.2 Reviewer
The user who reviews an application, interacts with the applicant, requests corrections if needed, and eventually decides that the application is ready to move forward.

### 2.3 Auditor
A new role that must be added to the platform. The auditor manages provider regulatory compliance, reviews applications after the reviewer completes their review, validates checklist items, generates/reviews the agreement PDF, and approves it for sending to the applicant for digital signature.

### 2.4 Provider / Supplier
The company or entity quoted in an item line. The provider has regulatory compliance values such as Hacienda, CCSS/Caja, and SICOP/CCOP status. Provider-level values are national-level attributes and should remain tied to the provider screen, not to each individual quote or application.

### 2.5 Fund Process
A configured process for a fund. It has a global start date and end date and can contain one, two, or more application reception windows inside that overall process period.

### 2.6 Application Reception Window
A configured period inside a fund process during which applicants are allowed to submit applications. Applicants may be allowed to create drafts outside these windows, but submission must only be enabled during active reception windows.

### 2.7 Regulatory Compliance
The set of provider-level statuses related to Hacienda, CCSS/Caja, and SICOP/CCOP. These statuses are no longer simple checkboxes. They must become enumerated values with defined possible states. The values must be used by the recommendation algorithm.

### 2.8 PME / PYME Flag
A new provider-level boolean value indicating whether the provider is a PME/PYME. The client spelled it as “P M E”. If checked, the provider receives additional scoring value in the recommendation algorithm.

### 2.9 Recommendation Algorithm
The scoring logic that determines which provider/supplier is recommended. The recommended provider is the one with the highest score. The lowest price does not always win because other criteria must also be considered.

---

## 3. Fund Process Configuration

### 3.1 Current Situation
The current system has a concept similar to stages or phases that are assigned a time period. The client stated that this must change significantly. Some current functionality may be reused, but the final requirement is not to preserve the existing phase model as-is.

### 3.2 Required Conceptual Change
A process for a fund must be defined by:

- A global start date.
- A global end date.
- One or more configured application reception windows inside that global period.
- A calendar of process activities/events that can drive applicant-facing visual messaging and submission availability.

A process is not only about receiving applications. It conceptually covers everything that occurs within the fund process lifecycle.

### 3.3 Application Reception Windows
Inside the global process start and end dates, the administrator must be able to configure one or more reception windows.

Examples from the client:

- Fund/process name: `Nexus`.
- Overall process period: February 2026 through November 2026.
- Reception window 1: March 1, 2026 through June 1, 2026.
- Reception window 2: August 1, 2026 through September 1, 2026.

The system must support one, two, or more reception windows within the same fund process.

### 3.4 Hard Date and Time Enforcement
The configured dates and times are strict. There are no exceptions unless the administrator changes the configuration.

The submission button must only be enabled when all required business rules are satisfied, including but not limited to:

- The process exists and is active.
- The current date and time are inside an active application reception window.
- The applicant meets all other existing submission rules.
- The applicant has not exceeded their configured maximum funding amount for the process.
- The application meets all existing completeness and validation rules.

If the application cannot be submitted, the system must always explain why.

Examples of explanations:

- The application is outside the allowed submission period.
- The reception period has not started yet.
- The reception period has already closed.
- The applicant has incomplete required fields.
- The applicant exceeds the allowed funding amount for the process.
- Any other existing business rule prevents submission.

### 3.5 Draft Behavior Outside Reception Windows
Applicants should be able to create draft applications before the reception window opens if the process allows application preparation.

When the applicant is outside an active reception window, the system should communicate clearly that:

- Drafts may be created or edited.
- Submission is not yet allowed.
- The date when submission will become available must be shown.

Example message behavior:

> You cannot submit your application yet. You may create and edit drafts, but applications for this process will begin receiving submissions on [configured start date/time].

### 3.6 Applicant-Facing Countdown / Visual Notices
The applicant-facing UI must show professional, visually clear notices or countdown-style elements for process timing.

The client requested a “nice” and professional countdown-like visual experience, not just plain text.

The visual area should help applicants understand:

- When the current or next reception window starts.
- When the current reception window closes.
- Whether the applicant is currently allowed to submit.
- Whether the applicant can only create drafts.
- Why the submit button is disabled, if disabled.

This should appear prominently at the top of the relevant applicant screens.

---


### 3.7 Process Calendar Events

Processes must support configurable calendar activities/events as first-class configuration items.

Reception windows are a special type of process calendar event, but they are not the only possible event in the process lifecycle. A process represents the entire lifecycle of a fund initiative, and the calendar should be able to represent activities that influence applicant-facing messaging even when they do not directly enable submission.

Process calendar events may influence:

- Applicant-facing banners and status messages.
- Countdown components.
- Submission availability when the event is a reception window.
- Display of future process milestones.
- Display of closed or completed periods.

Minimum event fields should include:

- Event name.
- Event type, such as `reception_window`, `informational`, `deadline`, or other configured process milestone.
- Start date/time.
- End date/time when applicable.
- Description or applicant-facing message.
- Whether the event controls submission availability.
- Display order or calendar order.
- Active/inactive state.

The system must support future windows, active windows, and closed windows. The UI must make those states visible and understandable.

## 4. User-to-Process Assignment and Maximum Funding Amount

### 4.1 New Required Field When Assigning a User to a Fund/Process
When an administrator links a user/person to a fund process, the system must request a maximum amount that this person is allowed to request within that process.

This is a new requirement and does not currently exist.

The value represents the maximum total funding amount that the person can request across one or more applications within the same process.

### 4.2 Multiple Applications Within the Same Process
A person may have more than one application within the same process.

The system must ensure that the sum of all the person’s applications for that process does not exceed the maximum amount configured for that person in that process.

Example:

- Person assigned to Process A with a maximum allowed amount of CRC 1,000,000.
- The person submits multiple applications under Process A.
- The total of those applications must not exceed CRC 1,000,000.

### 4.3 Changing the Maximum Amount After Applications Exist
The administrator must be allowed to modify the maximum amount assigned to the person for the process.

However, if the person already has applications in progress or submitted applications, the system must evaluate whether the change puts any application at risk.

Example from the client:

- The person already requested CRC 1,000,000.
- The administrator changes the maximum amount to CRC 500,000.
- The system must not silently accept this change without warning.

### 4.4 Risk Alert When Reducing the Maximum Amount
If changing the maximum amount creates a conflict with existing applications, the system must alert the administrator.

The alert must include links to each impacted application.

The system must clearly show which applications would be at risk due to the new maximum amount.

### 4.5 Decision Required for Conflicting Existing Applications
The client noted that if the new maximum amount is lower than existing requested amounts, a decision must be made about what happens to those applications.

Possible business behaviors include, but are not limited to:

- Automatically declining applications that exceed the new limit.
- Blocking the maximum amount change until existing applications are resolved.
- Allowing the change but marking existing applications as requiring administrative action.
- Requiring the administrator to explicitly confirm an override.

This behavior must be defined before implementation. The system must not ignore the risk.

---

## 5. Impact Templates and Category Templates

### 5.1 Shared Requirement for Impact and Category Catalog Elements
The system currently has catalog/template concepts for impacts and categories used by item lines.

For each element configured in impact templates and category templates, a new field must be added:

- Field name: `Information`.
- Purpose: Provide explanatory/help text for the configured element.

### 5.2 Tooltip Display
The `Information` field must be shown to the applicant/user as a help tooltip.

The behavior should be similar to tooltips already used elsewhere in the system.

Expected behavior:

- User hovers over or focuses on the help indicator.
- The system displays the explanation configured in the `Information` field.
- The tooltip explains what the field or template element means.

This applies to both:

- Impact template elements.
- Category template elements.

### 5.3 New Data Type: Percentage
A new input/data type must be added for both impact templates and category templates:

- Type: `Percentage`.

The percentage type is primarily a visual/UI behavior.

The stored value should behave like the existing decimal type, but when rendered on screen, the system must automatically show the percentage symbol `%`.

The client clarified that this is effectively a decimal value with automatic percentage formatting.

### 5.4 Percentage Type Storage and Display
Implementation expectations:

- Store as a decimal/numeric value, consistent with existing decimal handling unless the technical architecture requires otherwise.
- Display the `%` symbol automatically in the UI.
- Avoid requiring the user to manually type the `%` symbol if that would create inconsistent data.
- Use the same validation principles as decimal values, with percentage-specific formatting.

---

## 6. Item Line Creation Flow

### 6.1 Product Name Must Come Before Category
When adding a new item line, the order of fields must change.

The first field shown must be the product name.

After the product name, the user must select the category.

Once the category is selected, the system must show the fields associated with that category.

Required order:

1. Product name.
2. Category selection.
3. Dynamic fields for the selected category.
4. Remaining item/quotation fields as applicable.

This ordering requirement is explicit and must not be omitted.

---

## 7. User Creation Form Ordering

When creating a user, the `Role` field must be moved to the first field in the form.

This is a UI ordering requirement.

Required behavior:

- The first field in the user creation form must be the role.
- The rest of the form should follow after role selection.
- If the role affects the remaining fields, the form should be able to react accordingly.

---

## 8. Password Recovery Email Verification

### 8.1 Issue Observed by Client
The client attempted to use the “Forgot Password” flow and did not receive the recovery email.

### 8.2 Requirement
The system must be reviewed to confirm whether password recovery emails are being sent correctly.

The review must include:

- Whether the email generation is triggered.
- Whether the email service provider is accepting the request.
- Whether the recipient email address is correct.
- Whether the email is being blocked, rejected, or sent to spam.
- Whether there are environment-specific issues.
- Whether templates and sender configuration are correct.

### 8.3 Expected Outcome
Users must reliably receive password recovery emails when using the forgot password flow.

If an email cannot be sent, the system should log enough information for support/administrators to diagnose the issue.

---

## 9. New Role: Auditor

### 9.1 Role Name
A new platform role must be added:

- Role name: `Auditor`.

### 9.2 Purpose of the Auditor Role
The Auditor role manages everything related to provider/supplier companies, especially regulatory compliance.

The auditor must be able to:

- Access the provider management screen.
- Review provider information.
- Validate provider regulatory compliance values.
- Reject providers or mark providers as not valid, if applicable.
- Update regulatory compliance conditions/statuses.
- Modify provider warning flags and warning notes.
- Participate in the application workflow after reviewer approval.
- Review applications in the audit stage.
- Download PDFs and supporting documents available to reviewers.
- Complete audit checklist items.
- Return applications to the reviewer when audit criteria are not met.
- Generate and review the agreement PDF.
- Confirm that the PDF is correct before it is sent to the applicant for digital signature.

### 9.3 Provider Management Access
The auditor must have access to the screen where provider regulatory compliance is managed.

This includes values related to:

- CCSS / Caja.
- Hacienda.
- SICOP / CCOP.
- PME/PYME flag.
- Provider warning flag.
- Provider warning note.
- Regulatory review/audit metadata.

### 9.4 Auditor Is Not Just a Viewer
The auditor must be able to modify, reject, validate, and update provider compliance data. The role must not be implemented as read-only if the workflow requires edits.

---

## 10. Provider Management Changes

### 10.1 Remove Electronic Invoice Checkbox
The provider screen currently includes a checkbox related to electronic invoice / electronic invoicing.

This checkbox must be removed.

The client explicitly stated that it does not apply, does not exist as a useful requirement, and has no interest for the client.

This requirement must not be skipped.


### 10.1.1 Complete Removal Scope for Electronic Invoice Validation

The electronic invoice requirement must be removed completely from the current business workflow. It must disappear from:

- Provider list screens if shown there.
- Provider create screens.
- Provider edit screens.
- Provider detail/read-only screens.
- Validation logic.
- Reviewer workflows.
- Auditor workflows.
- Compliance sections.
- Checklist logic or checklist items, if currently present.
- Recommendation logic, if currently referenced.
- Any applicant, reviewer, or auditor UI that implies electronic invoice compliance is required.

If historical data must remain in the database for migration or audit reasons, it must not remain visible or active as a current requirement unless the client explicitly approves it.

### 10.2 Clarification: Do Not Confuse This With Delivery Lead Time
The removal applies only to the provider checkbox related to electronic invoice / electronic invoicing.

This must not be confused with the new delivery lead time field used by the recommendation algorithm.

- Remove: electronic invoice checkbox / “plazo con factura electrónica” or electronic invoicing-related check.
- Keep/add: item quotation delivery lead time, expressed in days or months, because it is part of the recommendation algorithm.

### 10.3 Provider Regulatory Compliance Values Are No Longer Simple Checkboxes
The existing provider regulatory values must stop being simple checkboxes.

They must become status fields with enumerated values.

This applies to:

- Hacienda.
- CCSS / Caja.
- SICOP / CCOP.

The exact states are defined in Section 13.

### 10.4 Provider PME/PYME Flag
A new provider-level checkbox must be added to indicate whether the provider is a PME/PYME.

If the checkbox is selected, the provider receives 2 points in the recommendation algorithm for this criterion.

If the checkbox is not selected, the provider receives 1 point for this criterion.

### 10.5 Provider Warning Flag and Warning Note
The system must support a warning flag on a provider.

Purpose:

- The client does not necessarily want to reject the provider.
- The client wants to highlight the provider during review.
- The warning may exist for many possible reasons.
- Because the reason can vary, a free-text note must be supported.

Required fields:

- `Has warning` / warning flag: boolean.
- `Warning note`: free text.

Required display behavior:

- When a reviewer or auditor is reviewing an application, if a provider has a warning flag, the system must show a warning message.
- The warning message must include the note explaining why the warning exists.
- The warning must be visible enough to call attention to the provider but must not necessarily block the application unless separate blocking rules are defined.


### 10.5.1 Warning Creation Permissions

Reviewers and auditors may need to create or update supplier/provider warnings depending on the final permission model.

Minimum requirement:

- Auditors must be able to create and update provider warnings.
- Reviewers must at least be able to see warnings during review.
- If reviewers are allowed to create warnings, those warnings must become visible to auditors and should be auditable.

A warning does not automatically reject or disqualify the supplier unless a separate blocking rule is configured.

### 10.6 Notify Auditors When a New Provider Is Added
Every time a new provider is added, the system must notify the auditor group for review.

This requirement was explicitly added and must be implemented.

Expected behavior:

- A new provider is created in the system.
- The system sends a notification to the group of auditors.
- The notification must make it clear that the provider requires auditor review.
- The notification should include enough information for auditors to identify and open the provider record.
- The notification should support routing to all users with the Auditor role or to a configured auditor group, depending on current notification architecture.


Additional rule:

- This notification applies regardless of how the provider is created, including manual creation by a user, import, future integrations, or any other creation source.

Recommended notification content:

- Provider name.
- Provider identification number, if available.
- Date/time created.
- User who created the provider, if available.
- Link to provider detail/review screen.
- Message indicating that regulatory compliance should be reviewed.

---

## 11. Application Workflow Change: Auditor Stage Before Agreement Generation

### 11.1 Current Workflow
Currently, after the reviewer finishes the back-and-forth conversation with the applicant and decides the application is ready, the next step is to generate the agreement PDF that the applicant must sign.

### 11.2 Required Workflow Change
The system must add a new intermediate workflow stage between reviewer completion and agreement PDF generation.

The new stage is the auditor stage.

The reviewer must no longer send the application directly to agreement/PDF generation.

### 11.3 High-Level New Workflow
The required workflow is:

1. Applicant submits application.
2. Reviewer reviews the application.
3. Reviewer communicates with applicant as needed.
4. Reviewer decides the application is ready for the next stage.
5. Reviewer completes the reviewer checklist.
6. System enables the action to send the application to audit only after all checklist items are checked.
7. Application moves to a new audit status/state.
8. Auditor sees the application in their audit inbox/screen.
9. Auditor reviews all application information and documents.
10. Auditor completes the same or relevant audit checklist.
11. If all criteria pass, auditor approves the application for agreement generation.
12. Auditor generates the agreement PDF.
13. Auditor reviews the generated PDF.
14. Auditor confirms through an additional checkbox that the PDF is correct.
15. System sends the agreement to the applicant for digital signature.
16. Application moves to the state that waits for digital signature.
17. Applicant receives the respective emails to sign.
18. Existing downstream flow continues as it works today.

### 11.4 New Application State
A new state/status must be added to the application flow to represent that the application is in audit.

Possible status names:

- `Pending Audit`.
- `In Audit Review`.
- `Audit Review`.

The exact display name can be finalized later, but the workflow state must exist.

### 11.5 Reviewer Sends to Auditor, Not Directly to Applicant
When the reviewer completes the review, the reviewer sends the application to the auditor.

The reviewer does not send it directly to the applicant for signature.

### 11.6 Auditor Return Path
If the auditor finds checklist items that do not comply, the auditor must provide a reason for each non-compliant item.

The application must be returned to the reviewer.

The application must not be immediately returned to the applicant.

The reviewer must then decide what to do next, including whether the issue was reviewer error, whether more applicant information is required, or whether internal correction is needed.

### 11.7 Auditor Approval Path
When the auditor confirms everything is correct:

- The system allows moving to agreement generation.
- The auditor generates the PDF.
- The auditor reviews the PDF.
- The auditor checks an additional confirmation that the PDF is correct.
- Only after that confirmation can the system send the PDF/agreement to the applicant for digital signature.

---

## 12. Reviewer and Auditor Checklist Templates

### 12.1 New Checklist Template Configuration
A new checklist template configuration screen is required.

The checklist is intentionally simple.

It consists of a list of text items.

Each text item represents something that the reviewer and auditor must verify.

### 12.2 Checklist Usage by Reviewer
When the reviewer is ready to send the application to the next stage, the system must show:

- The application information.
- The checklist beside or alongside the application information.

The reviewer must check each item.

Only when all checklist items are checked should the button/action to send the application to audit become enabled.

### 12.3 Checklist Usage by Auditor
The auditor must also see a checklist during the audit review.

The auditor must mark items as compliant or not compliant.

If an item does not comply, the auditor must provide a reason.

The reason must be sent back to the reviewer when the application is returned.

### 12.4 Checklist Requirements
The checklist must support:

- Text-only checklist items.
- Active/inactive items if needed by administration.
- Ordering of checklist items if needed.
- Mandatory completion before moving to the next workflow step.
- Capturing who completed each checklist item.
- Capturing date/time of completion.
- Capturing non-compliance reasons when items fail audit.

### 12.5 Same Checklist or Role-Specific Checklist
The client described the checklist as similar for reviewer and auditor. The implementation may use the same checklist template for both steps or allow role-specific checklist groups.

Minimum requirement:

- Reviewer must complete the checklist before sending to audit.
- Auditor must complete the checklist before approving or returning the application.

---

## 13. Provider Regulatory Compliance Status Model

### 13.1 General Requirement
The provider compliance values must be changed from simple checkboxes to explicit status values.

The statuses are used by the recommendation algorithm.

They also require auditability and review freshness tracking.

### 13.2 Hacienda Status Values
The Hacienda status field must support the following possible values exactly as provided by the client:

1. `sin inscripción`
2. `al día`
3. `estado moroso`
4. `cobro administrativo`
5. `desinscrito al día`
6. `sin información`
7. `desinscrito moroso`
8. `desinscrito de oficio`

Scoring rule:

- Only `al día` receives 2 points.
- All other values receive 1 point.

### 13.3 CCSS / Caja Status Values
The CCSS / Caja status field must support the following possible values exactly as provided by the client:

1. `sin inscripción`
2. `al día`
3. `estado moroso`
4. `cobro administrativo`
5. `estado inactivo / al día`
6. `estado inactivo / moroso`
7. `sin información`
8. `cobro judicial`

Scoring rule:

- Only `al día` receives 2 points.
- All other values receive 1 point.

### 13.4 SICOP / CCOP Status Values
The client referred to this concept as both SICOP and CCOP in different parts of the notes. The system must confirm the canonical label used internally and in the UI. Until confirmed, this document refers to it as `SICOP / CCOP`.

The status field must support the following possible values exactly as provided by the client:

1. `inhabilitación`
2. `sin sanciones`
3. `sin suscripción`
4. `con sanciones`
5. `suspensión`

Scoring rule:

- Only `sin sanciones` receives 2 points.
- All other values receive 1 point.

### 13.5 Regulatory Compliance Values Are Provider-Level Data
The client explicitly clarified that regulatory values belong to the provider at a national level.

They are not quotation-specific and should not move to the quotation/item screen.

They should remain on the provider management screen where they are today, but with the required changes:

- Replace checkboxes with status fields.
- Add audit tracking.
- Add last review/authorization timestamps.
- Add PME/PYME checkbox.
- Remove the electronic invoice checkbox.

---

## 14. Supplier Recommendation Algorithm

### 14.1 Purpose
The platform currently recommends a provider/supplier. The recommendation algorithm must be changed so that it evaluates multiple criteria, not only price.

The recommended provider is always the one with the highest score.

The lowest price does not always win because regulatory compliance, delivery lead time, warranty time, and PME/PYME status must also be considered.

### 14.2 Criteria Included in the Algorithm
The algorithm must include the following criteria:

1. Price.
2. Delivery lead time.
3. Warranty time.
4. Hacienda regulatory status.
5. CCSS / Caja regulatory status.
6. SICOP / CCOP regulatory status.
7. PME/PYME flag.

### 14.3 Quote-Level Criteria
The following criteria depend explicitly on the quotation/item line:

- Price.
- Delivery lead time.
- Warranty time.

These values can differ across providers for the same item/application.

### 14.4 Provider-Level Criteria
The following criteria are provider-level and national-level:

- Hacienda status.
- CCSS / Caja status.
- SICOP / CCOP status.
- PME/PYME flag.

These values are expected to be the same for the provider across applications and quotes.

### 14.5 Required New/Used Fields for Item/Quotation
When adding an item or provider quotation, the following fields must be captured and used by the algorithm:

#### 14.5.1 Delivery Lead Time
- Field: delivery lead time.
- Type: numeric value plus unit.
- Unit options: days or months.
- Meaning: how long the provider takes to deliver.
- Algorithm direction: lower delivery lead time is better.

#### 14.5.2 Warranty Time
- Field: warranty time.
- Type: numeric value plus unit.
- Unit options: days or months.
- Meaning: warranty period offered by the provider.
- Algorithm direction: this must be confirmed by the business if not already defined in implementation. The expected interpretation is that a longer warranty is better, but the client did not explicitly state the scoring direction in the notes. This must not be ignored.

#### 14.5.3 Price
- Field: price.
- Meaning: quoted price by the provider.
- Algorithm direction: lower price is better.

### 14.6 Base Scoring Principle
For each evaluated criterion, providers receive at least 1 point.

The client explicitly stated that all providers always receive 1 point.

When a provider wins a criterion, the provider receives 2 points for that criterion.

Example from the client for a criterion such as delivery lead time:

- Three providers are evaluated.
- The provider with the better value receives 2 points.
- The other two providers receive 1 point each.
- Score distribution for that criterion: `2, 1, 1`.


### 14.6.1 No Separate Standalone Base Score

Do not implement an additional standalone `Base Score` on top of the criterion scores.

The intended base-scoring concept is that each criterion gives every provider at least 1 point. Therefore, the total score must be calculated by summing the criterion scores only:

- Price.
- Delivery lead time.
- Warranty time.
- Hacienda.
- CCSS/Caja.
- SICOP/CCOP.
- PME/PYME.

Adding a separate base point would change the score scale and could create a mismatch with the client’s intended algorithm.

### 14.7 Delivery Lead Time Scoring
Delivery lead time is compared across providers.

Rule:

- Provider(s) with the shortest delivery lead time receive 2 points.
- Other providers receive 1 point.

If delivery values use different units, the system must normalize them before comparison.

Recommended normalization:

- Convert all values to days for scoring.
- Store the original value and unit for display.

Tie behavior for delivery lead time was not explicitly defined. The safest default is:

- If multiple providers tie for the shortest delivery lead time, all tied providers receive 2 points.
- Other providers receive 1 point.

This tie behavior should be confirmed with the business.

### 14.8 Warranty Time Scoring
Warranty time must be included in the algorithm.

The client specified that warranty must be expressed as days or months.

The client did not explicitly state whether the highest warranty time or lowest warranty time wins.

Expected business interpretation:

- Longer warranty is better.
- Provider(s) with the longest warranty receive 2 points.
- Other providers receive 1 point.

If warranty values use different units, the system must normalize them before comparison.

Recommended normalization:

- Convert all values to days for scoring.
- Store the original value and unit for display.

Tie behavior should be handled consistently:

- If multiple providers tie for the longest warranty, all tied providers receive 2 points.
- Other providers receive 1 point.

Because the client did not explicitly define the warranty scoring direction, this is an implementation assumption that must be validated.

### 14.9 Price Scoring
Price must be included in the algorithm.

Rule:

- The provider with the lowest price receives 2 points.
- Other providers receive 1 point.

The client explicitly stated that ties for price receive 1 point.


Source reconciliation note:

- One source document stated that all tied lowest-price suppliers receive 2 points.
- The more detailed source explicitly stated that price ties receive 1 point and no provider receives 2 points in a price tie scenario.
- This unified version uses the explicit client-stated rule: tied lowest-price providers receive 1 point.

Required tie behavior:

- If providers tie on the lowest price, all tied providers receive 1 point.
- No provider receives 2 points for price in a tie scenario.

### 14.10 Hacienda Scoring
Rule:

- `al día` receives 2 points.
- Every other Hacienda value receives 1 point.

### 14.11 CCSS / Caja Scoring
Rule:

- `al día` receives 2 points.
- Every other CCSS/Caja value receives 1 point.

### 14.12 SICOP / CCOP Scoring
Rule:

- `sin sanciones` receives 2 points.
- Every other SICOP/CCOP value receives 1 point.

### 14.13 PME/PYME Scoring
Rule:

- If the provider is marked as PME/PYME, the provider receives 2 points.
- If the provider is not marked as PME/PYME, the provider receives 1 point.

### 14.14 Total Score Calculation
For each provider candidate, the system must calculate:

```text
Total Score =
  Price Score
+ Delivery Lead Time Score
+ Warranty Time Score
+ Hacienda Score
+ CCSS/Caja Score
+ SICOP/CCOP Score
+ PME/PYME Score
```

The provider with the highest total score is the recommended provider.

### 14.15 Recommendation Output
The UI must show which provider is recommended.

The system should also expose enough detail to explain why the provider was recommended.

Recommended display:

- Total score.
- Per-criterion score.
- Price value.
- Delivery lead time value and unit.
- Warranty value and unit.
- Hacienda status.
- CCSS/Caja status.
- SICOP/CCOP status.
- PME/PYME flag.

This transparency is important because the recommendation is no longer based only on price.

### 14.16 Tie on Final Total Score
The client did not specify what happens if two or more providers have the same total score.

The system must define a tie-breaking behavior before implementation.

Possible tie-breaking options:

1. Show all tied providers as recommended.
2. Use lowest price as a tie-breaker.
3. Require manual reviewer/auditor selection.
4. Use a configured priority order.

This is an open business decision.

---

## 15. Provider Regulatory Audit Trail and Review Freshness

### 15.1 Audit Trail Requirement
The system must maintain an audit trail for each provider whenever an auditor changes any regulatory compliance value.

This applies to changes made manually by an auditor and changes made in the future by automated processes.

The audit trail must capture at least:

- Provider ID.
- Provider name.
- Field changed.
- Previous value.
- New value.
- Change type: manual or automated.
- User who changed it, if manual.
- Process or system job that changed it, if automated.
- Date/time of change.
- Optional reason or note.

### 15.2 Regulatory Values Covered by Audit
The audit trail must cover changes to:

- Hacienda status.
- CCSS / Caja status.
- SICOP / CCOP status.
- PME/PYME flag, if changed.
- Warning flag and warning note, if changed.

At minimum, the regulatory compliance statuses must be auditable.

### 15.3 Separate Last Review/Authorization Fields
For provider regulatory compliance, the system must maintain separate audit/review timestamp fields for each relevant regulatory value.

The client explicitly requested separate fields for:

- Last time CCSS / Caja was reviewed/authorized.
- Last time SICOP / CCOP was reviewed/authorized.

Hacienda is also part of regulatory compliance, but the client mentioned an automated Hacienda API sync. The system should still keep Hacienda audit timestamps and history for traceability.

Recommended provider fields:

- `haciendaStatus`.
- `haciendaLastReviewedAt`.
- `haciendaLastReviewedBy`.
- `haciendaLastUpdatedSource` (`manual`, `api`, `system`).
- `ccssStatus`.
- `ccssLastReviewedAt`.
- `ccssLastReviewedBy`.
- `ccssLastUpdatedSource` (`manual`, `system`).
- `sicopOrCcopStatus`.
- `sicopOrCcopLastReviewedAt`.
- `sicopOrCcopLastReviewedBy`.
- `sicopOrCcopLastUpdatedSource` (`manual`, `system`).

### 15.4 Display During Application Review
When a reviewer or auditor reviews an application, the system must show the regulatory review freshness information as part of the provider information.

The display must make it clear when each value was last reviewed.

Example display behavior:

- Hacienda status: `al día` — last updated today by API.
- CCSS/Caja status: `al día` — last reviewed 15 days ago by [auditor name].
- SICOP/CCOP status: `sin sanciones` — last reviewed 15 days ago by [auditor name].

The client explicitly wants the reviewer/auditor to see information such as “this was last done 15 days ago.”

### 15.5 Blocking Rule When Review Is Older Than One Month
If the review timestamp for a required regulatory value is older than one month, the system must not allow the application/request to advance.

The system must block progress and request that an auditor review and update or re-authorize those fields.

Required behavior:

- During application review/audit, check regulatory review freshness for provider fields.
- If a required field was last reviewed more than one month ago, block the workflow from advancing.
- Show a clear message explaining which provider and which regulatory field is stale.
- Direct the user to request auditor review or route the item to auditor review.

Example blocking message:

> Provider regulatory information is older than one month. An auditor must review and update or re-authorize the CCSS/Caja and/or SICOP/CCOP fields before this request can continue.

### 15.6 Reviewed but Unchanged Scenario
The client explicitly noted that a regulatory value may remain the same after one month.

The system must support a case where an auditor reviews the field and confirms that the value is still the same.

This must refresh the last reviewed/authorized timestamp without forcing a value change.

Required behavior:

- Auditor opens provider regulatory compliance review.
- Auditor confirms that the current value was reviewed and remains valid.
- System records a new review event.
- System updates the corresponding last reviewed/authorized timestamp.
- System does not require the value itself to change.
- Audit trail records that the field was reviewed and unchanged.

Suggested action label:

- `Reviewed - No Change`.
- `Re-authorize Current Value`.
- `Confirm Current Status`.

### 15.7 Manual and Automated Changes Must Both Be Auditable
If an auditor changes a value manually, audit history must be created.

If a future automated process changes a value, audit history must also be created.

The client explicitly wants future automated changes to be integrated into the audit model.

---

## 16. Automated Hacienda API Synchronization

### 16.1 Existing API
The client stated that there is currently an API that can consult the Hacienda administration status for an identification number.

This API must be integrated into the system.

### 16.2 Daily Morning Execution
The system must execute an automated process every morning for all providers.

The process must:

1. Iterate through all providers.
2. Use the provider identification number to query the Hacienda API.
3. Retrieve the current Hacienda status.
4. Update the provider’s Hacienda status automatically if needed.
5. Record audit history for the check/update.
6. Update Hacienda last reviewed/updated metadata.

### 16.3 Audit Behavior for Hacienda API Sync
The automated job must create audit records.

If the Hacienda value changes:

- Record previous value.
- Record new value.
- Mark source as API/automated.
- Record job execution date/time.

If the Hacienda value remains the same:

- Record that the provider was checked.
- Refresh or record last checked/reviewed metadata as appropriate.
- Preserve evidence that the API validation occurred.

### 16.4 Error Handling
The automated job must handle API errors safely.

Required behavior:

- If the API is unavailable, the job must not corrupt provider data.
- If a provider identification is invalid or missing, the job must record the error.
- Administrators/auditors should be able to see which providers failed automatic verification.
- Failed verification should not be silently ignored.

### 16.5 Scheduling
The execution time must be configurable or at least documented.

The client requested that it runs every day in the morning.

---

## 17. Application Review Blocking Based on Provider Regulatory Freshness

### 17.1 Why This Exists
Provider regulatory values are used by the recommendation algorithm and application review. If these values are outdated, reviewers and auditors must not continue as if they were current.

### 17.2 Where the Check Must Happen
The freshness check must happen when a reviewer or auditor is reviewing an application and attempts to advance the workflow.

It should also be visible before the final blocking point so users understand the issue early.

### 17.3 Blocking Condition
If any required provider regulatory review timestamp exceeds one month, the system must block the application from advancing.

At minimum, this applies to the provider fields for:

- CCSS / Caja.
- SICOP / CCOP.

Hacienda should remain fresh through the daily API process, but if Hacienda freshness is also tracked and becomes stale due to API failures, the system should surface that condition.

### 17.4 Required User Guidance
When blocking, the system must explain:

- Which provider is affected.
- Which regulatory field is stale.
- When it was last reviewed.
- That an auditor must review and update or re-authorize the value.

---

## 18. Auditor Inbox / Audit Screen

### 18.1 Purpose
Auditors need a screen or inbox showing all applications that require audit.

### 18.2 Required Information
The auditor must be able to see all information needed to perform the audit.

This must be equivalent to reviewer access for the purpose of reviewing the application.

The auditor must have access to:

- Application details.
- Applicant information.
- Requested items.
- Provider information.
- Provider regulatory compliance statuses.
- Provider warning flags and notes.
- Regulatory review freshness information.
- Impact/category data.
- Supporting documents.
- Generated or existing PDFs.
- Any documents that reviewers can download.
- Conversation/history relevant to the application review.

### 18.3 Download Access
Auditors must be able to download PDFs and supporting documents, similar to reviewers.

### 18.4 Audit Actions
Auditors must be able to:

- Approve audit checklist items.
- Mark checklist items as non-compliant.
- Enter reasons for non-compliance.
- Return the application to reviewer.
- Approve the application for PDF generation.
- Generate agreement PDF.
- Confirm PDF correctness.
- Send the application to the applicant for digital signature.

---

## 19. Agreement PDF Generation and Confirmation

### 19.1 PDF Generation Moves to Auditor Stage
After the auditor approves the audit review, the auditor generates the agreement PDF.

### 19.2 PDF Review by Auditor
The auditor must review the generated PDF.

### 19.3 Additional PDF Correctness Confirmation
The auditor must have one additional checkbox/action confirming:

- The PDF is correct.
- The PDF is ready to be sent to the applicant.

Only after this confirmation should the system send the agreement to the applicant for signature.

### 19.4 Applicant Notification
Once the auditor confirms the PDF and sends it forward:

- The application changes to the state waiting for digital signature.
- The applicant receives the corresponding email notification(s).
- The applicant is instructed to sign the agreement digitally.
- The existing downstream flow continues as currently implemented.

---

## 20. Applicant-Facing Timeline / Progress Visualization

### 20.1 Current Situation
The current applicant-facing timeline is not sufficient.

The client requested a much nicer timeline.

### 20.2 Required Timeline Behavior
The applicant-facing screen must show a visual timeline that clearly indicates:

- The different stages of the application.
- The current stage.
- What has already been completed.
- What remains pending.
- Progress toward the final disbursement step.

### 20.3 Percentage Progress
Because the stages are finite, the timeline should show percentage progress.

Example from the client:

- The UI may show that the application is at 7% progress toward the final disbursement.

The exact percentage calculation must be based on the configured or defined finite workflow stages.

### 20.4 Final Stage
The timeline must communicate progress toward the final part of the flow, which is disbursement.

---

## 21. Provider Notification and Review Lifecycle

### 21.1 New Provider Created
When a new provider is added, the system must notify auditors.

### 21.2 Auditor Review Required
Auditors should review provider regulatory compliance values and complete or confirm relevant statuses.

### 21.3 Provider Can Be Used With Warning
Some providers may not be rejected but may have a warning shown during review.

### 21.4 Provider Regulatory Values Must Remain Current
Provider regulatory values must be maintained manually by auditors and automatically by API where applicable.

If required values become stale, application progress must be blocked until review is performed.

---

## 22. Data Model Considerations

### 22.1 Fund Process
Suggested fields:

- `id`.
- `name`.
- `fundId`.
- `startDateTime`.
- `endDateTime`.
- `isActive`.
- `createdAt`.
- `createdBy`.
- `updatedAt`.
- `updatedBy`.

### 22.2 Application Reception Window
Suggested fields:

- `id`.
- `processId`.
- `name`.
- `startDateTime`.
- `endDateTime`.
- `description`.
- `isActive`.
- `displayOrder`.


### 22.2A Process Event

Suggested fields:

- `id`.
- `processId`.
- `eventType` (`reception_window`, `informational`, `deadline`, `milestone`, or configured equivalent).
- `name`.
- `description`.
- `startDateTime`.
- `endDateTime`.
- `controlsSubmissionAvailability`.
- `applicantFacingMessage`.
- `isActive`.
- `displayOrder`.
- `createdAt`.
- `createdBy`.
- `updatedAt`.
- `updatedBy`.

Reception windows may be implemented as a specialized table, as process events with `eventType = reception_window`, or both if the architecture requires it. The important requirement is that reception windows and other calendar activities can drive UI messaging and submission permissions.

### 22.3 User Process Assignment
Suggested fields:

- `id`.
- `userId`.
- `processId`.
- `maximumAllowedAmount`.
- `currency`.
- `createdAt`.
- `createdBy`.
- `updatedAt`.
- `updatedBy`.

### 22.4 Template Field / Catalog Element
Suggested fields:

- `id`.
- `templateId`.
- `name`.
- `label`.
- `type`.
- `information`.
- `isRequired`.
- `displayOrder`.
- `isActive`.

The `type` field must include the new `Percentage` option.

### 22.5 Provider
Suggested fields:

- `id`.
- `name`.
- `identificationNumber`.
- `haciendaStatus`.
- `haciendaLastReviewedAt`.
- `haciendaLastReviewedBy`.
- `haciendaLastUpdatedSource`.
- `ccssStatus`.
- `ccssLastReviewedAt`.
- `ccssLastReviewedBy`.
- `ccssLastUpdatedSource`.
- `sicopOrCcopStatus`.
- `sicopOrCcopLastReviewedAt`.
- `sicopOrCcopLastReviewedBy`.
- `sicopOrCcopLastUpdatedSource`.
- `isPmeOrPyme`.
- `hasWarning`.
- `warningNote`.
- `createdAt`.
- `createdBy`.
- `updatedAt`.
- `updatedBy`.

The existing electronic invoice checkbox must be removed from the provider model and UI unless it is needed only for historical migration. It must not remain visible as a current business requirement.

### 22.6 Provider Regulatory Audit Event
Suggested fields:

- `id`.
- `providerId`.
- `fieldName`.
- `previousValue`.
- `newValue`.
- `eventType` (`value_changed`, `reviewed_no_change`, `api_checked`, `api_changed`, `manual_changed`).
- `source` (`manual`, `api`, `system`).
- `reviewedByUserId`.
- `jobExecutionId`.
- `note`.
- `createdAt`.

### 22.7 Item Provider Quote / Supplier Candidate
Suggested fields:

- `id`.
- `applicationItemId`.
- `providerId`.
- `price`.
- `currency`.
- `deliveryLeadTimeValue`.
- `deliveryLeadTimeUnit` (`days`, `months`).
- `warrantyTimeValue`.
- `warrantyTimeUnit` (`days`, `months`).
- `normalizedDeliveryLeadTimeDays`.
- `normalizedWarrantyTimeDays`.
- `createdAt`.
- `createdBy`.
- `updatedAt`.
- `updatedBy`.

### 22.8 Recommendation Score Detail
Suggested fields:

- `id`.
- `applicationItemId`.
- `providerId`.
- `priceScore`.
- `deliveryLeadTimeScore`.
- `warrantyTimeScore`.
- `haciendaScore`.
- `ccssScore`.
- `sicopOrCcopScore`.
- `pmeOrPymeScore`.
- `totalScore`.
- `isRecommended`.
- `calculatedAt`.

### 22.9 Checklist Template
Suggested fields:

- `id`.
- `name`.
- `description`.
- `appliesToStage` (`reviewer`, `auditor`, or both).
- `isActive`.
- `createdAt`.
- `createdBy`.

### 22.10 Checklist Template Item
Suggested fields:

- `id`.
- `checklistTemplateId`.
- `text`.
- `displayOrder`.
- `isRequired`.
- `isActive`.

### 22.11 Application Checklist Response
Suggested fields:

- `id`.
- `applicationId`.
- `stage` (`reviewer`, `auditor`).
- `checklistTemplateItemId`.
- `status` (`checked`, `not_compliant`, `not_applicable` if allowed).
- `nonComplianceReason`.
- `completedByUserId`.
- `completedAt`.

---


### 22.11A Provider Warning History / Supplier Warning

The current provider warning can be stored as fields on the Provider entity, but the system should consider a separate warning history entity if warnings need traceability.

Suggested fields if implemented as a separate entity:

- `id`.
- `providerId`.
- `warningText`.
- `isActive`.
- `createdByUserId`.
- `createdByRole`.
- `createdAt`.
- `updatedByUserId`.
- `updatedAt`.
- `resolvedByUserId`.
- `resolvedAt`.
- `resolutionNote`.

Minimum requirement remains the current-state provider warning flag and free-text warning note.

## 23. Permission Requirements

### 23.1 Auditor Permissions
The Auditor role must be granted permissions to:

- View provider list.
- View provider details.
- Create/update provider regulatory compliance values, if applicable.
- Validate/reject providers if the system supports provider validation state.
- Update warning flag and warning note.
- View audit history.
- Re-authorize current regulatory values without changing them.
- Access audit inbox.
- View application details in audit stage.
- Download application documents and PDFs.
- Complete audit checklists.
- Return application to reviewer.
- Approve application for PDF generation.
- Generate agreement PDF.
- Confirm PDF correctness.
- Send agreement to applicant for signature.

### 23.2 Reviewer Permissions
Reviewer permissions must be updated to:

- Complete reviewer checklist.
- Send application to audit after all checklist items are completed.
- Receive applications returned by auditor.
- See auditor non-compliance reasons.
- Continue working on returned applications without automatically exposing internal audit feedback to the applicant unless the reviewer decides to communicate it.

### 23.3 Applicant Permissions
Applicants must:

- See process timing notices and countdown information.
- Create drafts when allowed.
- Submit only during active reception windows and when all other rules are satisfied.
- See improved application timeline and percentage progress.
- Receive email notification when agreement is ready for digital signature.

---

## 24. UI/UX Requirements

### 24.1 Applicant Process Notices
The applicant UI must include professional visual notices/countdowns for process windows.

### 24.2 Disabled Submit Button Explanation
Whenever the submit button is disabled, the UI must explain why.

### 24.3 Template Tooltips
Impact and category template fields must show configured information as tooltips.

### 24.4 Item Line Field Order
Item line creation must show product name first, then category, then category-specific fields.

### 24.5 User Creation Role Field
User creation must show role as the first field.

### 24.6 Provider Warning Display
Provider warning messages must be visible to reviewers and auditors during application review.

### 24.7 Regulatory Freshness Display
Provider regulatory compliance values shown during review must include last reviewed/authorized information.

### 24.8 Applicant Timeline
The applicant timeline must be visually improved and include percentage progress toward disbursement.

---

## 25. Notifications


### 25.0 Password Recovery Notification

Password recovery emails are a required notification path and must be reliable.

Trigger:

- User requests password recovery through the forgot-password flow.

Recipients:

- The requesting user, at the registered email address.

Purpose:

- Deliver the recovery/reset instructions successfully.

Required operational behavior:

- The system must log delivery attempts and failures.
- Administrators/support must have enough information to diagnose non-delivery.
- Email templates, sender configuration, provider/SMTP configuration, queues, and delivery logs must be reviewed.

### 25.1 New Provider Notification
Trigger:

- A new provider is created.

Recipients:

- Auditor group or all users with Auditor role.

Purpose:

- Notify auditors that the provider requires review.

### 25.2 Applicant Signature Notification
Trigger:

- Auditor confirms generated PDF is correct and sends it to applicant.

Recipients:

- Applicant.

Purpose:

- Notify applicant that the agreement is ready for digital signature.

### 25.3 Potential Notifications for Stale Regulatory Values
The system should consider notifying auditors when provider regulatory review timestamps become older than one month or are approaching one month.

This was not explicitly requested as a notification, but the blocking rule requires auditor action. A notification would reduce operational friction.

---


### 25.4 Audit Rejection / Return-to-Reviewer Notification

This notification is recommended to reduce operational friction.

Trigger:

- Auditor marks one or more checklist items as non-compliant and returns the application to the reviewer.

Recipients:

- Assigned reviewer or reviewer group.

Purpose:

- Notify the reviewer that the application was returned from audit and requires attention.

Recommended content:

- Application identifier.
- Applicant name.
- Auditor name.
- Checklist items marked as non-compliant.
- Non-compliance reasons.
- Direct link to the application review screen.

## 26. Business Rules Summary

### 26.1 Submission Window Rule
Applicants can submit only inside active configured reception windows.

### 26.2 Draft Rule
Applicants may create drafts outside reception windows if allowed by the process configuration.

### 26.3 Explanation Rule
Every disabled submission action must explain the reason.

### 26.4 Maximum Amount Rule
The sum of a person’s applications within a process must not exceed their configured maximum amount.

### 26.5 Maximum Amount Change Risk Rule
If an administrator changes a maximum amount and existing applications are affected, the system must alert the administrator with links to affected applications.

### 26.6 Electronic Invoice Checkbox Removal Rule
The provider electronic invoice checkbox must be removed.

### 26.7 New Provider Notification Rule
Every new provider must trigger notification to auditors.

### 26.8 Provider Regulatory Status Rule
Provider regulatory compliance fields must use enumerated statuses, not simple checkboxes.

### 26.9 Recommendation Rule
The recommended provider is the provider with the highest total score.

### 26.10 Lowest Price Does Not Always Win Rule
Price is only one factor in the recommendation algorithm.

### 26.11 Provider Regulatory Freshness Rule
If required provider regulatory review is older than one month, the application must not advance until an auditor reviews and updates or re-authorizes the values.

### 26.12 Reviewed No Change Rule
Auditors must be able to review a regulatory value and refresh its review timestamp without changing the value.

### 26.13 Reviewer Checklist Rule
The reviewer cannot send the application to audit until all required reviewer checklist items are completed.

### 26.14 Auditor Checklist Rule
The auditor cannot approve the application for agreement/PDF generation until required audit checklist items are completed.

### 26.15 Auditor Non-Compliance Rule
If an auditor marks checklist items as non-compliant, the auditor must enter a reason and the application must return to the reviewer, not directly to the applicant.

### 26.16 PDF Confirmation Rule
The agreement PDF cannot be sent to the applicant until the auditor confirms that the generated PDF is correct.

---

## 27. Acceptance Criteria

### 27.1 Fund Process and Reception Windows
- Admin can create a process with global start and end dates.
- Admin can configure one or more reception windows inside the process period.
- Applicant can see visual timing notices/countdown information.
- Applicant can create drafts before a reception window opens, if allowed.
- Applicant cannot submit outside an active reception window.
- Disabled submit action explains why submission is not allowed.

### 27.2 User Maximum Amount Per Process
- Admin must enter a maximum amount when assigning a user/person to a process.
- System calculates the sum of applications per person per process.
- System prevents or flags applications that exceed the maximum amount according to final business decision.
- If admin lowers the maximum amount and existing applications are affected, admin receives a warning with links to affected applications.

### 27.3 Impact and Category Templates
- Template elements include an `Information` field.
- The information is displayed as a tooltip in forms.
- Both impact and category templates support a `Percentage` type.
- Percentage values are displayed with `%` automatically.

### 27.4 Item Line Creation
- Product name appears before category.
- Category-specific fields appear after category selection.

### 27.5 User Creation
- Role appears as the first field in the user creation form.

### 27.6 Password Recovery
- Forgot password flow sends email successfully.
- Failures are logged and diagnosable.

### 27.7 Auditor Role
- Auditor role exists.
- Auditor can access provider management.
- Auditor can update regulatory compliance values.
- Auditor can access audit inbox.
- Auditor can review applications in audit state.
- Auditor can download the same relevant documents/PDFs as reviewer.

### 27.8 Provider Management
- Electronic invoice checkbox is removed.
- Hacienda, CCSS/Caja, and SICOP/CCOP are status fields, not checkboxes.
- PME/PYME checkbox exists.
- Provider warning flag and warning note exist.
- Provider warning is shown during review when applicable.
- New provider creation triggers auditor notification.

### 27.9 Recommendation Algorithm
- Algorithm includes price, delivery lead time, warranty time, Hacienda, CCSS/Caja, SICOP/CCOP, and PME/PYME.
- Every criterion gives at least 1 point.
- Winning criterion values receive 2 points according to rules.
- Hacienda `al día` receives 2 points.
- CCSS/Caja `al día` receives 2 points.
- SICOP/CCOP `sin sanciones` receives 2 points.
- PME/PYME checked receives 2 points.
- Lowest price receives 2 points unless tied; if tied, tied providers receive 1 point.
- Recommended provider is the provider with the highest total score.
- UI exposes enough information to explain the recommendation.

### 27.10 Regulatory Audit and Freshness
- Every manual regulatory compliance change creates audit history.
- Automated Hacienda API checks/changes create audit history.
- Separate last reviewed timestamps exist for CCSS/Caja and SICOP/CCOP.
- Hacienda also has traceability for API checks and status changes.
- Review screens show when each provider regulatory value was last reviewed.
- If required review is older than one month, application progress is blocked.
- Auditor can confirm current value remains valid without changing the value.

### 27.11 Audit Workflow
- Reviewer completes checklist before sending to audit.
- Application moves to audit state.
- Auditor sees application in audit inbox/screen.
- Auditor completes checklist.
- Non-compliance requires reason and returns to reviewer.
- Auditor approval enables agreement PDF generation.
- Auditor confirms generated PDF is correct.
- Applicant receives signature notification only after auditor PDF confirmation.

### 27.12 Applicant Timeline
- Applicant sees improved visual timeline.
- Timeline shows current stage.
- Timeline shows progress percentage toward disbursement.

---

## 28. Open Questions and Decisions Needed

These items must not be interpreted as omissions. They are captured because the client’s notes imply a need for a final business decision before coding.

### 28.1 Maximum Amount Conflict Behavior
When an admin lowers a person’s maximum amount below existing application totals, should the system:

- Auto-decline affected applications?
- Block the maximum amount change?
- Allow the change but mark applications as at risk?
- Require admin confirmation and manual resolution?

### 28.2 Warranty Scoring Direction
The client stated that warranty time must be included and expressed in days or months, but did not explicitly state whether longer or shorter warranty wins.

The expected business rule is that longer warranty is better, but this should be confirmed.

### 28.3 Final Score Tie-Breaking
If two or more providers have the same total score, the system needs a defined behavior.

Options include showing all tied providers, using lowest price as tie-breaker, or requiring manual selection.

### 28.4 Canonical Label: SICOP or CCOP
The notes reference both SICOP and CCOP. The system must confirm the canonical label to use in UI, database fields, and documentation.

### 28.5 Regulatory Status Labels
The exact Spanish status values were captured from the client and should be preserved. Before implementation, confirm spelling and casing with the client or source system.

### 28.6 One-Month Freshness Calculation
Define whether “one month” means:

- 30 calendar days.
- Same day in the next calendar month.
- Configurable number of days.

### 28.7 Which Regulatory Fields Block Progress
The client explicitly mentioned separate timestamps and blocking for Caja/CCSS and CCOP/SICOP. Hacienda should also be tracked because it is part of compliance and API-based validation. Confirm whether Hacienda staleness should block when the API job fails or has not run successfully.

### 28.8 Notification Channels
Define whether auditor notifications are sent by:

- In-app notification.
- Email.
- Both.
- Another configured channel.

### 28.9 Checklist Scope
Define whether reviewer and auditor use:

- The same checklist template.
- Separate templates.
- A shared template with role-specific sections.

---


### 28.10 Which Application States Count Toward User Funding Limits

Define which applications are included in the sum of requested amounts for the user/process maximum.

Potential options:

- Drafts only after they reach a certain completeness level.
- Submitted applications.
- Applications in review.
- Applications in audit.
- Approved applications.
- Signed applications.
- Disbursed applications.
- Rejected or withdrawn applications excluded.

This decision affects when the system blocks new submissions or warns administrators.

### 28.11 Reception Window Inclusivity Rules

Define whether reception window start and end timestamps are inclusive or exclusive.

Examples:

- Is submission allowed exactly at the start timestamp?
- Is submission allowed exactly at the end timestamp?
- What happens during the final minute/second of a window?

The recommended implementation is:

- Start is inclusive.
- End is exclusive.

Final confirmation is required before coding.

### 28.12 Time Zone Strategy

Define the authoritative timezone for process dates, reception windows, countdowns, and blocking rules.

Required decision:

- Store timestamps in UTC and display in configured business timezone.
- Define the business timezone used for Costa Rica processes or per-fund configuration.
- Ensure countdowns and submit-button rules use the same timezone logic.

### 28.13 Supplier Disqualification Rules

Define whether any provider compliance status should fully disqualify a provider, or whether all statuses only affect scoring.

The current captured requirement says compliance statuses affect scoring. It does not define automatic disqualification.

Potential decision:

- No compliance status disqualifies automatically; only scoring and warnings apply.
- Some statuses block recommendation but allow manual override.
- Some statuses fully block supplier usage.

### 28.14 Warning Governance

Define whether reviewers may create provider warnings or whether only auditors can create them.

Minimum requirement:

- Auditors can create/update warnings.
- Reviewers can see warnings.

## 29. Implementation Notes for AI Coding Agent

1. Do not remove requirements because they appear operational or minor. The client explicitly stated that these details are important.
2. Treat provider regulatory compliance as a first-class module.
3. Do not keep electronic invoice as a visible provider checkbox.
4. Do not move provider regulatory compliance values into quotation/item lines.
5. Delivery lead time, warranty, and price are quotation-specific.
6. Hacienda, CCSS/Caja, SICOP/CCOP, and PME/PYME are provider-specific.
7. The recommendation algorithm must be explainable.
8. The auditor role affects both provider management and the application workflow.
9. The reviewer no longer sends directly to agreement generation.
10. The auditor must confirm PDF correctness before the applicant receives the signature request.
11. Regulatory review freshness must be visible and enforceable.
12. A stale regulatory review older than one month must block progress until auditor action.
13. The system must support “reviewed but unchanged” for regulatory values.
14. New provider creation must notify auditors.
15. Every regulatory change or automated update must be auditable.


16. Treat process calendar events as explicit configuration because they drive countdowns, visual messages, and, when applicable, submission availability.
17. Remove electronic invoice validation completely from visible UI and active business logic, not only from one checkbox.
18. Do not implement a separate standalone recommendation base score; each criterion already gives a base point.
19. Preserve Spanish regulatory status labels as source-of-truth values unless the business confirms translated enum labels.
20. New provider notifications must fire regardless of provider creation source.
21. Timezone and reception-window inclusivity rules must be defined before coding strict submission enforcement.
22. Do not make supplier compliance statuses disqualifying unless the business explicitly defines disqualification rules.

---

## 30. Suggested Development Breakdown

### Phase 1 — Data Model and Provider Compliance Foundation
- Add Auditor role.
- Convert provider compliance checkboxes into status fields.
- Remove electronic invoice checkbox.
- Add PME/PYME flag.
- Add provider warning flag and note.
- Add provider regulatory audit history.
- Add last reviewed timestamps.

### Phase 2 — Recommendation Algorithm
- Add delivery lead time and warranty fields to item/provider quotations.
- Implement normalization for days/months.
- Implement scoring rules.
- Display recommendation explanation.
- Handle tie cases once business decision is made.

### Phase 3 — Auditor Workflow
- Add audit application state.
- Add auditor inbox/screen.
- Add checklist template and checklist completion flow.
- Add return-to-reviewer flow.
- Add auditor PDF generation/review/confirmation step.

### Phase 4 — Process Windows and Applicant Experience
- Replace or extend current process phase model with global process dates and multiple reception windows.
- Implement applicant countdown/visual notices.
- Enforce submit button availability based on reception windows.
- Improve disabled-action explanations.
- Improve applicant timeline and percentage progress.

### Phase 5 — Automation and Notifications
- Notify auditors when providers are created.
- Integrate Hacienda API.
- Schedule daily morning Hacienda synchronization.
- Add stale regulatory compliance blocking.
- Add optional proactive stale-review notifications.

### Phase 6 — UI Ordering and Supporting Fixes
- Move role to first field in user creation.
- Change item line field order to product first, then category.
- Add `Information` tooltip fields for templates.
- Add percentage type.
- Review password recovery email delivery.

---

## 31. Final Non-Omission Checklist

The following explicit client points are included in this document:

- Processes are defined by start/end dates and multiple reception windows.
- Applicants may have multiple submission opportunities within one process.
- Applicants should see professional countdown/visual notices.
- Submit button must be disabled outside reception windows and must explain why.
- Admin must assign a maximum amount per user/person per process.
- Multiple applications within a process must not exceed that maximum amount.
- Admin must be warned if lowering the amount puts applications at risk.
- Impact and category templates need an `Information` field shown as tooltip.
- Impact and category templates need a new `Percentage` type.
- Password recovery email must be reviewed because the client did not receive it.
- Item line creation must show product name before category.
- User creation must show role as first field.
- New Auditor role must exist.
- Auditor manages provider compliance.
- Electronic invoice checkbox must be removed from provider screen.
- Provider warning flag and free-text note must exist.
- Reviewer no longer goes directly to PDF/agreement generation.
- New auditor stage must exist in application workflow.
- Reviewer checklist must be completed before sending to audit.
- Auditor checklist must be completed before approving.
- Auditor returns non-compliant items to reviewer, not directly to applicant.
- Auditor generates and reviews PDF.
- Auditor confirms PDF is correct before applicant signature request.
- Applicant timeline must be improved and show percentage progress toward disbursement.
- Every new provider must notify the auditor group.
- Recommendation algorithm must include price, delivery lead time, warranty, Hacienda, CCSS/Caja, SICOP/CCOP, and PME/PYME.
- Delivery lead time must be numeric with days/months.
- Warranty must be numeric with days/months.
- Hacienda status values and scoring are captured.
- CCSS/Caja status values and scoring are captured.
- SICOP/CCOP status values and scoring are captured.
- PME/PYME checkbox scoring is captured.
- Lowest price receives 2 points, but price ties receive 1 point.
- Recommended provider is highest score, not necessarily lowest price.
- Regulatory values are provider-level, not quote-level.
- Delivery, warranty, and price are quote-level.
- Provider regulatory changes require audit history.
- Hacienda API sync must run daily in the morning for all providers.
- Automated changes must update provider values and audit history.
- Separate last reviewed fields are required for CCSS/Caja and SICOP/CCOP.
- Review screens must show when provider regulatory values were last reviewed.
- If review is older than one month, the request/application cannot advance.
- Auditor must be able to confirm reviewed-but-unchanged values and refresh timestamps.


- Process calendar events are included as explicit configuration.
- Reception windows are treated as special process events or equivalent first-class configuration.
- Electronic invoice validation is removed from screens, edit forms, validation logic, workflows, compliance sections, and checklists.
- New provider notifications apply regardless of creation source.
- Recommendation scoring does not include a separate standalone base score.
- Price tie conflict between source documents is resolved using the explicit client-stated rule: tied lowest prices receive 1 point.
- Open decisions include application states for funding-limit calculation, reception-window inclusivity, timezone strategy, and supplier disqualification rules.
