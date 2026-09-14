# LEAP Platform — Non-Functional Requirements Catalog

**Status:** Approved design-first (2026-07-31).
**Purpose:** The source of truth for the platform's non-functional targets and how each is
verified. Every NFR is written so it can be checked — an NFR that cannot be verified will
not survive a platform with no dedicated maintenance team.

**Related:** the target-state architecture doc and ADR-002..007 this catalog was distilled from are
not present in this trimmed workshop copy; Compass PRD
[docs/02_prd/finalized/PRD-compass.md](../02_prd/finalized/PRD-compass.md).

**Driving constraint:** LEAP has **no dedicated maintenance team**. Reliability, correctness,
and boundary integrity must come from design and automation, not from people watching.

**Cross-reference key:** `AC-NFR-n` / `AC-n` / `BR-n` refer to the Compass PRD.

**Verification status:** Every "Verify" line below is a *target method*, not a passing test.
These artifacts are design-first; the checks are implemented in the follow-on build.

---

## Maintainability & Operability (M)

These carry the "no maintenance team" constraint. See ADR-002.

- **M1 — Bounded ops surface.**
  Target: exactly one deployable, one database, one CI/CD pipeline for the whole platform.
  Cost is subsumed here rather than tracked as a separate category.
  Verify: deployable count = 1 and DB count = 1 (a second of either fails review).

- **M2 — Self-managing data.**
  Target: every routine data change (add/deactivate EDJEr, client, assignment, SOW, lookup
  values) has an admin-UI path; no SQL/DBA needed for normal operation.
  Verify: no routine-ops runbook step requires raw SQL. (Compass is largely this by design.)

- **M3 — Boundaries enforced by the build, not by discipline.**
  Target: an architecture test fails CI if a consumer module references Directory-owned
  tables/internals directly (mechanizes AC-NFR-1 so it cannot rot).
  Verify: a deliberately-bad reference turns the build red.

- **M4 — Automated migrations.**
  Target: schema migrations run in the pipeline, never by hand; drift is detected in CI.
  Verify: pipeline applies migrations; a drift check is present and blocking.

- **M5 — Data durability.**
  Target: automated PostgreSQL backups with a *rehearsed* restore. This database is the
  company's single source of truth for people/clients; loss is catastrophic.
  Verify: backup schedule exists and a documented restore has been executed. (Ties to R3.)

- **M6 — Failures visible without a watcher.**
  Target: health/readiness checks, structured logs, and a minimal alert on "app down / DB
  unreachable."
  Verify: health endpoints green in smoke tests; at least one alert wired.

- **M7 — Dependency hygiene on autopilot.**
  Target: Renovate/Dependabot plus the existing green-CI gates so upgrades need no standing
  effort.
  Verify: update bot configured; CI blocks on failures.

- **M8 — Explicit HiBob source-of-truth boundary.**
  Target: a documented field-ownership list separating what Compass owns from what HiBob
  (the HRIS of record) owns, so the two do not silently drift into manual reconciliation.
  Verify: field-ownership list exists; Compass omits/rejects HiBob-owned fields.
  Source: Compass PRD Summary/Scope (HiBob remains HRIS of record).

## Reliability & Availability (R)

Reliability comes from design and automation (health-gated deploys, auto-rollback,
idempotent jobs, point-in-time backups), not from on-call humans.

- **R1 — Availability target.**
  Target: 99.5% during business hours (ET), best-effort off-hours. No 24/7 on-call.
  Verify: uptime monitor on the health endpoint.

- **R2 — Fault isolation across modules.**
  Target: one module's failure (Timesheet, OOTO) must not take down Compass or the host;
  boundary calls have timeouts and scoped error handling.
  Verify: fault-injection test — break a consumer, Compass still serves.

- **R3 — Recovery objectives.**
  Target: **RPO ≤ 5 min** (PostgreSQL point-in-time recovery) and **RTO ≤ 4 hours**
  (rehearsed restore).
  Verify: a rehearsed restore meets both objectives. (Ties to M5.)

- **R4 — Deployment safety.**
  Target: health-gated rolling deploys with automatic rollback on failed health
  (Helm `--atomic`). No manual "watch the deploy" step.
  Verify: a deliberately-broken deploy auto-rolls back.

- **R5 — Stateless instances.**
  Target: app instances hold no session state (cookie sessions + shared Data Protection key
  ring), so restarts/scaling never log people out.
  Verify: a rolling restart keeps existing sessions alive.

- **R6 — Consistency under concurrent edits.**
  Target: optimistic concurrency (row version) on Compass write entities; a stale write is
  rejected with a clear "reload, someone changed this" response.
  Verify: a concurrent-edit test rejects the stale write.

