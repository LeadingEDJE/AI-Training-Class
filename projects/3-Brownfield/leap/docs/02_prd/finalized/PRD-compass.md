# PRD: TPS Rewrite (EDJE Compass)

> **Provenance.** This document is the EDJE Compass PRD (v5), captured verbatim from the
> intake lifecycle dashboard on 2026-07-31. It is the authoritative *product* requirements
> for Compass. It was written against the legacy TPS stack and predates knowledge of this
> repository's current state, so the platform-level *technical* reconciliations below take
> precedence over conflicting stack statements in the body.
>
> **Reconciliations (authoritative for LEAP — see the ADRs, do not edit the body):**
> - **Database — MySQL 8.0 → PostgreSQL 16.** AC-NFR-6 and AC-41 say MySQL; LEAP standardizes
>   on the PostgreSQL engine already running. See
>   [ADR-003](../../adr/ADR-003-postgresql-over-prd-mysql.md).
> - **Deployment shape — "separate solution" → module in the LEAP monolith.** Compass is a
>   module in the modular monolith with a published boundary and standalone auth (satisfying
>   BR-13), not a separate deployable. See
>   [ADR-002](../../adr/ADR-002-leap-modular-monolith-compass-core.md) and
>   [ADR-006](../../adr/ADR-006-timesheet-peer-module-repo-reshape.md).
> - **Integration boundary (AC-NFR-1).** Realized as an in-process `IDirectory` interface plus
>   a versioned HTTP `/api/compass/v1`, same DTOs. See
>   [ADR-004](../../adr/ADR-004-directory-integration-boundary.md).
> - **External-consumer auth (AC-NFR-1/AC-NFR-2).** Machine-to-machine access uses OAuth2
>   client-credentials; interactive users continue to use the existing Google SAML + cookie
>   session. See [ADR-005](../../adr/ADR-005-oauth2-client-credentials-external-consumers.md).
> - **Retention (AC-NFR-3/BR-16).** Audit purges at 2 years; business records never purge. See
>   [ADR-007](../../adr/ADR-007-audit-retention-asymmetry.md).
>
> Testable non-functional targets derived from this PRD live in the
> [NFR catalog](../../nfr/NFR-catalog.md). Platform shape lives in
> [TARGET-STATE.md](../../architecture/TARGET-STATE.md).

---

Grounding: Written against the Scope and Journey Map artifacts, the BA decisions of Jul 30 2026, the BA-supplied source PRD draft, ERD, and mockups, the BA open-question resolutions of Jul 31 2026, and the findings of Review v1 (Jul 31 2026).

Status: no open questions remain. All ten questions from v1, three from v2, and four carried from Review v1 are resolved and folded into the criteria below. Eight review findings require revisions to Scope and Journey Map rather than to this document — see Cross-Artifact Dependencies.

## What changed in v5
Closes the four open questions carried from Review v1.

AC-43 — coach emails are personalized. Bodies now name the EDJEr instead of opening with the literal word "EDJEr," so a coach who manages several people can identify the subject from the body alone. This matters because the emails deliberately carry no link back into Compass.
AC-NFR-4 — performance target made measurable. The 1-second target is now a p95 server response time, verifiable independent of client device, browser, and network. That keeps it meaningful alongside AC-NFR-7's mobile requirement, where full-render timings vary with cellular conditions outside Compass's control.
AC-4 — cross-application launcher removed. Compass provides no navigation to Timesheet or OOTO. Users reach internal systems through LEAP or by direct system URL. Recorded as BR-15, and explicitly separated from the data integration boundary in AC-NFR-1.
AC-NFR-3 — retention scope confirmed. The 2-year period governs the audit trail only; assignment, SOW, client, and EDJEr business records are retained indefinitely and never purged. Recorded as BR-16.

## What changed in v4
Responded to Review v1's blocking findings.

B1 resolved — assignment eligibility decoupled from derived client status (AC-27, AC-42, AC-21, BR-11).
B4 resolved — migration validation posture defined as grandfathering (AC-41, AC-33, BR-14).
S5 resolved — Compass Admin read-only made explicit (AC-44).

## Summary
EDJE Compass replaces the legacy Team Profile System as Leading EDJE's production-hardened system of record for EDJErs, clients, employee/client assignments, and SOWs [Scope]. The team is approximately 90 EDJErs: roughly 80% are deployed on client assignments, and the remainder work in internal, non-billable functions — sales, recruiting, admin, ops, finance, and marketing [source PRD draft §1; v1 OQ-10 resolution]. Compass exists to keep Sales and Ops ahead of SOW renewals, rollouts, and bench availability, and to feed adjacent systems (Time Tracking, OOTO), which are out of scope to build [Scope]. Access is role-isolated, driven by the group membership delivered in the Google SAML authentication response. HiBob remains the HRIS of record; Compass holds only basic operational profile data [Scope].

