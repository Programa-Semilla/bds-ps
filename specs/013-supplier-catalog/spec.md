# Feature Specification: Centralized Supplier Catalog with Multi-Branch Support and Admin-Controlled Compliance

**Feature Branch**: `013-supplier-catalog`
**Created**: 2026-04-30
**Status**: Draft
**Input**: User description: "Centralized Supplier Database with Multi-Branch Support and Admin-Controlled Compliance"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Reuse a Verified Supplier with an Existing Branch (Priority: P1)

An applicant building a quotation enters a supplier's legal ID. The system finds the existing `Verified` supplier in the catalog, displays its name, electronic-invoice flag, and compliance status (all read-only — admin-controlled), and shows a list of branches/offices already on file. The applicant picks the branch whose contact and address match what their quotation document references and saves the quotation in seconds — no re-typing of supplier data, no compliance checkboxes to guess at.

**Why this priority**: This is the everyday happy path that delivers the headline value of the feature: structured reuse and applicant relief from compliance guesswork. Without P1, none of the other stories matter — every other story is a variant of this lookup-then-pick flow.

**Independent Test**: Seed a Verified supplier with two branches. As an applicant on a draft application, search by legal ID, pick the second branch, save quotation. Verify the quotation references that branch, no new supplier or branch row was created, and the applicant was never asked for compliance values.

**Acceptance Scenarios**:

1. **Given** a Verified supplier with legal ID `3-101-123456` and two branches exists, **When** the applicant searches by `3-101-123456` on the Add Quotation form, **Then** the supplier name, electronic-invoice flag, and the three compliance flags are shown read-only and both branches are listed in a picker.
2. **Given** the applicant has selected an existing branch and submitted the form, **When** the save completes, **Then** the new quotation is linked to that branch's identifier and no `Suppliers` or `SupplierBranches` row was created or modified.
3. **Given** the applicant types `  3-101-123456  ` (whitespace) or `3-101-123456` in mixed case, **When** the search runs, **Then** the same supplier is returned (lookup is case-insensitive and trimmed).

---

### User Story 2 - Add a New Branch under an Existing Supplier (Priority: P1)

An applicant looks up a supplier by legal ID, finds the supplier in the catalog, but none of the existing branches match the office their quotation came from. They click "Add new branch", fill in branch-specific fields (branch name, contact, email, phone, address, province, shipping/warranty notes), and save. The new branch is attached to the existing supplier and the quotation references it. The applicant never edits any field on the parent supplier itself.

**Why this priority**: P1 because the multi-office reality is the second-most-common applicant experience and is the reason a single flat row per legal ID was insufficient. Branches must be addable without admin involvement; otherwise applicants are blocked waiting on admin every time a new office appears.

**Independent Test**: Seed a Verified supplier with one branch. As an applicant on a draft application, search by legal ID, click "Add new branch", fill in fields, save quotation. Verify a new branch row exists under that supplier, the quotation links to the new branch, and the supplier itself is unchanged.

**Acceptance Scenarios**:

1. **Given** a Verified supplier with one default branch exists, **When** the applicant chooses "Add new branch" and submits valid branch data, **Then** a new branch is persisted under that supplier with `IsDefault = false` and `CreatedByApplicantId` set to the current applicant.
2. **Given** the applicant has just added a new branch, **When** the quotation is saved, **Then** the quotation references the newly created branch.
3. **Given** the applicant attempts to edit the parent supplier's name or compliance flags from the branch form, **When** they submit, **Then** the changes are silently ignored (those fields are not part of the editable surface) and only the branch is written.

---

### User Story 3 - Create a Brand-New Supplier in Draft (Priority: P1)

An applicant looks up a legal ID that the catalog has never seen. The system shows a new-supplier form with no compliance checkboxes and no electronic-invoice flag (those are admin-set fields). The applicant fills in the supplier identity (legal ID, name) and the first branch's contact details, and saves. The supplier is created in `Draft` status, owned by the applicant, and is invisible to anyone else (other applicants, admins, reviewers) until the parent application is submitted.

