# Compass module (API)

The API half of EDJE Compass: a module boundary, the `compass` schema's 7 tables, and — since feature
004 — real configuration surfaces.

## What this is

**This README described a deliberately EMPTY scaffold until 2026-08-06, and that is no longer the
state.** The empty slate was an owner decision rather than an oversight, and it was the deliverable of
Phase 49; PR #181 then added the schema and feature 004 added write paths. The history is recorded
rather than deleted, because the rule the clean slate protected is still binding:

> **Nothing is added speculatively.** Every entity, field, DTO member and route traces to an accepted
> acceptance criterion. `CompassEdjerDtoTests` and `CompassLookupDtoTests` pin their member sets for
> exactly this reason, and `CompassSchemaFromErdTests.Client_HasNoStoredStatusColumn` fails the build if
> a status column appears.

Nothing existing moved into Compass. `Person` is a native Platform entity (`api/Platform/Domain/`);
the rest of the legacy directory records (`Employee`, `Client`, `Assignment`, `JobTitle` and their
siblings) were dropped with the Timesheet/Ooto module removal. Compass's own
`Employee` and `Client` are **separate entities against separate tables** — `compass.employee` and
`compass.client` — and that duplication is transitional and tracked, not a mistake to collapse.

## Configuration surfaces

All under `/api/compass/v1/admin`, all gated by `RolePolicy.CompassSuperAdmin` through
`Endpoints/Admin/CompassAdminRouteGroup.cs`.