Platform context — LEAP. LEAP (Leading EDJE Application Platform) is a separate solution that acts as the launch point for Leading EDJE's internal solutions, providing single sign-on into EDJE Compass, EDJE Timesheet, and EDJE OOTO. LEAP and Compass are built in parallel, and Compass may be ready first. Compass must therefore behave as a fully standalone solution — directly reachable, independently authenticated — as well as a LEAP-launched one. No Compass acceptance criterion depends on LEAP being live. Cross-application navigation belongs to LEAP alone: Compass does not link out to Timesheet or OOTO, and users reach those systems through LEAP or by direct URL [BA decisions Jul 31 2026].

Identity. Authentication resolves against Google SAML, whose response includes all groups the authenticated person belongs to. That assertion is passed through to whichever SSO layer initiated the flow — LEAP in the LEAP-launched path, Compass itself in the standalone path — so Compass receives the same group claims either way [BA decision Jul 31 2026].

Delivery: Full — all acceptance criteria below ship in a single release [Scope; BA].

Actors: Compass Super Admin, Compass Admin, Compass Ops, Compass Sales, EDJEr (regular), Coach (notified), Migration engineer, Migration reconciler (Ops) [Journey Map; BA decision Jul 31 2026].

## Acceptance Criteria

### Authentication & Roles
AC-1 — Authentication via LEAP or standalone, with direct-link support

Given an EDJEr with a Leading EDJE Google account
When they reach Compass either by launching it from LEAP or by following a direct link or bookmark to a Compass URL
Then they are authenticated in both cases: an existing LEAP single-sign-on session carries them straight in, and a direct arrival without a session is authenticated by Compass in its own right and then returned to the originally requested Compass location rather than a generic landing page. Compass must function as a standalone solution when LEAP is not present or not yet deployed. Direct links must never bypass authentication; unauthenticated users reach no Compass area. (source: JM J1; BA decisions Jul 31 2026 — resolves v2 OQ-2)

AC-2 — Role resolution from SAML-delivered group membership

Given an EDJEr authenticating through Google SAML
When the authentication response is received and the session is established
Then the response carries every group the person is a member of; Compass derives the user's roles from the four Compass groups present in those claims (compass_super_admin, compass_admin, compass_ops, compass_sales), ignores all non-Compass groups in the assertion, and treats the base EDJEr role as implicit for any authenticated user. Role membership is never stored in Compass, and role resolution behaves identically whether the flow was initiated by LEAP or by Compass directly. (source: Scope; JM J1; BA decision Jul 31 2026 — resolves v2 OQ-1)

AC-3 — Multiple roles union

Given an EDJEr who belongs to more than one Compass group
When their permissions are evaluated
Then their effective access is the union of all held roles. (source: BA decision)

AC-4 — Role-gated navigation; no cross-application launcher (revised in v5)

Given an authenticated EDJEr on the landing page
When navigation is rendered
Then only the Compass areas their roles permit are shown (administration links, Ops assignment functions, Sales dashboard/reports appear only for the roles that grant them); a user with no elevated role sees the directories only.
Compass navigation is limited to Compass functionality. Compass provides no cross-application launcher and no navigation to EDJE Timesheet or EDJE OOTO. Users reach other internal systems through LEAP or by direct system URL. (source: JM J1; BA decision Jul 31 2026 — resolves Review v1 finding S8)

AC-44 — Compass Admin is read-only

Given a user whose highest privilege is Compass Admin
When they access any Compass area
Then they may read all EDJEr records including inactive ones, and they are granted no configuration rights: EDJEr configuration, client configuration, lookup administration, assignment management, and SOW management are unavailable to them, both as navigation and as write operations. Read-only status applies regardless of entry point and is enforced server-side, not merely by hiding controls. (source: Scope v3; JM v2 actor table; Review v1 finding S5)

### Team Directory
AC-5 — Team directory listing

Given any authenticated EDJEr
When they open the Team Directory
Then they see active EDJErs, ordered by hire date by default, showing first name, last name, hire date, email, employee type, coach, state, and current client assignment(s); all current assignments display when an EDJEr has more than one. (source: Scope; JM J2)

AC-6 — Directory search, filter, sort

Given the Team Directory
When the EDJEr searches by last name (partial, case-insensitive), filters by employee type and/or state, or selects a column header
Then the list narrows to matches and sorts by the chosen column. (source: JM J2; BA decision)

AC-7 — Directory navigation

Given a directory row
When the EDJEr selects the email link or a client-assignment link
Then the email opens the read-only employee detail and the client link opens the read-only client view, each subject to the viewer's role. (source: JM J2, J3, J5)

### Read-only Employee Detail
AC-8 — Read-only employee detail