**Why this priority**: P1 because applicants must be able to keep working when their supplier is not yet in the catalog — blocking the applicant on admin response would defeat the purpose. The Draft state is what makes that possible without contaminating the shared catalog.

**Independent Test**: As an applicant on a draft application, search a legal ID that does not exist, fill in supplier + first-branch fields, save. Verify a new `Suppliers` row exists with `VerificationStatus = Draft` and `CreatedByApplicantId` set, exactly one branch with `IsDefault = true`, and that a second applicant searching the same legal ID does not see the Draft supplier.

**Acceptance Scenarios**:

1. **Given** no supplier with legal ID `3-101-999999` exists, **When** the applicant submits the new-supplier form, **Then** a `Suppliers` row is created with `VerificationStatus = Draft`, all three compliance flags `false`, and `CreatedByApplicantId` set to the submitter.
2. **Given** the new supplier was created in step 1, **When** a different applicant searches `3-101-999999`, **Then** the system reports "no supplier found" and offers the new-supplier form.
3. **Given** the parent application is still in `Draft` state, **When** the owning applicant returns to edit the supplier or its first branch, **Then** all fields are editable.

---

### User Story 4 - Application Submission Locks Draft Suppliers and Routes to Admin (Priority: P1)

An applicant submits an application that includes one or more Draft suppliers. The system, atomically with the submission, flips every owned Draft supplier on the application to `PendingReview`, revokes the applicant's edit access, and surfaces the supplier in the admin verification queue. The reviewer can begin scoring the application immediately; pending suppliers contribute zero compliance points to the recommendation algorithm and display a "Pending verification" badge in the reviewer UI.

**Why this priority**: P1 because this is the lifecycle promise — Draft is provisional until submission, then admin owns the record. Without it, drafts pollute the shared catalog or applicants block the admin queue waiting to submit.

**Independent Test**: Seed an application in Draft state with one Draft supplier the applicant created. Submit the application. Verify the supplier's status flipped to `PendingReview`, the applicant cannot edit it on the application detail page, and the supplier appears in the admin Suppliers queue filtered to PendingReview.

**Acceptance Scenarios**:

1. **Given** an applicant's draft application contains one Draft supplier they created, **When** they submit the application, **Then** the supplier's `VerificationStatus` is `PendingReview` after the submission transaction commits.
2. **Given** the supplier is now `PendingReview`, **When** the same applicant tries to edit it, **Then** the action is rejected and the UI shows the supplier as read-only.
3. **Given** the supplier is `PendingReview`, **When** an admin opens the Suppliers admin page with the default filter, **Then** the supplier appears in the list.
4. **Given** the supplier is `PendingReview`, **When** the reviewer opens the application's review screen, **Then** each quotation linked to that supplier shows a "Pending verification" badge and contributes zero compliance points to its `SupplierScore` total.

---

### User Story 5 - Admin Verifies, Edits, or Rejects a Pending Supplier (Priority: P1)

An admin opens the Suppliers admin page (default filter: `PendingReview`), drills into a supplier, reviews the applicant-submitted data and any branches, optionally edits any field including the three compliance flags, and clicks **Verify** (status → `Verified`, verifier and timestamp recorded) or **Reject** (status → `Rejected`, requires a written reason). The decision takes effect immediately. Verified suppliers become reusable for future applicants and start contributing compliance points to recommendations.

**Why this priority**: P1 because admin verification is what moves a Draft from "trapped on one application" to "shared catalog asset". Without it, every supplier the system sees is provisional forever.

**Independent Test**: As an admin, open a supplier in `PendingReview`, toggle the three compliance flags, click Verify. Verify the status becomes `Verified`, the verifier's user ID and a timestamp are persisted, and a different applicant on a fresh draft application searching that legal ID is now allowed to reuse it.

**Acceptance Scenarios**:

1. **Given** a `PendingReview` supplier, **When** the admin edits the supplier name and clicks Verify, **Then** the change is persisted, status becomes `Verified`, `VerifiedByUserId` is the current admin, and `VerifiedAt` is the current timestamp.
2. **Given** a `PendingReview` supplier, **When** the admin clicks Reject without entering a reason, **Then** the action is blocked and a validation error is shown.
3. **Given** a `PendingReview` supplier, **When** the admin clicks Reject with a reason, **Then** status becomes `Rejected`, `RejectionReason` is stored, and applications referencing the supplier display a reviewer banner stating the supplier was rejected.
4. **Given** a `Verified` supplier, **When** an admin edits any field (e.g., toggles a compliance flag), **Then** the change is persisted immediately and is reflected on the next render of any application's review screen referencing that supplier.

---

### User Story 6 - Admin Edits a Verified Supplier on Applicant's Behalf (Priority: P2)

An applicant contacts the admin out-of-band reporting that a piece of supplier data is wrong (e.g., a typo'd email on a branch, a missing address line). The admin opens the supplier in the admin area, edits the offending field on the supplier or branch, and saves. The corrected data is immediately visible to all applicants and reviewers referencing that supplier; no re-verification is required.

**Why this priority**: P2 because corrections are common but not blocking — they happen via an out-of-band channel and on already-verified data, which the system already trusts. Worth shipping but not gating the main flow.

**Independent Test**: Seed a Verified supplier with a typo in a branch email. As an admin, edit the email and save. Verify the corrected email shows on the applicant's quotation detail and on a fresh review-screen render.

**Acceptance Scenarios**:

1. **Given** a `Verified` supplier and one of its branches with a typo in `Email`, **When** an admin updates the email and saves, **Then** the new value is persisted and visible on subsequent renders of any view that displays that branch.

---

### User Story 7 - Admin Sees Filterable Queue of Suppliers Needing Attention (Priority: P2)

The admin lands on the Suppliers admin page and sees a queue defaulting to `PendingReview`. They can switch filters to view `Verified`, `Rejected`, search by legal ID, search by name, or filter to suppliers with at least one compliance flag still false. From the list they can click into any supplier for the detail view used in P1.

**Why this priority**: P2 because the workflow works without the filters (admin can scroll), but at any non-trivial scale of pending records, filters are how an admin actually keeps up.

**Independent Test**: Seed three suppliers (one Pending, one Verified, one Rejected). Open the admin page. Verify the default view shows only the Pending one. Switch the filter to Verified, confirm the list updates. Search by partial legal ID, confirm matching results.

**Acceptance Scenarios**:

1. **Given** suppliers in three different statuses exist, **When** the admin opens the Suppliers page, **Then** only suppliers in `PendingReview` are listed by default.
2. **Given** the admin switches the status filter to `Verified`, **When** the list reloads, **Then** only `Verified` suppliers are shown.
3. **Given** the admin enters a partial legal ID in the search box, **When** the search executes, **Then** results match suppliers whose legal ID contains that substring (case-insensitive).

---

### Edge Cases

- **Concurrent creation of the same legal ID**: Two applicants type the same brand-new legal ID at the same instant. The unique constraint on `Suppliers.LegalId` serializes the writes; the second submission fails on the constraint, the UI re-runs the lookup, finds the now-existing supplier, and presents the existing-supplier flow (branch picker / add new branch).
- **Abandoned drafts**: An applicant creates a Draft supplier, never submits the application, and walks away. The supplier remains in `Draft` indefinitely, invisible to others. If the application is later deleted, the Draft supplier is cascade-deleted along with it (assumption: a Draft supplier with no other quotation reference is safe to remove).
- **Whitespace and case in legal IDs**: Inputs are trimmed and uppercased on lookup and on persistence. `  3-101-123456  ` and `3-101-123456` map to the same supplier.
- **Quotation against a Rejected supplier**: Blocked at the controller layer with a localized error directing the applicant to contact admin. The Rejected supplier does not appear in lookup results for new quotation use.
- **Admin rejects mid-review**: An admin rejects a supplier on a submitted application that the reviewer has already partially scored. A banner appears on the next review-screen render; the reviewer is not auto-redirected and can choose to keep their existing pick or change it.
- **Single-branch suppliers**: When a supplier has exactly one branch, the branch picker UI collapses to "Use Sede principal" with an "Add new branch" link, avoiding a single-radio-button choice.
- **Reviewer cannot edit suppliers or branches**: The reviewer role retains read-only access only; any attempted edit endpoint must reject the role.
- **Admin re-verifies a previously Rejected supplier**: Status transition `Rejected → Verified` is allowed; the rejection reason is cleared and verifier/timestamp are updated.

## Requirements *(mandatory)*

### Functional Requirements

#### Supplier identification

- **FR-001**: System MUST normalize legal IDs (trim whitespace, uppercase) on every lookup and on persistence so identifiers are matched canonically.
- **FR-002**: System MUST expose a supplier-search-by-legal-ID lookup to applicants. The lookup MUST return supplier metadata plus all branches when a `Verified` supplier with that legal ID exists (visible to every applicant), AND when a `PendingReview` supplier with that legal ID exists AND was created by the searching applicant (visible only to its creator).
- **FR-003**: System MUST return the applicant's own `Draft` supplier (created by the same applicant on a draft application) as a hit when they search its legal ID. Otherwise both `Draft` AND `PendingReview` suppliers created by other applicants MUST be invisible to lookup. Until an admin promotes a supplier to `Verified`, the supplier is not reusable across applicants.
- **FR-004**: System MUST treat `Rejected` suppliers as not-found for applicants creating new quotations and MUST display a localized "contact admin" message inline.
- **FR-005**: System MUST handle case-insensitive comparisons and whitespace trimming on the legal ID field so equivalent inputs map to the same supplier.

#### Existing-supplier flow

- **FR-010**: System MUST render the supplier name, electronic-invoice flag, and compliance flags as read-only when the applicant lands on an existing `Verified` supplier (or, for the supplier's creator, an existing `PendingReview` supplier per FR-002), and MUST display a branch picker plus an "Add new branch" affordance.
- **FR-011**: System MUST allow the applicant to select an existing branch and link the new quotation to it without writing to `Suppliers` or `SupplierBranches`.
- **FR-012**: System MUST allow the applicant to create a new branch under an existing supplier; the new branch MUST inherit only its parent's `SupplierId` (branches do not carry their own verification status).
- **FR-013**: System MUST forbid applicants from editing any field on a supplier whose `VerificationStatus` is `Verified`, `PendingReview`, or `Rejected`. Edits to `Draft` suppliers are allowed only for the supplier's creator and only while the parent application is in `Draft`.
- **FR-014**: System MUST allow applicants to edit branches they originally created only while the parent application is in `Draft`.

#### New-supplier flow

- **FR-020**: System MUST present a new-supplier form (legal ID, name, plus first-branch fields) when the legal-ID lookup returns nothing. The form MUST NOT show compliance checkboxes and MUST NOT show the electronic-invoice flag (admin-verified per FR-040).
- **FR-021**: System MUST create the new supplier with `VerificationStatus = Draft`, `CreatedByApplicantId = current applicant`, `HasElectronicInvoice = false`, all three compliance flags `false`, and exactly one default branch (`IsDefault = true`).
- **FR-022**: System MUST allow the owning applicant to edit the Draft supplier name and any of its branches while the parent application is `Draft`. The applicant MUST NOT edit the electronic-invoice flag or any compliance flag (those are admin-only at any status).
- **FR-023**: System MUST hide `Draft` suppliers from all users except the supplier's creator (no admin queue surfacing, no other-applicant lookup hit).
- **FR-024**: System MUST flip every Draft supplier referenced (transitively via quotations) by an application to `PendingReview` atomically with `Application.Submit`, revoking applicant edit access in the same transaction.
- **FR-025**: System MUST keep `PendingReview` suppliers in `PendingReview` if their parent application is sent back to draft by the reviewer (admin retains control of the lifecycle).

#### Admin control

- **FR-030**: System MUST provide a Suppliers admin page that defaults to filtering on `VerificationStatus = PendingReview`.
- **FR-031**: System MUST allow admins to filter the Suppliers list by status, partial legal ID, partial name, and "has at least one false compliance flag".
- **FR-032**: System MUST display, on each supplier's admin detail view, the supplier identity, all branches, and the application(s) currently referencing the supplier.
- **FR-033**: System MUST allow admins to edit any field on a supplier (legal ID, name, electronic-invoice flag, all three compliance flags) and persist changes immediately.
- **FR-034**: System MUST allow admins to edit any field on any branch and persist changes immediately.
- **FR-035**: System MUST support these admin status transitions: `PendingReview → Verified` (records `VerifiedByUserId` + `VerifiedAt`), `PendingReview → Rejected` (requires non-empty `RejectionReason`), `Verified → Rejected` (requires reason), `Rejected → Verified` (clears reason, records verifier + timestamp).
- **FR-036**: System MUST forbid admins from deleting suppliers or branches that are referenced by any quotation.
- **FR-037**: System MUST forbid direct admin creation of suppliers in v1; admins can only act on applicant-initiated suppliers.

#### Compliance and lifecycle

- **FR-040**: System MUST store the three compliance flags (CCSS, Hacienda, SICOP) AND the electronic-invoice flag (`HasElectronicInvoice`) on the supplier identity (not on the quotation), and MUST NOT expose any of these four flags on any applicant-facing form. Only admins may toggle them.
- **FR-041**: System MUST compute `SupplierScore` (the recommendation score introduced by the supplier evaluation engine feature) by reading `IsCompliantCCSS`, `IsCompliantHacienda`, `IsCompliantSICOP`, and `HasElectronicInvoice` from the supplier identity. The scoring math (one point each for the four compliance/e-invoice factors plus one for lowest price; max five) MUST remain unchanged.
- **FR-042**: System MUST extend the `SupplierScore` result with two read-only flags exposing the supplier's verification state: "verified" and "rejected".
- **FR-043**: System MUST set the "Recommended" badge only on quotations whose supplier is not `Rejected`. Pre-selection rules from the supplier evaluation engine spec are otherwise unchanged.

#### Submission, blocking, and reviewer signals

- **FR-050**: System MUST allow an application to be submitted even when every supplier on it is `Draft` or `PendingReview`. Submission MUST NOT block on admin verification.
- **FR-051**: System MUST display a "Pending verification" badge on every reviewer-facing quotation row whose supplier is in `PendingReview`, and pending suppliers MUST contribute zero points to the four compliance/e-invoice factors of `SupplierScore`.
- **FR-052**: System MUST display a banner on the reviewer's application detail/review screen when at least one quotation on the application references a `Rejected` supplier, indicating the count of such quotations.
- **FR-053**: System MUST block creation of any new quotation that would reference a `Rejected` supplier at both the controller and UI layers.

#### Migration of existing data

- **FR-060**: System MUST migrate every existing `Suppliers` row in a single forward-only transaction with these properties: `VerificationStatus = Verified`, `VerifiedByUserId = system admin sentinel`, `VerifiedAt = migration timestamp`, `CreatedByApplicantId = NULL`, and the existing compliance flags preserved as-is.
- **FR-061**: System MUST create exactly one default branch per migrated supplier carrying its prior `ContactName / Email / Phone / Location → AddressLine / ShippingDetails / WarrantyInfo` values, with `BranchName = "Sede principal"` and `IsDefault = true`.
- **FR-062**: System MUST repoint every existing quotation to the migrated default branch and MUST assert that every quotation has a non-null `SupplierBranchId` and that the quotation's denormalized `SupplierId` equals its branch's `SupplierId`. The migration MUST abort on any assertion failure.
- **FR-063**: System MUST require the existence of a system-admin sentinel user (introduced by the admin-area feature) and MUST fail the migration loudly if it is absent.

#### Permissions

- **FR-070**: System MUST enforce the following permission matrix at the controller layer:

  | Actor | Verified supplier | Owned Draft supplier (draft app) | Owned PendingReview / Rejected supplier | Other applicant's PendingReview / Rejected supplier | Branch on a supplier the actor can see |
  |---|---|---|---|---|---|
  | Applicant (creator of supplier) | read | read + edit | read | n/a | read; edit only on own-created branches while parent app is Draft |
  | Applicant (other) | read (via lookup) | not visible | n/a | not visible (lookup miss; Rejected returns localized "contact admin" error) | read on visible suppliers only |
  | Admin | read + edit | read + edit (rare; only via direct nav) | read + edit | read + edit | read + edit |
  | Reviewer | read | read (via the application they are reviewing) | read (via the application they are reviewing) | read (via the application they are reviewing) | read |

### Key Entities

- **Supplier**: A canonical legal entity identified by a unique legal ID. Owns a name, an electronic-invoice flag, the three compliance booleans, a verification lifecycle (`Draft`, `PendingReview`, `Verified`, `Rejected`), and references to the applicant who created it (if applicant-initiated) and the admin who last verified it.
- **SupplierBranch**: An office, location, or contact under a supplier. Carries branch-specific contact data (name, email, phone, address, province), shipping/warranty notes, and a `IsDefault` flag (exactly one default per supplier). Branches do not have their own verification status; they inherit the parent supplier's status implicitly.
- **Quotation (modified)**: References a `SupplierBranch` directly via a foreign key, in addition to keeping a denormalized reference to the parent supplier for query convenience. Compliance booleans are NOT stored on the quotation; they live only on the supplier.
- **Application (existing)**: Lifecycle interaction unchanged except for the `Submit` action, which now also flips owned Draft suppliers to PendingReview atomically.
- **AdminUser (existing)**: Now consumes the supplier verification queue; must include a system-admin sentinel reused from the admin-area feature for migration provenance.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Applicants creating a new quotation against an already-cataloged supplier complete the supplier portion of the form in fewer keystrokes than today (target: at least 70% reduction in fields they have to type) because supplier identity, contact for an existing branch, and compliance are all pre-filled or admin-owned.
- **SC-002**: 100% of applicant-facing forms have zero compliance checkboxes after this feature ships. Compliance values are set exclusively by admins.
- **SC-003**: Existing applications and their `Recommended` / `PreSelected` quotations show identical results after the migration as before, byte-for-byte, on a representative production-like dataset.
- **SC-004**: A new supplier flows from applicant creation to admin verification within the existing admin's working day in 90% of cases (admin queue is filterable and discoverable enough that pending records do not pile up beyond one business day on average — measured in production via queue age).
- **SC-005**: Reviewer recommendation logic produces identical scoring totals to the current implementation for verified suppliers; pending and rejected suppliers receive an identifiable visual treatment so reviewers are not silently misled by a zero-compliance score.
- **SC-006**: The migration runs to completion in a single transaction in under 60 seconds against the production database (small dataset; SQL Server, single-node).

## Assumptions

- The system-admin sentinel user introduced by the admin-area feature is present in production at migration time.
- A `Supplier` aggregate is the appropriate domain boundary for both identity and lifecycle methods (`SubmitForReview`, `Verify`, `Reject`, plus branch CRUD).
- An applicant-initiated `Draft` supplier with no remaining quotation references can be cascade-deleted along with its parent application; nothing else references it.
- A reviewer is allowed to choose a `Rejected` supplier as the chosen quotation, but the supplier cannot bear the "Recommended" badge regardless of score.
- Province is a free-text string in v1. Promotion to an enum (CR's seven provinces) can come later without breaking changes if the column is normalized at write time.
- An audit log table for supplier edits is out of scope for v1; existing EF Core change tracking and infrastructure logging are sufficient.
- Notifications to applicants when a draft supplier is verified or rejected are out of scope for v1 (potentially a follow-up "wow moment" item).
- Direct admin creation of suppliers (without an applicant initiator) is out of scope for v1.
- External CCSS / Hacienda / SICOP integrations are out of scope; compliance is set by admin toggle only.
- Bulk supplier import (CSV/Excel) is out of scope.
- Supplier-side login/portal is out of scope; suppliers do not authenticate.
- Branch soft-delete UI is out of scope; admins cannot delete branches that are referenced by quotations.