- **R7 — Reliable, idempotent background jobs.**
  Target: coach notifications (AC-29 / AC-32) and reminders are idempotent — a retry or
  restart never double-sends; AC-29 already requires "subsequent edits do not re-notify."
  Verify: replay/re-trigger sends exactly once; a delivery log records each send.

- **R8 — Bounded resource use.**
  Target: connection-pool sizing plus a request timeout so one slow report query cannot
  exhaust the pool at expected volume.
  Verify: load test at ~90 users holds the p95 target (ties to P1).

## Data Integrity & Audit (D)

Correctness is enforced **server-side in the domain**, because there are multiple entry
points (UI, the versioned API, migration) and no team watching. See ADR-007 for retention.

- **D1 — Attributable change audit.**
  Target: record who/what/when for changes to assignments, SOWs, EDJEr records, and client
  records (client audit *includes* billable categories and the invoice-frequency default).
  Lookup tables are not audited. Migration writes are attributed to a named migration
  principal, distinct from operators.
  Verify: editing an audited entity produces an entry with actor + before/after + timestamp;
  editing a lookup produces none.
  Source: AC-NFR-3, AC-41.

- **D2 — Retention asymmetry.**
  Target: audit entries purge at **2 years**; business records (assignment, SOW, client,
  EDJEr) are **never purged** — AC-24 and AC-39 depend on history older than two years.
  Verify: the retention job deletes only audit rows; a 3-year-old assignment still appears
  in AC-24 / AC-39 output.
  Source: AC-NFR-3, BR-16. See ADR-007.

- **D3 — Invariants enforced in the domain, not the UI.**
  Target: each rule below is enforced at the API/domain level with a test that bypasses the UI:
  - Email unique across active *and* inactive EDJErs (AC-18 / BR-9).
  - SOW non-overlap, end ≥ start, gaps allowed (AC-33 / BR-3), with the Legacy-Migrated
    load-time exemption that ends on first edit (AC-41 / BR-14).
  - Deactivation blocked while any assignment lacks an end date; never auto-end (AC-19 / BR-10).
  - Derived client status computed, never stored, and never restricts assignment selection
    (AC-42 / BR-11).
  - Invoice-frequency precedence: assignment override > client default (AC-28 / BR-8).
  - "Current" = end date empty or not in the past (AC-30 / BR-7).
  - Compass Admin is read-only, enforced server-side (AC-44).
  Verify: an invariant test suite hitting the API/domain directly.

- **D4 — No orphaning of the shared core.**
  Target: Directory entities are deactivated, never hard-deleted; ids are permanent/stable
  because other modules reference them by id.
  Verify: no hard-delete path exists for referenced entities; an id-stability test.

- **D5 — Migration integrity.**
  Target: phased (closed assignments first, then open, then manual SOW completion),
  reconciled per phase, grandfathered validation, attributed to the migration principal, and
  idempotent on re-run (stable EdjeIds).
  Verify: re-running a phase changes nothing; a per-phase reconciliation report exists.
  Source: AC-41, BR-14.

- **D6 — Time/precision correctness.**
  Target: dates with no time component map to native Postgres `date`; "current," "beach," and
  "expiring < 90 days" are evaluated in Eastern Time so day-boundary math is stable.
  Verify: boundary tests at ET midnight for the current/beach/90-day rules.

- **D7 — Versioned, stable boundary contract.**
  Target: the published Compass API is versioned (`/v1`); a breaking change requires a new
  version so consumers never silently break.
  Verify: an OpenAPI/contract-diff gate flags breaking changes. (See E2.)

## Security & Compliance (S)

The auth mechanism already exists; the work is consistent, boundary-level enforcement.
See ADR-005 for machine-to-machine auth.

- **S1 — No anonymous access, ever.**
  Target: every Compass area requires auth; direct links/bookmarks authenticate rather than
  bypass (AC-1, AC-NFR-2).
  Verify: an unauthenticated request to any route redirects to auth and leaks no data.

- **S2 — Identity from SAML, roles from group claims, never stored.**
  Target: roles derived from the four `compass_*` groups, all other groups ignored, effective
  access = union of roles, EDJEr base role implicit (AC-2, AC-3, BR-2, BR-12).
  Verify: a role-resolution test from SAML groups; no role rows persisted.

- **S3 — Server-side authorization on every endpoint.**
  Target: the visibility matrix (BR-1) is enforced in the API, not by hiding nav — regular
  EDJErs see active EDJErs only and SOWs only on their own record; rate-increase/SOW
  notes/assignment notes are elevated-only; Time Tracking Settings are Super-Admin-only;
  Compass Admin is read-only (AC-44).
  Verify: per-role/per-endpoint authorization tests; a lower role hitting an elevated
  endpoint gets 403, not merely a hidden control.

- **S4 — Least privilege by role.**
  Target: the four roles + implicit EDJEr map to an explicit capability matrix; reuse the
  existing policy/privilege infrastructure.
  Verify: a capability-matrix test.