Given a non-Super-Admin EDJEr who opens an employee detail
When the page renders
Then it displays the EDJEr's profile and assignment history with no edit controls; a client in the assignment history links to that client's view. (source: JM J3)

AC-9 — Active/inactive employee visibility

Given an employee detail or any EDJEr listing
When it is rendered
Then regular EDJErs see active EDJErs only and never see former (inactive) EDJErs; all elevated roles (Super Admin, Compass Admin, Sales, Ops) additionally see inactive EDJErs. (source: Scope; JM J3, J5; BA decision)

AC-10 — Time Tracking Settings visibility

Given an employee detail
When it is rendered
Then the Time Tracking Settings section is visible only to Compass Super Admin. (source: JM J3; BA decision)

AC-11 — SOW, notes, and rate-increase visibility

Given an EDJEr viewing assignment/SOW information
When the detail renders
Then a regular EDJEr sees SOWs only on their own record (never another EDJEr's) and does not see the rate-increase indicator, SOW notes, or assignment notes; all elevated roles see all SOWs, the rate-increase indicator, and all notes. (source: JM J3; BA decision)

### Client Directory & Client View
AC-12 — Client directory listing

Given any authenticated EDJEr
When they open the Client Directory
Then they see a table of all clients, searchable by client name (partial, case-insensitive) and sortable by column, with each client linking to its client view. (source: Scope; JM J4; BA decision)

AC-13 — Client view name prominence

Given a client view
When it renders (including when the Client Details panel is hidden for the viewer's role)
Then the client name is displayed prominently at the top. (source: JM J5; BA decision)

AC-14 — Client view role scoping

Given a client view
When it renders
Then non-Super-Admins (EDJEr, Ops, Sales, Compass Admin) see only the EDJEr Assignment History panel (Client Details and Time Tracking Settings panels hidden); Compass Super Admin sees the full client configuration with edit controls. (source: JM J5; BA decision)

AC-15 — Client assignment-history visibility

Given the EDJEr Assignment History on a client view
When it renders
Then regular EDJErs see active EDJErs only; all elevated roles also see inactive EDJErs; each row shows the assignment start/end date and EDJEr status. (source: Scope; JM J5; BA decision)

AC-16 — View-SOW access in history

Given the assignment-history panel
When it renders
Then the View-SOW affordance is shown only to elevated roles (Super Admin, Compass Admin, Sales, Ops). (source: JM J5; BA decision)

### EDJEr Configuration
AC-17 — Add an EDJEr

Given a Compass Super Admin
When they add an EDJEr
Then the record captures first name, last name, hire date, unique email, employee type (active types only), coach (optional), state of residence (US states only), active status, and the time-tracking flags (timesheet required, can submit under 40, include in payroll); on save the EDJEr becomes selectable across Compass. (source: Scope; JM J6; PRD draft EC-1/EC-2; BA decision)

AC-18 — Unique email

Given an EDJEr being added or updated
When the email matches any existing EDJEr (active or inactive)
Then the system rejects the save and the record is not created/updated. (source: JM J6; BA decision)

AC-19 — Update an EDJEr; deactivation requires all assignments end-dated

Given a Compass Super Admin editing an EDJEr
When they change profile fields or time-tracking flags
Then the changes are saved.
Given a Compass Super Admin attempting to toggle the active flag off
When the EDJEr still holds any assignment without an end date
Then the system rejects the deactivation and identifies the assignments that must be end-dated first; the EDJEr remains active. The system does not auto-end assignments, because an assignment's true end date frequently differs from the date the EDJEr is deactivated and must not be defaulted.
Given an EDJEr whose assignments are all end-dated
When the Super Admin deactivates them
Then the deactivation succeeds and the EDJEr is hidden from regular EDJErs while remaining visible to all elevated roles. (source: Scope; JM J7; BA decision Jul 31 2026 — resolves v1 OQ-1)

AC-20 — EDJEr assignment history

Given a Compass Super Admin on an EDJEr record
When the record renders
Then every client the EDJEr has been assigned to is shown with each assignment's start/end date and status, with navigation to the assignment's SOWs. (source: Scope; JM J8; PRD draft EC-3)

### Client Configuration
AC-21 — Add a client

Given a Compass Super Admin
When they add a client
Then the record captures a unique client name, optional MSA and NDA signed dates, and the internal-EDJE ("beach") indicator. No stored active/inactive flag is captured — client status is derived per AC-42, and a newly created client with no assignments is immediately available for assignment per AC-27. (source: Scope; JM J9; PRD draft CC-1; BA decisions Jul 31 2026; Review v1 finding B1)

AC-22 — Client invoice frequency and billable categories

Given a client record
When the Super Admin configures it
Then a client-level invoice frequency default (active types only) can be set, and zero-to-many billable time categories can be added, each with an active/inactive flag. (source: Scope; JM J9; PRD draft CC-2; BA decision)

AC-23 — Update a client

Given a Compass Super Admin editing a client
When they change client details, the invoice frequency default, the internal flag, or add/edit/deactivate billable categories
Then the changes are saved. (source: Scope; JM J10)

AC-24 — Client assignment history (admin)

Given a Compass Super Admin on a client record
When the record renders
Then every EDJEr ever assigned to the client — including inactive EDJErs — is listed with each assignment's start/end date and status. This history is never truncated by retention policy (AC-NFR-3, BR-16). (source: Scope; JM J11; PRD draft CC-3)

AC-42 — Derived client active/inactive status is informational only

Given a client
When its status is evaluated for display or reporting
Then the client is active if at least one assignment exists whose end date is empty or in the future, and inactive if it has assignments and every one of them has an end date in the past. Status is always derived from assignment data and is never stored or set by hand; because status follows the assignments, there is no client-inactivation action and therefore no cascade to live assignments or SOWs.
Derived status is a reporting and display attribute only. It must never be used to filter or restrict which clients are available for assignment (AC-27), and it never blocks a client from being selected, edited, or re-engaged. A client with no assignments has no derived status to report. (source: BA decisions Jul 31 2026 — resolves v1 OQ-2 and Review v1 finding B1)

### Lookup Administration
AC-25 — Employee Type administration

Given a Compass Super Admin
When they add or edit an employee type or toggle its active/inactive flag
Then only active employee types are selectable when configuring EDJErs. (source: Scope; JM J12; PRD draft ETC-1/2)

AC-26 — Invoice Frequency Type administration

Given a Compass Super Admin
When they add or edit an invoice frequency type or toggle its active/inactive flag
Then only active invoice frequency types are selectable when configuring client defaults and assignment overrides. (source: Scope; JM J13; PRD draft IFC-1/2; BA decision)

### Assignments
AC-27 — Create an assignment; all clients selectable

Given a Compass Ops user working from an EDJEr record or a client record
When they create an assignment
Then the assignment captures an active EDJEr, a client, a start date, an optional end date, an optional note, and an optional invoice-frequency override; an EDJEr may hold zero-to-many assignments.
Client selection shows all clients regardless of derived active/inactive status (AC-42). The picker must not filter on, or block selection by, derived status; it may surface derived status alongside each client as informational context. This ensures a newly created client can receive its first assignment and a client whose assignments have all ended can be re-engaged.
The active-EDJEr precondition is retained: EDJEr active status is an explicitly stored flag (AC-17) and AC-19 already prevents deactivating an EDJEr who holds open assignments, so the precondition creates no comparable deadlock. (source: Scope; JM J14; PRD draft AC-1/2/3; BA decisions Jul 31 2026 — resolves Review v1 finding B1)

AC-28 — Invoice-frequency override precedence

Given an assignment with an invoice-frequency override
When the effective invoice frequency is determined
Then the assignment override takes precedence over the client default; where no override is set, the client default applies. (source: BA decision)

AC-29 — End an assignment notifies the coach

Given an open assignment
When Ops sets its end date for the first time
Then the assignment is marked ended and a single email is sent to the EDJEr's coach using the assignment-end template in AC-43; subsequent edits to the assignment do not re-notify. (source: JM J14; BA decision)

AC-30 — Current assignment definition

Given an assignment
When "current" status is evaluated
Then an assignment counts as current when its end date is empty or not in the past. (source: Journey Map; PRD draft TD-2)

### SOW / Contract Tracking
AC-31 — Add an initial-contract SOW

Given a Compass Ops user on an assignment
When they add an SOW of type "Initial Contract"
Then the SOW captures start date, end date, and an optional note and appears in the assignment's SOW list. No coach notification is sent. (source: Scope; JM J15; PRD draft SOW-1/2/3)

AC-32 — Add an extension SOW notifies the coach

Given a Compass Ops user on an assignment
When they add an SOW of type "SOW Extension," set the rate-increase checkbox, and enter start/end dates and an optional note
Then the SOW is recorded and a single email is sent to the EDJEr's coach on the initial add using the SOW-extension template in AC-43; edits do not re-notify. The rate-increase control is a checkbox and is visible only to elevated roles. (source: JM J16; PRD draft SOW-2; BA decision)

AC-33 — SOW non-overlap and gaps

Given SOWs on a single assignment
When an SOW is added or edited through the application
Then its date range must not overlap another SOW on the same assignment (rejected on save if it does), each SOW end date must be on or after its start date, and gaps between SOWs are allowed.
Migration exception: SOWs seeded by data migration with the Legacy Migrated type are exempt from the non-overlap rule at load time and may be loaded in an overlapping state. The exemption ends on first edit — see AC-41 and BR-14. (source: Scope; JM J15/J16; PRD draft SOW-4; ERD; BA decision Jul 31 2026 — resolves Review v1 finding B4)

> **Reconciliation note (not part of the original PRD):** the **Legacy Migrated SOW type is
> superseded** — it was removed by owner decision on 2026-08-05 and does not exist in the schema.
> SOW type is Initial Contract or SOW Extension only, and Compass does not record whether an SOW
> was migrated. The type conflated two orthogonal facts (whether a period extends a prior one, and
> where the row came from), so a migrated *extension* could not record both.
>
> **Every migration exception keyed to that type therefore no longer applies** — here, in AC-35,
> AC-41, BR-3, BR-14 and the B4 review finding. Non-overlap and end-after-start are enforced by the
> database on **every** row, so legacy contract data that violates either is corrected before or
> during load rather than loaded as-is and reconciled at first edit.
>
> **The grandfathering is not missed, because the migration cannot produce the state it protected.**
> Per the product owner (2026-08-05), legacy TPS has no concept of an initial versus an extension
> SOW: every closed assignment comes across as a **single entry** with **no extensions**, so the bulk
> load cannot violate non-overlap — one period cannot overlap itself. Active assignments, the only
> cohort that can produce several SOWs on one assignment, are loaded with **manual intervention** and
> carry their correct types. This is consistent with AC-41's own note that TPS never tracked SOWs and
> the records are *synthesized*: the migration chooses their date ranges rather than inheriting them.
> The authoritative statement is `specs/001-edje-compass-directory/spec.md` (FR-051, FR-053, FR-072
> and the Legacy Migrated note under Assumptions).

AC-34 — Edit an SOW

Given an existing SOW
When Ops edits its type, rate-increase indicator, dates, or note
Then the changes are saved subject to AC-33 and do not re-notify the coach. (source: JM J17; BA decision)

AC-35 — SOW types

Given the SOW type
When an SOW is created
Then it is one of Initial Contract, SOW Extension, or Legacy Migrated; Legacy Migrated is reserved for data-migration records; the rate-increase indicator applies only to SOW Extension. (source: BA decision; PRD draft)

AC-43 — Coach notification recipients and content (revised in v5)

Given a notification-triggering event (AC-29 or AC-32)
When the email is composed and sent
Then the sole recipient is the EDJEr's coach, with no cc or bcc to Ops, Sales, or the EDJEr; the subject line is <EDJEr name> Assignment Change for both events; and the body names the EDJEr:
Assignment end date entered: "<EDJEr name>'s assignment at <client name> is coming to an end on <assignment end date>"
SOW extension entered: "<EDJEr name> has received an updated SOW at <client name> through <SOW end date>"
Naming the EDJEr in the body is required because a coach may manage several EDJErs and the emails deliberately carry no deep link back into Compass, so the body must be self-identifying.
Emails are notification-only and contain no link into Compass. (source: BA decisions Jul 31 2026 — resolves v1 OQ-3 and Review v1 finding S7)

### Sales Dashboard
AC-36 — Dashboard tiles

Given a Compass Super Admin, Ops, or Sales user
When they open the Sales Dashboard
Then they see tiles for: number of active assignments; SOWs expiring in under 90 days with no follow-on SOW on the same assignment; confirmed rollouts (assignments with a future end date); and EDJErs on the beach (assigned to an internal client with a null and/or future end date). (source: Scope; JM J18; PRD draft SD-1; BA decision)

AC-37 — Dashboard drill-down

Given the dashboard
When the user selects a tile
Then the underlying detail for that category is shown (e.g., EDJEr, client, SOW end date, coach), and selecting another tile swaps the breakdown. (source: JM J18; PRD draft SD-2)

### Reports
AC-38 — Availability Report

Given a Compass Super Admin, Ops, or Sales user
When they open the Availability Report
Then three chronological sections display: Currently Available EDJErs (name, internal-assignment start, coach); Confirmed Rollouts (name, current client, assignment end date, days until rollout, coach); Unconfirmed SOWs expiring within 90 days (name, current client, current SOW end date, days until expiration, coach). (source: Scope; JM J19; PRD draft RPT-3; BA decision)

AC-39 — Client Assignment Duration report

Given a Compass Super Admin, Ops, or Sales user
When they open the Client Assignment Duration report
Then all active assignments display with EDJEr, client, coach, and total duration, sorted longest-first by default with column sorting available. Duration is computed over the full assignment history and is never truncated by retention policy (AC-NFR-3, BR-16). (source: Scope; JM J20; PRD draft RPT-4; BA decision)

AC-40 — Assignment Start lookup

Given a Compass Super Admin, Ops, or Sales user
When they enter a start and end date and run the lookup
Then all assignments that started within the range are returned with EDJEr, client, and assignment start date. (source: Scope; JM J21; PRD draft RPT-5; BA decision)

### Data Migration
AC-41 — Phased migration, split ownership, and validation posture

Given the new MySQL 8.0 database
When legacy TPS data is migrated
Then it proceeds in phases: closed (ended) assignments first, transferred as-is with their SOW records seeded as the Legacy Migrated type; then open assignments, followed by manual adjustment of incomplete contract data; migrated data is reconciled against the legacy source per phase.
Ownership: the engineering/delivery team executes the data movement for each phase. Ops owns reconciliation against the legacy source and supplies the missing or incomplete SOW contract dates, since the contract knowledge resides with Ops.
Validation posture (grandfathering): migration loads legacy records as-is, bypassing application-level validation, because legacy TPS never tracked SOWs and seeded records are synthesized from ops and sales records of uneven quality. Seeded Legacy Migrated SOWs may therefore be loaded in a state that would otherwise violate AC-33. The exemption is not permanent: the first time a Legacy Migrated SOW is edited through the application, it must satisfy AC-33 in full before the save is accepted, so legacy data is corrected as it is touched rather than in a single up-front cleanup (BR-14).
Audit attribution: migration writes are attributed in the audit trail (AC-NFR-3) to a named migration system principal rather than to an individual user, so bulk loads are distinguishable from operator activity. (source: Scope; JM J22; PRD draft §7; BA decisions Jul 31 2026 — resolves v1 OQ-7 and Review v1 finding B4)

> **Reconciliation note (not part of the original PRD):** "the new MySQL 8.0 database" is
> superseded — LEAP migrates into PostgreSQL 16 and reuses the Phase 43 Postgres import
> tooling. See [ADR-003](../../adr/ADR-003-postgresql-over-prd-mysql.md).

## Non-Functional Requirements
AC-NFR-1 — External consumer integration boundary

Compass must expose its profile, client, assignment, SOW, coach, billable-category, and invoice-frequency data to authenticated, authorized external consumers (e.g., Time Tracking, OOTO) through a stable, versioned integration boundary, without those consumers reaching into Compass's internal data store directly. This data boundary is independent of user-facing navigation: Compass supplies data to Timesheet and OOTO while providing no navigation to them (AC-4, BR-15). (source: BA decision)

AC-NFR-2 — Authentication, identity source, and LEAP independence

All access requires authentication; there is no anonymous access to any Compass area. Identity and group membership are established by the Google SAML authentication response, which is passed through to whichever SSO layer initiated the flow. LEAP is the primary launch point and single-sign-on entry once available, and Compass must additionally support direct URLs and bookmarks without weakening authentication (AC-1). Because LEAP and Compass are developed in parallel and Compass may ship first, Compass carries no hard dependency on LEAP — every criterion in this PRD is satisfiable with Compass deployed standalone. (source: Scope; JM J1; BA decisions Jul 31 2026 — resolves v2 OQ-1 and OQ-2)

AC-NFR-3 — Audit trail and retention (revised in v5)

Changes to assignments, SOWs, EDJEr records, and client records must be attributable: the system records who made the change, what changed, and when. Client-record auditing includes the client's billable time categories and invoice-frequency default. Lookup tables (employee types, invoice frequency types) are not audited. Migration writes are attributed to a migration system principal (AC-41).
Retention: audit entries are retained for 2 years. Retention governs the audit trail only — assignment, SOW, client, and EDJEr business records are retained indefinitely and are never purged, because AC-24 (every EDJEr ever assigned to a client) and AC-39 (total assignment duration) depend on history older than two years and would silently return incomplete results if business data were aged out. (source: prd-reviewer audit lens; BA decisions Jul 31 2026 — resolves v1 OQ-4, v2 OQ-3, and Review v1 finding O2)

AC-NFR-4 — Performance (revised in v5)

Directory, dashboard, and report views must meet a p95 server response time of under 1 second at the expected data volume (~90 EDJErs and associated clients, assignments, and SOW history).
The target is defined as server response time, not full browser render, so it is verifiable independent of client device, browser, and network conditions — which matters given the mobile support required by AC-NFR-7, where cellular variability lies outside Compass's control. (source: BA decisions Jul 31 2026 — resolves v1 OQ-5 and Review v1 finding S6)

AC-NFR-5 — Accessibility

Screens meet WCAG 2.1 AA. (source: prd-reviewer accessibility lens; BA confirmation Jul 31 2026 — resolves v1 OQ-6)

AC-NFR-6 — Platform & boundaries

Data platform is MySQL 8.0; only US-based EDJErs are supported; reports are on-screen only for MVP (no PDF/print export); termination dates and billing rates are not stored. (source: Scope; ERD)

> **Reconciliation note (not part of the original PRD):** "Data platform is MySQL 8.0" is
> superseded — LEAP uses PostgreSQL 16. See
> [ADR-003](../../adr/ADR-003-postgresql-over-prd-mysql.md). The remaining boundaries
> (US-only EDJErs, on-screen reports only, no termination dates or billing rates) stand.

AC-NFR-7 — Responsive design and cross-platform support

Compass must be responsive and function correctly on current versions of the major evergreen browsers (Chrome, Edge, Firefox, Safari) and on iPhone and Android mobile devices. Responsive coverage applies to all screens, including Super Admin EDJEr/client configuration, lookup administration, and the assignment and SOW forms — while it is expected that Super Admin configuration work will in practice be performed on a desktop. (source: BA decision Jul 31 2026)

## Business Rules
BR-1 — Visibility matrix: regular EDJErs see active EDJErs only; all elevated roles (Super Admin, Compass Admin, Sales, Ops) see active and inactive. Time Tracking Settings are Super-Admin-only. Rate-increase indicator, SOW notes, and assignment notes are elevated-only; a regular EDJEr sees SOWs only on their own record. Compass Admin's elevated visibility carries no write rights (AC-44). (AC-9, AC-10, AC-11, AC-14, AC-15, AC-16, AC-44)
BR-2 — Role union: effective permissions are the union of a user's Compass group roles; the EDJEr base role is implicit. (AC-2, AC-3)
BR-3 — SOW integrity: SOWs on one assignment cannot overlap; end date ≥ start date; gaps allowed; overlap rejected on save. Legacy Migrated SOWs are exempt at load only (BR-14). (AC-33)
BR-4 — Beach: an EDJEr is "on the beach" when assigned to an internal-EDJE client with a null and/or future end date. (AC-36)
BR-5 — SOW expiring < 90 days: an SOW counts as expiring when its end date is within 90 days and no subsequent SOW exists on the same assignment. (AC-36, AC-38)
BR-6 — Coach notifications: email the EDJEr's coach — and only the coach, with no cc — on the first end-of-assignment and on the initial add of an extension SOW, using the subject and name-bearing bodies in AC-43; edits and reversals do not re-notify; initial-contract SOWs do not notify; emails contain no deep link. (AC-29, AC-32, AC-34, AC-43)
BR-7 — Current assignment: end date empty or not in the past. (AC-30)
BR-8 — Invoice frequency: assignment override wins over client default; else client default applies. (AC-22, AC-28)
BR-9 — Email uniqueness: EDJEr email is unique across all EDJErs, active or inactive. (AC-18)
BR-10 — Deactivation precondition: an EDJEr cannot be deactivated while holding any assignment without an end date; assignments are never auto-ended on deactivation. (AC-19)
BR-11 — Derived client status is informational: a client is active when any assignment has an empty or future end date, and inactive when it has assignments and all have ended; status is derived, never stored, and never triggers a cascade. Derived status is used for display and reporting only and never restricts client selection when creating an assignment. (AC-42, AC-27)
BR-12 — Group claims from SAML: the Google SAML response carries every group the user belongs to; Compass reads roles from the four compass_* groups in that assertion, ignores all other groups, and stores no role data. (AC-2)
BR-13 — LEAP independence: Compass must be fully operable standalone as well as LEAP-launched; no requirement depends on LEAP being deployed. (AC-1, AC-NFR-2)
BR-14 — Legacy SOW grandfathering: Legacy Migrated SOWs may be loaded by migration in a state violating the non-overlap rule; that exemption ends at first edit, when AC-33 must be satisfied in full before the save is accepted. (AC-33, AC-41)

> **Reconciliation note (not part of the original PRD): BR-14 is superseded in full.** The Legacy
> Migrated type was removed on 2026-08-05, so there is no grandfathering and no first-edit
> reconciliation — AC-33 binds every SOW at all times, enforced by the database. See the fuller note
> at AC-33 and `specs/001-edje-compass-directory/spec.md`.
BR-15 — Compass navigation boundary (new in v5): Compass navigation covers Compass functionality only. Compass provides no cross-application launcher and no navigation to Timesheet or OOTO; users reach other internal systems through LEAP or by direct system URL. This is a user-interface boundary only and does not restrict the data integration boundary in AC-NFR-1. (AC-4, AC-NFR-1)
BR-16 — Retention boundary (new in v5): the 2-year retention period applies to audit entries only; assignment, SOW, client, and EDJEr business records are never purged. (AC-NFR-3, AC-24, AC-39)

## Cross-Artifact Dependencies
Review v1 and the subsequent BA decisions require revisions to Scope and Journey Map, not to this PRD. They remain outstanding and should be cleared before the Plan stage, so that planning is not grounded on superseded upstream text.

Review finding / Required upstream change / Artifact
B2 — Journey 7 must add the blocked-deactivation path; step 2 needs the end-date precondition (AC-19, BR-10) — Journey Map
B3 — Authentication must be restated around Google SAML, LEAP as launch point, standalone operation, and ignoring non-Compass groups (AC-1, AC-2, AC-NFR-2) — Scope + Journey Map J1
B1 (follow-on) — "Ops assigns active EDJErs to active clients" must drop the active-client precondition (AC-27) — Scope
S8 (follow-on) — Journey 1 step 3 must drop the Home landing that presents Compass, Time Tracking, and OOTO app links — Compass has no cross-application launcher (AC-4, BR-15) — Journey Map
S1 — Three resolved questions still listed open: success targets, final name, migration owner — Scope + Journey Map
S2 — Replace SG&A [definition needed] with "internal, non-billable functions" — Scope
S3 — Split the single "Migration operator" actor into Migration engineer and Migration reconciler (Ops); reassign Journey 22 steps 4–5 to Ops — Journey Map
S4 — Record the responsive/cross-platform requirement as a cross-cutting scope item (AC-NFR-7) — Scope

## Resolved Decisions

### From v1 (BA, Jul 31 2026)
1 — Deactivating an EDJEr with open assignments — Block; all assignments must be end-dated first; no auto-end, because the assignment end date often differs from the deactivation date — AC-19, BR-10
2 — Client retirement and cascade to live assignments/SOWs — Client status is derived from assignment end dates, not stored; no inactivation action and no cascade — AC-21, AC-42, BR-11
3 — Coach-email recipients and content — Coach only, no cc; shared subject <EDJEr name> Assignment Change; separate bodies per event; no deep link — AC-43, BR-6
4 — Audit scope and retention — Assignments, SOWs, EDJEr records, client records; lookups excluded; 2-year retention — AC-NFR-3
5 — Performance target — Under 1 second for directory, dashboard, and report views — AC-NFR-4
6 — Accessibility target — WCAG 2.1 AA confirmed; new responsive/cross-platform NFR added — AC-NFR-5, AC-NFR-7
7 — Migration owner — Engineering moves the data; Ops owns reconciliation and missing SOW dates — AC-41
8 — Final system name — EDJE Compass confirmed; LEAP platform context added — Title, Summary, AC-1, AC-NFR-2
9 — Success measurement — Qualitative acceptance for v1; no hard numeric targets — Summary
10 — Undefined SG&A acronym — Replaced with plain wording: "internal, non-billable functions" — Summary

### From v2 (BA, Jul 31 2026)
1 — LEAP vs Google Groups as the source of role claims — The Google SAML authentication response supplies identity and every group the person belongs to, and is passed through to whichever SSO layer initiated the flow; Compass reads the compass_* groups from that assertion and ignores the rest — AC-2, AC-NFR-2, BR-12
2 — LEAP delivery sequencing and fallback if Compass ships first — LEAP and Compass are built in parallel and Compass may be ready first; Compass must behave as a standalone solution as well as LEAP-launched, with no hard LEAP dependency — AC-1, AC-NFR-2, BR-13
3 — Billable time categories inside client audit scope — Yes, included — AC-NFR-3

### From Review v1 — blocking (BA, Jul 31 2026)
B1 — How can a new client receive its first assignment when derived status makes it inactive? — Show all clients when adding an assignment, regardless of active or inactive derived status; derived status becomes display/reporting only — AC-27, AC-42, AC-21, BR-11
B4 — Migration validation posture against the SOW non-overlap rule — Grandfathering; load as-is bypassing validation; Legacy Migrated SOWs exempt at load; AC-33 must be satisfied on first edit — AC-41, AC-33, BR-14
S5 — Compass Admin read-only is only implied — Added an explicit criterion, enforced server-side — AC-44, BR-1

### From Review v1 — non-blocking (BA, Jul 31 2026)
S7 — Should coach-email bodies name the EDJEr rather than opening with the literal word "EDJEr"? — Yes; substitute the EDJEr's name in both bodies, since the coach may manage several people and the email carries no link — AC-43, BR-6
S6 — What conditions define the 1-second performance target? — p95 server response time, measured server-side rather than as full browser render — AC-NFR-4
S8 — Who owns the cross-application launcher? — LEAP exclusively. Timesheet is not launched from Compass; users reach internal systems via LEAP or direct URL, and Compass provides no navigation to OOTO or Timesheet — AC-4, BR-15, AC-NFR-1
O2 — Does 2-year retention govern only the audit trail? — Yes; audit entries purge at 2 years; business records are never purged — AC-NFR-3, BR-16, AC-24, AC-39

## Open Questions
None. All questions raised in v1, v2, and Review v1 are resolved above. The outstanding work is upstream: see Cross-Artifact Dependencies.