| Resource | Routes | Audited? | Story |
|---|---|---|---|
| `employee-types` | `GET`, `POST`, `PUT /{id:int}` | **No** — FR-008 | 004 US1 (#58) |
| `invoice-frequency-types` | `GET`, `POST`, `PUT /{id:int}` | **No** — FR-008 | 004 US1 (#58) |
| `edjers` | `GET`, `GET /{id:int}`, `POST`, `PUT /{id:int}` | **Yes** — FR-017 | 004 US2 (#59) |
| `clients` | `GET`, `GET /{id:int}`, `POST`, `PUT /{id:int}` | **Yes** — FR-027 | 004 US3 (#60) |
| `clients/{clientId:int}/billable-time-categories` | `POST`, `PUT /{categoryId:int}` | **Yes**, against the **client** | 004 US3 (#60) |

**There is no `DELETE` on any of them.** Lookups and categories are deactivated; an EDJEr is
deactivated through a guarded flag; a client is never retired at all, because its status is derived
(below). Records elsewhere reference all four (Principle VIII).

**Category writes are audited against their CLIENT**, not as their own entity — AC-NFR-3 puts
categories and the invoice-frequency default inside the client's audited scope, so "what changed about
this client" is one query. Their change-list entries are qualified (`BillableTimeCategory:Development.IsActive`)
because a bare `IsActive` in a client's trail would read as though the *client* had been deactivated,
which is the one thing that can never happen.

**⚠️ Never substitute `RolePolicy.CompassAdmin` on a write.** It resolves to `"Compass Admin" OR
"Compass Super Admin"`, and Compass Admin is READ-ONLY however administrative the name sounds (AC-44,
Principle IV). That substitution would pass every other test in the feature;
`CompassAdminRouteGroupTests` is what fails the build on it.

**The audit asymmetry in that table is deliberate.** AC-NFR-3 says verbatim that lookup tables are not
audited, and `CompassLookupServiceTests` asserts structurally that the lookup service cannot even reach
`IAuditService`. `CompassEmployeeServiceTests` asserts the mirror image. A reviewer who expects audit on
every Compass write — or on none — will find one of the two surprising; both are correct.

### Client status is DERIVED — read this before touching anything that reports it

A client's Active/Inactive is **computed from its assignments on every read** (BR-11): Active exactly
when it holds at least one *current* assignment, where current means the end date is empty or not in the
past. It is **never stored, never settable, and never a filter**, and there is deliberately no
`is_active` column on `compass.client`.

Four rules, each with a build gate behind it:

1. **One implementation.** `Services/ClientStatusDerivation.cs` is the only file permitted to compare an
   assignment's `EndDate`, or to name `ClientStatus.Active`/`Inactive`.
   `ClientStatusSingleDerivationTests` fails the build on any other. Independent implementations diverge
   on the exactly-today case, which AC-42 names as the one nobody tests by hand.
2. **Lists derive SET-WISE.** One query answers "which of these are active" for the whole collection —
   `IsActive(today)` composed into the query, never `Of()` in a loop. Both list surfaces have a test
   measuring the command count at two different row counts and failing if it grows.
3. **Nothing HOLDS the enum.** DTOs publish a `string`, so a stale value cannot outlive the assignments
   it summarises (`ClientStatusNonGatingTests`). The enum lives inside the derivation.
4. **"Today" is Eastern, and comes from `ICompassBusinessDate`.** Never `DateTime.UtcNow` —
   `CompassBusinessDateTests` fails the build on an ambient clock anywhere in the module, because a
   UTC-derived date is correct all day and wrong every evening after about 20:00 Eastern.

**Status gates nothing, and that is load-bearing rather than defensive.** A client with no assignments is
Inactive, so every brand-new client is Inactive — any path that filtered, hid, or refused on Inactive
would make a new client impossible to use, exactly as AC-42 warns. The detail route asks the database
whether *any* current assignment exists rather than calling `Of()`, because `Of()` evaluates over
`Client.ClientAssignments` and a caller who forgets the `Include` gets a confident, silent "Inactive".

### Two things about the EDJEr write path that are easy to get wrong

**`422`, not `400`, for a refused deactivation.** An EDJEr holding an assignment with no end date cannot
be deactivated (FR-019, BR-10, AC-19), and the response *lists the blocking assignments* so the answer
is actionable. The request was well formed and the caller authorised; the state of the world forbids it.
No assignment is ever auto-ended — an assignment's true end date frequently differs from the
deactivation date — and the recovery path needs Stream 3's assignment surface. It uses
`Dtos/CompassWrite.cs`, a module-local envelope, because the platform-shared `AdminMutationStatus` falls
through to `400` on anything it does not recognise.

**The save happens BEFORE the audit call**, which is the opposite of what `AuditService`'s own remarks
suggest. A create needs the identity key for `EntityId` and EF assigns it at save; and
`ICompassUnitOfWork` is what translates a lost uniqueness race into a `409` rather than a `500`. The
trade-off is documented at `CompassEmployeeService`.

## The folders

| Folder | Contents today | Purpose |
|---|---|---|
| *(module root)* | 7 entities + `UsStateCodes` | Entities live at the module root. |
| `Contracts/` | `IDirectory` and the six DTO types reachable from its signatures (`CompassEmployeeDto` + its two co-located siblings, `CompassClientDto`, `CompassAssignmentDto`, `CompassInvoiceFrequencyDto`, `CompassBillableCategoryDto`, `CompassDirectorySowDto`) | The published, in-process directory contract — the ONE Compass namespace a consumer outside the module may reference (spec 013, #451). Kept flat: no subdirectory. |
| `Dtos/` | The EDJEr, client, SOW and lookup request/response DTOs, `CompassWrite<TDto>` | Internal contracts for surfaces OTHER than the published directory boundary. Member sets pinned by tests. |
| `Interfaces/` | One repository + service interface per surface, plus cross-cutting interfaces (`IClientStatusDerivation`, `ICompassUnitOfWork`, ...) | Interfaces travel with their implementation, inside the module. |
| `Services/` | `CompassDirectoryService`, `CompassLookupService`, `CompassEmployeeService`, `CompassAuditReason` | Business logic. Owns `SaveChangesAsync` via `ICompassUnitOfWork`. |
| `Data/` | `CompassUnitOfWork` | The save boundary. The ONLY place in this module allowed to name the context. |
| `Data/Repositories/` | directory, lookup and employee repositories | Data access. **No `SaveChangesAsync`.** |
| `Data/Configurations/` | 7 entity configurations + `CompassEntityConfiguration` base | The only `ToTable` call sites in the repository. |
| `Data/SeedData/` | `CompassDirectorySeeder` | 97 employees, 28 clients, relative dates. |
| `Endpoints/` | the boundary read | `/api/compass/v1/employees/{id:int}` |
| `Endpoints/Admin/` | the configuration surfaces + the shared route group | `/api/compass/v1/admin/...` |

Namespaces mirror folders 1:1. This is the **complete** vertical slice: the module boundary lives in
folders and namespaces, not in a separate data store.

## Deliberately not following the OOTO module's shape

The OOTO module's landing was a partial slice — its entities went flat into the shared models folder,
its EF configurations flat into the shared configurations folder, its interfaces flat into the shared
interfaces folder, and it has **no repository layer at all** (its services inject the database context
and query it directly, which is precisely the habit a boundary exists to prevent). Its endpoint routes
are also unversioned, inherited from a legacy system's shapes and now contract-locked by an absorbed
SPA.

**Use the OOTO module as a cautionary note, not as a template.** Compass gets the complete slice so
the playbook documents one coherent pattern.

## One context, one migration history

Compass entities — when the team creates them — go on the **existing** `LeapDbContext`. Do **not**
create a second context, a second connection string, a second migrations-history table, or a
model-wide default schema. Each of those would reverse an owner decision, and each would destroy the
property the single context buys: one `SaveChangesAsync` can span modules, cross-schema joins work in
one query on one connection, and cross-module navigation properties and foreign keys are possible.

`scripts/check-one-context.sh` guards all four of those invariants and prints its measured counts.