- **S5 — Standalone auth without weakening it.**
  Target: Compass authenticates on its own when LEAP is absent (BR-13), same-origin so the
  cookie stays first-party.
  Verify: login works with the LEAP shell not deployed.

- **S6 — The versioned boundary is authenticated and authorized.**
  Target: in-process consumers ride the caller's session; out-of-process consumers use a
  service credential and are authorized per scope (AC-NFR-1).
  Verify: unauthenticated call to `/api/compass/v1` → 401; out-of-scope → 403.

- **S7 — Secrets and transport.**
  Target: no hardcoded secrets/URLs; secrets from Secrets Manager under the mandated
  `/chat-edje/<env>/` prefix; HTTPS only; HttpOnly + `SameSite=Lax` + Secure cookies.
  Verify: secret scan in CI; cookie-flag assertions in smoke tests.

- **S8 — PII minimization / HiBob boundary.**
  Target: Compass stores only basic operational profile data (name, email, hire date, state,
  coach, employee type); no termination dates, no billing rates (AC-NFR-6); US-only EDJErs.
  Verify: schema has no billing-rate/termination-date columns; stored field list matches the
  M8 ownership doc.

- **S9 — Supply-chain / SAST in CI.**
  Target: dependency audit + SAST + pinned actions (existing Trivy-pinning constraint),
  blocking on high severity.
  Verify: CI runs the scans and blocks.

- **S10 — Machine-to-machine auth for external consumers.**
  Target: **OAuth2 client-credentials** — per-consumer identity, scoped authorization,
  revocation, rotation; `/v1` endpoints accept a user session *or* a service token.
  Verify: a scoped token succeeds; an out-of-scope call → 403; a revoked credential → 401.
  See ADR-005.
  Note: auditing sensitive *reads* is explicitly out of scope for v1 (no compliance driver);
  D1 audits changes only.

## Extensibility & Integration (E)

The crux of the umbrella goal: new apps that need employee/client data must be cheap and
uniform to add, and impossible to add wrongly. See ADR-004.

- **E1 — One published boundary is the only door.**
  Target: all directory data is reached through the Compass boundary, never raw tables
  (AC-NFR-1).
  Verify: the M3 architecture test plus contract tests.

- **E2 — Versioning policy consumers can trust.**
  Target: OpenAPI-first; additive changes within `v1`, breaking changes to `v2` with a
  defined support window; a generated typed client (`@leap/directory-client`).
  Verify: an OpenAPI-diff gate flags breaking changes; the generated client builds in CI.

- **E3 — A golden path to add an app.**
  Target: a documented recipe — mount under a URL prefix in the shell, authenticate via the
  shared session, consume the directory client, inherit CI/deploy — with no bespoke wiring.
  Verify: the recipe produces a working "hello module" that reads an employee via the boundary.

- **E4 — A module scaffold.**
  Target: a reference module shape (folders, DI registration, endpoint group, tests, nav tile)
  so every app looks alike.
  Verify: the scaffold exists; new modules match it.

- **E5 — Registration-based launcher.**
  Target: adding a tile + route is registration, not surgery on the shell; tiles are
  role-gated.
  Verify: adding a module adds a role-gated tile via registration only.

- **E6 — Shared cross-cutting services consumed, not copied.**
  Target: auth/session, audit, notifications, ET/precision helpers, and the directory client
  are platform services every module reuses.
  Verify: modules consume shared services; enforced by arch tests where feasible.

- **E7 — Extend data without breaking consumers.**
  Target: Compass can add employee/client fields without touching consumers, because they
  bind to versioned DTOs, not the schema.
  Verify: adding a Directory column leaves the `v1` DTO unchanged.

- **E8 — Reversible to a standalone service.**
  Target: the in-process interface and the HTTP `/v1` expose the same contract, so a future
  extraction is a transport swap, not a rewrite.
  Verify: consumers depend only on the client interface, never on in-process internals.

- **E9 — Consumer-driven contract tests in CI.**
  Target: a Compass change that would break an in-repo consumer (Timesheet, OOTO) fails
  Compass's build, not production.
  Verify: contract tests run in CI and go red on a breaking producer change.
  Note: external (out-of-repo) consumers rely on E2 versioning + deprecation policy + the
  E10 sandbox instead of their tests running in this pipeline.

- **E10 — The HTTP `/v1` API is a supported external product.**
  Target: published OpenAPI as the public contract, generated clients, an independent
  versioning/deprecation lifecycle, and a sandbox.
  Verify: a consumer with no shared process authenticates and reads directory data
  end-to-end.

- **E11 — Governed, low-friction consumer onboarding.**
  Target: registering an external consumer (issue credential, assign scopes) is
  self-service-ish (ties to M2), with per-consumer rate limits/quotas protecting the source
  of truth.
  Verify: a new consumer is provisioned via admin/config; its rate limit is enforced.

