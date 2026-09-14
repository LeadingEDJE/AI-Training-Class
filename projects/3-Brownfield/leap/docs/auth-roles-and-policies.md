# Roles, policies and group mappings

**What a role means, which policies it satisfies, and which Google group grants it.**
Verified against the code on 2026-08-02. Every count below was measured, not carried over.

> This repository is a trimmed, Compass-only workshop copy of a larger monorepo. The Timesheet
> **module** was removed, but its role vocabulary and policies are still declared in
> `api/Platform/Authorization/` and registered in `api/Program.cs` — a real developer running this
> app locally will still see them. The out-of-office (OOTO) module and its two roles/policies have
> been fully removed from the code, so that content is cut below rather than described as history.

This file covers **authorization** — who is allowed to do what. For the **authentication**
topology (Google SAML, cookie sessions, why the deployment must be same-origin) read
`api/Platform/Auth/` and the SAML mock guide, [`local-mock-google-saml.md`](local-mock-google-saml.md).
For the rules that apply when a **new module** adds roles, read
[`platform/adding-a-module.md` §8](platform/adding-a-module.md#8-roles-every-module-owns-its-own-and-inherits-nothing) —
it holds the isolation guarantees, the byte-exact group-name rule, and the one direction that
fails open. This file is the reference table; that file is the recipe.

---

## 1. The vocabulary is closed: 13 role strings

`api/Platform/Authorization/KnownRoles.cs` is the single source of truth. It is a
`FrozenSet`, compared case-insensitively, and it is **deliberately closed**:
`GroupRoleMappingValidator` **fails application startup** on a configured role string that is
not in it.

| Module | Count | Strings |
|---|---|---|
| Timesheet | 9 | `EDJEr`, `Manager`, `TimesheetProcessor`, `Accounting`, `HR`, `Ops`, `PayrollProcessor`, `Admin`, `SuperAdmin` |
| Compass | 4 | `Compass Super Admin`, `Compass Admin`, `Compass Ops`, `Compass Sales` — **the spaces are real** |

**Membership in this set is not a grant.** It says only that the string is a recognised role
somewhere in the platform. Which policies it satisfies is decided entirely by the policy
registrations in §3.

**Every module owns its own roles and inherits nothing.** A timesheet `SuperAdmin` is **not** a
Compass admin, and `Compass Super Admin` is **not** a timesheet root. No module policy admits a
role from another module.

---

## 2. What each timesheet role is for

These nine are frozen by decision — they are timesheet's by history, and they are unprefixed,
which is why a *new* module must prefix its own (§4).

| Role | Primary purpose |
|---|---|
| `EDJEr` | Baseline — every employee. Own timesheets, time categories, directory reads, self reads. |
| `Manager` | Timesheet approvals for direct reports. |
| `TimesheetProcessor` | Invoicing / weekly-close processing pipeline. |
| `Accounting` | Invoice and reclassified-time reports. |
| `HR` | Employee attributes, balances, admin timesheets, audit logs, HR reports. |
| `Ops` | Operational reports, period snapshots, training lookups. |
| `PayrollProcessor` | Payroll processing and the commission report. |
| `Admin` | Reference data and system configuration (time categories); locked-timesheet edit in the UI. |
| `SuperAdmin` | Timesheet root. Impersonation, user-role management, system settings, sales reps. Member of every compound **timesheet** policy below — and of no Compass policy. |

Declared once in `api/Platform/Authorization/RolePolicy.cs`. The Timesheet front-end that duplicated
these for navigation gating (`web/timesheet/`) was deleted along with the module; only the
dev/test injection profiles (§6) still exercise them from outside the API project.

---

## 3. Authorization policies — 24 role policies, 25 registrations

Measured against the current tree: **19 `AddPolicy` calls in `api/Program.cs`** (the non-role
`"Default"` policy, 9 single-role, 9 compound — the out-of-office policies were removed along with
the OOTO module) plus **6 in `api/Modules/Compass/CompassAuthorizationExtensions.cs`** (two more
than before: `CompassElevated` and `CompassReporting` were added since this file was last verified).
That is **25 registrations**, 24 of them role policies (`"Default"` is not role-based).

Endpoints reference `RolePolicy.*` constants, never string literals.

### Single-role (9)

Each requires the identically-named role: `EDJEr`, `Manager`, `TimesheetProcessor`,
`Accounting`, `HR`, `Ops`, `PayrollProcessor`, `Admin`, `SuperAdmin`.

### Compound (9) — satisfied by any one of the listed roles

| Policy | Requires any of |
|---|---|
| `ManagerOrProcessor` | Manager, TimesheetProcessor, SuperAdmin |
| `HROrSuperAdmin` | HR, SuperAdmin |
| `ProcessorOrAdmin` | TimesheetProcessor, Admin, SuperAdmin |
| `OpsOrSuperAdmin` | Ops, SuperAdmin |
| `AccountingOrSuperAdmin` | Accounting, SuperAdmin |
| `PayrollProcessorOrSuperAdmin` | PayrollProcessor, SuperAdmin |
| `InvoiceAccess` | Accounting, TimesheetProcessor, SuperAdmin |
| `BalanceAccess` | **Ops, HR, SuperAdmin** |
| `ReportDownload` | Accounting, TimesheetProcessor, HR, Ops, PayrollProcessor, SuperAdmin |

> ⚠️ **Live doc drift in the code.** The XML doc-comment on `RolePolicy.BalanceAccess` says
> "HR, PayrollProcessor, SuperAdmin". The **registered** set is Ops, HR, SuperAdmin, and the
> registration governs. Still unfixed as of 2026-08-02 — worth correcting in code.

### Compass (6)

`CompassSuperAdmin`, `CompassAdmin`, `CompassOps`, `CompassSales`, `CompassElevated` and
`CompassReporting` — registered by `AddCompassAuthorization()`. Policy-name constants are
space-free; the role strings they require carry the space (`Compass Super Admin`, etc.). **No
timesheet role satisfies any of them**, including `SuperAdmin`; each requires its own role string
plus the Compass-internal root where a Compass root legitimately applies.
`tests/unit/Authorization/CompassRoleIsolationTests.cs` asserts both directions of that denial
against the real registered authorization service, plus a positive control.

`CompassElevated` is Admin, Ops, or Sales, or the root — a read-only elevated tier (AC-16/FR-025);
never attach it to a write route, since Admin and Sales hold no write rights anywhere in Compass
(AC-44). `CompassReporting` is narrower — Sales, Ops, or the root, deliberately excluding Compass
Admin (FR-018/FR-019/AC-44) — so the dashboard and reports stay out of Compass Admin's reach even
though `CompassElevated` would otherwise admit it.

### Other authorization surfaces

- `/api/reports/available` is `.RequireAuthorization()` (any authenticated user), then filters
  the report list by role in code.
- The Slack interaction endpoint is `AllowAnonymous()` — it is Slack-signature verified — and
  passes `[Manager, TimesheetProcessor]` as the approver roles.
- A few imperative in-handler checks exist (audit-log reads; locked-timesheet edit in the UI).
- Impersonation is SuperAdmin-gated and is a **server-side session swap** that re-issues the
  session cookie with an `Impersonator` claim. It mints no token.

---

## 4. Google group → role mapping

Roles are derived at sign-in: `GoogleAuth:Groups` is a config table of
`{GroupName, Role}` pairs, read by `SignInService.CompleteSignIn`. It lives in three surfaces
that must agree — `api/appsettings.Development.json`, `deploy/helm/leap/values.yaml`, and the
mock IdP's user definitions (`docker/google-saml-mock/authsources.php`).

The development mapping, as configured:

| Google group | Grants role |
|---|---|
| `Timesheet-SuperAdmin-dev` | `SuperAdmin` |
| `Timesheet-Admin-dev` | **`HR`** |
| `Timesheet-Config-dev` | **`Admin`** |
| `Timesheet-Approval-dev` | `Manager` |
| `Timesheet-Invoicing-dev` | `TimesheetProcessor` |
| `Timesheet-Payroll-dev` | `PayrollProcessor` |
| `Timesheet-Accounting-dev` | `Accounting` |
| `Timesheet-Ops-dev` | `Ops` |
| `Compass-SuperAdmin` | `Compass Super Admin` |
| `Compass-SuperAdmin-dev` | `Compass Super Admin` |
| `Compass-Admin` | `Compass Admin` |
| `Compass-Admin-dev` | `Compass Admin` |
| `Compass-Ops` | `Compass Ops` |
| `Compass-Ops-dev` | `Compass Ops` |
| `Compass-Sales` | `Compass Sales` |
| `Compass-Sales-dev` | `Compass Sales` |
| *(no group required)* | `EDJEr` — the baseline, granted to any domain-validated user with an active person record |

**The mapping is deliberately not 1:1, and two rows surprise everyone.** This is the
authoritative organisational mapping, not an accident or an alias:

- **`Timesheet-Admin-dev` is the HR group**, not the `Admin` group. HR gates employee
  attributes, balances, admin timesheets and audit logs.
- **`Timesheet-Config-dev` is the `Admin` group** — reference data and system configuration.
- **There is no `Timesheet-HR` group and none will be created.** HR comes from
  `Timesheet-Admin-dev`. Do not go looking for a missing group.

Prod overrides these with the non-suffixed group names.

### Why the eight `Timesheet-*` group names are frozen

**The role strings are generic; the group names are what carry module scope.**
`Timesheet-Ops-dev` grants `Ops` *in timesheet* — membership in it is a statement about the
timesheet module, and someone can hold `Ops` in timesheet while holding nothing in Compass.
Renaming it to something platform-shaped (`Leap-Ops-dev`) would recast a module-scoped grant as
a platform-wide one: a silent authorization widening across every future module. **Do not
rename any of them.**

The mapper being a config table does mean group changes are additive and reversible when they
*are* wanted — add the new entry alongside the old, verify, then remove the old.

### Compass group names are byte-exact (prod + -dev)

Each Compass role is granted by **two** Google groups — the production name and the `-dev`
name — space-free like `Timesheet-*-dev` (`Compass-Admin` / `Compass-Admin-dev`). Verified from
a live SAML `Groups` attribute. **Never rewrite them.** A typo maps to nothing and grants nothing.

### The one direction that fails open

Almost every mistake here fails **closed** — a group-name typo maps to nothing and grants
nothing. Exactly one direction fails **open**:

> **A module group mapped to another module's bare role string grants that role in the other
> module.** Mapping `Compass-Ops` (or `Compass-Ops-dev`) to the bare string `Ops` grants
> **Ops in timesheet**.

Three guards exist because of it: the closed `KnownRoles` vocabulary, the fail-fast
`GroupRoleMappingValidator` at startup, and the isolation denial matrix. Detail and the proof
that each fires: [`platform/adding-a-module.md` §8](platform/adding-a-module.md#8-roles-every-module-owns-its-own-and-inherits-nothing).

---

## 5. How a policy is actually resolved

`api/Platform/Services/CompositeAuthorizationResolver.cs`. Every policy is a
`RequireAssertion` that calls `IAuthorizationResolver.HasAnyRoleAsync`, which checks:

1. **`Privilege` claims on the current identity first** (short-circuit). Since Phase 41 these
   are stamped onto the **cookie** identity by the sign-in pipeline from the Google group
   mapping. There is no JWT and no bearer token.
2. **Then the local `user_roles` table**, keyed by the `EdjeId` claim. Additive / OR semantics —
   a role granted in either place is held.

So an app-side grant in `user_roles` (managed at `/admin/user-roles`) is genuinely additive on
top of whatever the Google groups conferred. It is not an override and it cannot revoke.

> **Stale comment in code:** the class's XML doc and inline comments still say "JWT claims
> first (IdP-issued)" and "IdP always wins". The *behaviour* is correct; the wording predates
> Phase 41. The claims are on the cookie identity now.

> ⚠️ **Known gap: `UserRoleService.AssignRoleAsync` does not validate the role name.** It will
> persist any arbitrary string into `user_roles`; the admin UI's dropdown is the only
> constraint. `KnownRoles` guards *configuration* at startup, not this write path. A garbage
> row grants nothing (no policy requires it), so this is a data-hygiene gap rather than a
> privilege-escalation one — but it should be validated against `KnownRoles`.

---

## 6. Signing in without Google

| Path | Where | Notes |
|---|---|---|
| `google-saml-mock` container | local, via `make dev-all SAML=1` | The **real** Sustainsys handshake against a mock Google. See [`local-mock-google-saml.md`](local-mock-google-saml.md). |
| `/auth/stub-login` | Development only | Drives the same sign-in pipeline with a supplied identity, so group mapping and person resolution really run. |
| `DevBypass` | Development only | Profile-based claim injection when no session exists; profiles in `api/appsettings.Development.json`, overridable per request. |
| `TestAuthHandler` | test projects | Injects claims directly; wired by the test factories. |

All four are hard-off under `ASPNETCORE_ENVIRONMENT=Production`.

**Bootstrap root access** is a separate, deliberate surface: at startup a seeder ensures an
active person row plus a `SuperAdmin` `user_roles` grant for each configured address, so a
fresh environment with an empty `people` table is signable-in before any Google group is
attached. The list is in-repo on purpose — it is a privilege-grant surface, so it stays
PR-reviewable. It is **timesheet-scoped** and was deliberately not extended to Compass.

---

## Provenance

This document replaces `docs/idp-roles-authoritative.md`, a 2026-07-20 point-in-time audit of
the same subject written during the custom-IdP era. Everything above was re-derived from the
code rather than copied forward; the parts of that audit which described the IdP transport (a
JWT `Privilege` claim issued by `login.LeadingEdje.com`, the vendor IdP application, its
database read path, the `AutoSelect` policy scheme) described machinery that Phase 41 deleted,
and its "18 policies" and "9 roles are the whole vocabulary" counts were both stale. The
original text is preserved in git history at tag `archive/docs-2026-08-02`.