## Performance / Accessibility / Responsive (P)

- **P1 — Server response time.**
  Target: p95 server response < 1s for directory, dashboard, and report views at expected
  volume (~90 EDJErs and associated data), measured server-side (AC-NFR-4).
  Verify: a perf test asserts the server p95.

- **P2 — The boundary is a hot path; keep it cheap.**
  Target: directory reads (hit by every module and external consumers) are indexed and free
  of N+1.
  Verify: index-coverage and no-N+1 assertions on boundary queries. (E11 rate limits protect
  P1.)

- **P3 — Accessibility.**
  Target: WCAG 2.1 AA on all screens (AC-NFR-5).
  Verify: automated axe checks in CI plus a manual audit of key flows.

- **P4 — Responsive / cross-platform.**
  Target: responsive on evergreen browsers (Chrome/Edge/Firefox/Safari) + iPhone/Android,
  all screens including admin/config and assignment/SOW forms (AC-NFR-7).
  Verify: Playwright cross-browser plus the viewport-screenshot standard (390x844,
  767/768x1024, 1280x800, 1920x1080).

  **Where the verification lived (Compass, feature 011 / issue #83, 2026-08-25):** a
  `specs/011-compass-cross-browser-mobile-verification/` evidence doc covering 23 screens (22 leaf
  routes + `NotFoundPage`) x 5 viewports x 5 engine/device projects, with the gate definitions and
  the per-screen disposition of every finding — the `specs/` tree it lived in is not present in
  this trimmed workshop copy. The live gate is `web/compass/tests/e2e/responsive-stranding.critical.spec.ts`.

  **Two questions resolved by the owner on 2026-08-20. Recorded here so neither is re-derived:**

  - **Q1 — Edge is covered by Chromium; no `channel: 'msedge'` project.** Edge is Chromium-shelled,
    so a dedicated project re-runs the same engine at the cost of a second browser download. "Chrome/
    Edge" in the target above is **one** engine obligation, not two.
  - **Q2 — "iPhone/Android" means Playwright device emulation in CI, plus a one-time scoped manual
    pass on real hardware.** Emulation is the standing gate (`devices['iPhone 14']`,
    `devices['Galaxy S24']`, both with `isMobile`); it cannot show a real iOS Safari viewport-unit
    quirk, a real on-screen-keyboard resize, or real momentum scrolling, which is what the manual
    pass is for. The manual pass is **not** a recurring gate — repeat it only when the layout system
    changes, not per release.

  **Cadence, which is the existing policy rather than a new one.** Chromium runs on every pull
  request; Firefox, WebKit and both emulated devices run on merge to `main` and on the Tuesday 09:30
  UTC schedule — `.github/workflows/ci.yml`'s measured decision for the timesheet tree (Firefox
  601 s, WebKit 554 s, *"neither said anything chromium had not"*), recorded there as *"a DEMOTION,
  NOT A DELETION"*. `compassProjectsFor()` in `.github/scripts/detect-modules.cjs` derives Compass's
  project set from the same `enginesFor` so the two cannot drift apart.

  **The emulated Android is 360px wide — narrower than the 390px floor above.** That is deliberate:
  it tests below the documented minimum and has already found one page-overflow defect there. A
  screen may be waived below 390px **by name**, never by selector class.

  **There is no screenshot standard any more, and the caveat that stood here is worth keeping.**
  Visual baselines *were* captured at 1280x800 until issue **#574** deleted the whole gate on
  2026-09-08 — above Tailwind's `md` breakpoint, so they proved desktop presentation did not move and
  said **nothing** about the layout below it. That is why responsive behaviour always needed its own
  assertions, and it is why deleting the gate costs this standard nothing: a green visual suite was
  never evidence of a correct phone layout. The 767/768 pair matters for the same reason — it
  straddles `md`, and a defect has been measured at 768 that 390 alone did not show.

- **P5 — No PDF or print export.** *(Narrowed 2026-08-25, issue #337.)*
  Target: no PDF/print export (AC-NFR-6) — a deliberate scope/complexity boundary.
  **CSV export of the three Compass reports IS in scope** and shipped under issue #337. AC-NFR-6's
  parenthetical names PDF and print only; "CSV" appears in neither the AC, PRD v11 nor Plan v7, and
  the previous wording of this entry ("no export endpoints ship in v1") was a broader reading of it
  than its text supports. The Sales Dashboard remains export-free — it is a tile summary, not a
  report.
  Verify: no PDF or print affordance on any Compass screen, asserted by
  `web/compass/tests/e2e/export-control.critical.spec.ts` (which also asserts the reports DO expose a
  CSV control and the dashboard does not). Rationale and the surviving counter-reading:
  `specs/007-compass-dashboard-reports/spec.md` Deviation 12.
