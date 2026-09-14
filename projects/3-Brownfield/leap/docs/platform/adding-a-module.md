# Adding a module to LEAP

**This is a record of what was actually done, not a theory.** Every step below was performed while
adding the **EDJE Compass** module, and every command was run. Where a step has a trap, the trap is
described with the real error text produced when it was hit.

Read it in order. It is long because the traps are real and several of them are invisible until
production.

**Worked example:** `api/Modules/Compass/` (backend) and `web/compass/` (front-end). When a step is
unclear, open the Compass equivalent — it is deliberately minimal, so it reads as a template.

> **Compass owns 7 tables in the `compass` schema** since PR #181 (2026-08-06), so it is now a worked
> example of *both* the module shape and a module's own schema —
> `api/Modules/Compass/Data/Configurations/CompassEntityConfiguration.cs` is the base class to copy in
> section 3 below. Until then its schema was intentionally empty, by owner decision rather than
> oversight, so that the incoming team designed the data model themselves; that constraint has been
> met and lifted.

---

## 1. The data model: one context, one migration history

**LEAP is one application with submodules.** One database, one `LeapDbContext`, one migration history.
A module gets its own Postgres **schema** purely as an organizational namespace, mapped in that
module's own entity configurations.

### Three prohibitions, and why each one matters

| Never add | Why |
|---|---|
| A second `DbContext` | Two persistence boundaries. No single `SaveChangesAsync` spanning modules, no cross-module navigation properties, no cross-module foreign keys. This is the property with real value, and a second context destroys it silently. |
| `MigrationsHistoryTable` | Splits migration history in two. You lose one ordered account of what has been applied. |
| `HasDefaultSchema` | Model-wide. It moves *everything*, not just your module. |

If something appears to require one of these, **stop and ask.** Each reverses an owner decision.

These are enforced mechanically, so you will be told rather than discovering it later:

```bash
bash scripts/check-one-context.sh
```

```
[one-context] EF context registrations (AddDbContext):        1  (required: exactly 1)
[one-context] Custom migrations-history configurations:       0  (required: exactly 0)
[one-context] Model-wide default-schema configurations:       0  (required: exactly 0)
[one-context] Compass-specific EF context declarations:       0  (required: exactly 0)
[one-context] All four one-context invariants hold.
```

The script **prints its measured counts**, not just a verdict — a gate that only says "OK" cannot be
distinguished from one that measured nothing. All four invariants were deliberately violated one at a
time and each was observed to fail with exit 1, for example:

```
INVARIANT VIOLATED: one-context -- expected exactly 1 AddDbContext registration, found 2.
```

### The schema is a namespace, not a boundary

This was proven, not assumed. On the single context, in a single transaction:

- a cross-schema join (`compass.<table> JOIN public.time_categories`) returns rows, on one connection;
- one `SaveChangesAsync` against a `public` entity and a write into `compass` are visible in the same
  open transaction, and **neither survives the rollback**.

See `tests/integration/Compass/CompassSchemaIsANamespaceTests.cs`. Cross-schema foreign keys work in
Postgres, so your module *can* reference the shared directory entities with real navigation properties.

---

## 2. Creating an empty module schema

Adding a schema that has **no entity mapped to it yet** produces an **empty migration scaffold** — the
model diff is empty, so EF generates nothing. EF only emits `EnsureSchema` as a side effect of
scaffolding an entity mapped to that schema. This surprises people; it is expected.

The sequence used:

```bash
docker compose up -d postgres
dotnet ef migrations add AddCompassSchema --project api \
  --output-dir Platform/Data/Migrations --context LeapDbContext
```

Then hand-author the two operations in the generated file (see
`api/Platform/Data/Migrations/20260801094020_AddCompassSchema.cs`):

- `Up`: `migrationBuilder.EnsureSchema(name: "compass");`
- `Down`: `DROP SCHEMA IF EXISTS "compass" RESTRICT;` — **`RESTRICT`, not `CASCADE`.** A guarded drop
  refuses on a non-empty schema instead of silently deleting a colleague's tables.

Confirm the model snapshot did **not** change (an empty schema adds nothing to the model):

```bash
git status --porcelain api/Platform/Data/Migrations/LeapDbContextModelSnapshot.cs
# expect: no output
```

Apply it and then **inspect the live catalog.** Reading the generated C# is *not* verification:

```bash
make migrate
docker compose exec -T postgres psql -U timesheet -d leap_dev -c '\dn'
docker compose exec -T postgres psql -U timesheet -d leap_dev \
  -c "SELECT count(*) FROM information_schema.tables WHERE table_schema='compass';"
docker compose exec -T postgres psql -U timesheet -d leap_dev \
  -c "SELECT table_schema, table_name FROM information_schema.tables
      WHERE table_name ILIKE '%EFMigrationsHistory%';"
```

> The local database is **`leap_dev`** (`POSTGRES_DB` in the `Makefile`). These commands originally
> read `timesheet_dev`, which was renamed in Phase 51 and **dropped on 2026-08-02** — they would now
> fail with `FATAL: database "timesheet_dev" does not exist`. The transcript below was captured
> before the rename, so it shows the old name in its prompt output; the schema facts it demonstrates
> are unchanged.

Observed:

```
       List of schemas
  Name   |       Owner
---------+-------------------
 compass | timesheet
 public  | pg_database_owner
(2 rows)

 count
-------
     0

 table_schema |      table_name
--------------+-----------------------
 public       | __EFMigrationsHistory
(1 row)
```

Also replay the **cold** path (`make reset-db && make migrate`) and confirm a fresh clone lands in the
same state. An incremental apply working proves nothing about a first-time apply.

> **Two `psql` casing traps, both hit for real.**
> The history **table** name stays PascalCase (`__EFMigrationsHistory`) while its **columns** are
> snake_cased. Querying `"MigrationId"` fails:
> ```
> ERROR:  column "MigrationId" does not exist
> HINT:  Perhaps you meant to reference the column "__EFMigrationsHistory.migration_id".
> ```
> Use `migration_id`, and match the table name case-insensitively (`ILIKE`).

---

## 3. Explicit table-and-schema mapping — PROVEN, and Compass is the worked example

> **This section previously read "unverified" and told the next module to run the experiment
> itself.** That was true when it was written (Compass owned no tables yet) and is stale now — PR
> #181 (2026-08-06) mapped Compass's seven entities to the `compass` schema, and the outcome is
> checked into this repository as passing tests, not as an open question. Updated 2026-08-11 while
> auditing spec 002's docs tasks; the correction itself predates spec 002 (the mapping shipped
> before that spec's stream started), so don't read this as something spec 002's US1/US6 work
> discovered — it is a pre-existing doc/code drift this pass found and closed.

**An EF entity CAN be mapped to an explicit table-and-schema, alongside
`UseSnakeCaseNamingConvention()`, without a fight.** `api/Modules/Compass/Data/Configurations/CompassEntityConfiguration.cs`
is an abstract base that every one of Compass's seven entity configurations derives from. It calls

```csharp
builder.ToTable(TableName, Schema, ConfigureTable);
```

once, and columns still come back snake_case — verified against the **live catalog**, not the
generated migration, by `CompassSchemaFromErdTests.CompassColumns_AreSnakeCase` (`tests/integration/Compass/`).
This is Option 2: the
entity is reached with `context.Set<T>()`, not a named `DbSet` on `LeapDbContext`, so the Platform
layer stays ignorant of the module. It is still ONE context, ONE `__EFMigrationsHistory` in
`public`, and ONE `SaveChangesAsync` boundary.

**If you are adding a module's first entity, copy `CompassEntityConfiguration<T>` rather than
re-running the spike:**

1. Write an abstract base under your module's `Data/Configurations/` that calls
   `ToTable(TableName, Schema, ConfigureTable)` from one place — repeating the schema string per
   entity is one typo away from silently creating a table in `public` instead.
2. Have each entity's configuration derive from that base and declare its table name and columns
   explicitly (see point 3 below for why columns are explicit too).
3. Generate the migration:
   ```bash
   dotnet ef migrations add <Name> --project api \
     --output-dir Platform/Data/Migrations --context LeapDbContext
   ```
4. **Apply it to a local Docker Postgres** — reading the generated C# is not verification:
   ```bash
   docker compose up -d postgres
   make migrate
   ```
5. **Inspect the LIVE catalog:**
   ```bash
   docker compose exec -T postgres psql -U timesheet -d leap_dev -c '\dn'
   docker compose exec -T postgres psql -U timesheet -d leap_dev -c '\d <your_schema>.*'
   ```
   Confirm against the **live database** — not the migration file — that the schema exists, the
   table is in it, and the table and column names are what you intended. Compass's own columns are
   named **explicitly** rather than left to the naming convention, because the ERD is a published
   contract other streams build against, and the convention's handling of trailing digits is not
   obvious (`CanSubmitUnder40` does not reliably yield `can_submit_under_40`).
6. Re-run the integration suite and confirm the reset path still works:
   ```bash
   dotnet test --project tests/integration
   ```
   `IntegrationTestBase`'s `TRUNCATE` is **schema-qualified** specifically because of this: an
   unqualified table name resolves through `search_path`, so a table outside `public` would
   silently not be truncated, leaving rows behind and producing failures in unrelated tests.
7. Prove the cross-schema property with a join over **real** tables, the way
   `tests/integration/Compass/CompassSchemaIsANamespaceTests.cs` does — a probe table created and
   rolled back inside a transaction was the placeholder this repository used before any module
   owned real tables; once yours exist, join two of them instead.

**Still unproven: deployed behaviour of a schema-mapped entity.** Everything above is local Docker
Postgres and ephemeral Testcontainers. Local green proves nothing about a deployed environment — that
is the standing lesson of this project.

---

## 4. Model-snapshot merge conflicts

With several developers on one shared model, two people adding migrations concurrently **will** collide
in `LeapDbContextModelSnapshot.cs`. This is routine EF friction, but the recovery has an order.

**Do not hand-merge the snapshot.** It is generated code describing the whole model; a plausible-looking
manual merge produces a snapshot that matches neither branch's reality, and every subsequent migration
is then diffed against that wrong baseline.

Recovery:

1. Take the merged model — i.e. resolve conflicts in the **entities and configurations**, which are
   hand-written and reviewable.
2. **Delete the later migration entirely** (both the migration file and its `.Designer.cs`), and check
   out `LeapDbContextModelSnapshot.cs` from the branch you merged *into* so the baseline is coherent.
3. **Regenerate** that migration against the merged model:
   ```bash
   dotnet ef migrations add <SameNameAsBefore> --project api \
     --output-dir Platform/Data/Migrations --context LeapDbContext
   ```
4. Re-verify against a live database — apply it and inspect the catalog, as in section 2.

**What a bad merge looks like downstream**, so you catch it early: the next `migrations add` you run
generates operations that make no sense — re-creating a table or column that already exists, or
dropping something nobody touched. That is the tell that the snapshot no longer describes the database.
Also, an `Up` that has already been applied to a shared database cannot be edited in place — add a new
migration instead.

---

## 5. The namespace-versus-type compiler trap

Referencing the `Timesheet` **entity** by its simple name from inside a sibling module fails to
compile. Measured, verbatim:

```
api/Modules/Compass/__cs0118_probe.cs(8,21): error CS0118:
'Timesheet' is a namespace but is used like a type
    1 Error(s)
```

**Cause.** C# name lookup walks up the enclosing namespaces. From
`LeadingEDJE.Leap.Api.Modules.Compass` it reaches `LeadingEDJE.Leap.Api.Modules`, finds the sibling
**namespace** `Timesheet`, and that shadows the same-named **type** imported by a `using` directive.

**The asymmetry that explains it:** the identical simple-name reference from *inside* the Timesheet
module compiles cleanly, because lookup finds the type in the current namespace before anything can
shadow it. That is why 65 such references live happily inside that module while the same text fails
from another.

**Two sanctioned fixes:**

- a file-scoped alias — the form already used in seven files:
  ```csharp
  using TimesheetEntity = LeadingEDJE.Leap.Api.Modules.Timesheet.Timesheet;
  ```
- or full qualification at the use site.

**An assembly-wide `global using` alias is NOT the fix.** It was measured: adding
`global using TimesheetEntity = …` produces **7 `CS1537` errors** against the pre-existing per-file
aliases of the same name.

> ### Never rename the entity class to dodge this
> Table names in this repository derive from the **DbSet property names** on the context, and there are
> **zero explicit table mappings**. Renaming an entity therefore **silently renames its table**, which
> means a migration nobody intended against a shared database.

**Scope of the trap today:** exactly **one** type in the whole API collides with a sibling module
namespace — the `Timesheet` entity. Nothing in `api/Modules/Compass/` needed an alias, because Compass
reads `Employee` and `Person`, which collide with nothing. **The trap is armed but currently
untriggered**, which is precisely why it is written down instead of rediscovered.

---

## 6. The module boundary is folders and namespaces

Not a separate database, not a separate context, not a separate deployment. **Folders and namespaces,
mirroring each other one-to-one.**

```
api/Modules/Compass/
  README.md
  CompassServiceCollectionExtensions.cs
  CompassAuthorizationExtensions.cs
  Contracts/IDirectory.cs               # published — the ONE segment a consumer OUTSIDE the
  Contracts/CompassEmployeeDto.cs       # module may reference (spec 013, #451); see §6a
  Dtos/CompassLookupRequest.cs          # internal DTOs that never cross the boundary stay here
  Interfaces/ICompassDirectoryRepository.cs
  Services/CompassDirectoryService.cs
  Data/Repositories/CompassDirectoryRepository.cs
  Data/Configurations/          # entity configurations, when the module has entities
  Endpoints/CompassEmployeeEndpoints.cs
```

Every namespace matches its directory path exactly. Copy the **complete** slice: entities, EF
configurations, interfaces, repositories, services, DTOs and endpoints all live under the module.

### 6a. `Contracts/` is a seventh folder, not a variant of `Dtos/`

A module that publishes an in-process contract for other modules to consume — the shape Compass's
`IDirectory` established — homes that contract's interface and its return DTOs in their own
`Contracts/` folder, separate from `Interfaces/` and `Dtos/`. This is not the original layout:
`IDirectory` and its DTOs lived in `Interfaces/`/`Dtos/` through features 001–009, and spec 013
(#451) relocated them once a second module needed to reference the contract from outside Compass
— naming a Compass namespace from a sibling module collided with the boundary rule below, which
forbids exactly that. `Contracts/` is the namespace segment the boundary gate exempts, and it is
kept a **flat** directory on purpose (no subdirectory): the exemption cannot distinguish a nested
namespace from a member access, so a subtree under `Contracts/` would be a side door. Full detail:
`specs/013-compass-contracts-seam/contracts/gate-contract.md`.

**Not every DTO under a module's boundary belongs in `Contracts/`.** Only the types reachable from
the published interface's own signatures do. A DTO that supports an admin or configuration
endpoint but is never returned by the in-process contract stays in `Dtos/`, exactly as before.

> **Do not use the out-of-office module as the structural template.** It landed as a *partial* slice:
> its own subfolder and namespace at the endpoint, service and DTO layers, but its entities went flat
> into the shared models folder, its EF configurations flat into the shared configurations folder, its
> interfaces flat into the shared interfaces folder, and it has **no repository layer at all** — its
> services touch the context directly. It is the cautionary note, not the pattern.

### The boundary rule

**A module reaches another module's data through an interface, never by injecting the context.**

> **This subsection described the boundary at its first-slice size — one method, a `Guid` id, one
> endpoint file.** That was true when it was written and is stale now: feature 009
> (`specs/009-compass-integration-boundary/`, closed 2026-08-25) grew `IDirectory` across all seven read
> families AC-NFR-1 names, the identifier became an `int` back in feature 003 (2026-08-10,
> `compass.employee.employee_id` — the `Guid` below was the legacy `public.employees` uuid key), and the
> HTTP transport is now **four endpoint files, eight routes, one route group per family** — not the
> single `CompassEmployeeEndpoints.cs` this section still shows in isolation below and in the §6 folder
> listing above.
>
> **The count in that correction was itself one iteration stale.** It said "eight methods … eight
> routes", a one-to-one that no longer holds: issue #430 took `IDirectory` to nine methods with eight
> routes, and feature 018 to **eleven methods with eight routes**. **So do not copy "every contract
> method gets a route" as the rule.** The rule is
> that a method served on one transport only is a DECISION, which has to be stated:
> `tests/unit/Architecture/CompassTransportParityTests.cs` enumerates the `Contracts/` seam and fails
> the build on a method that has neither a handler nor an allowlist entry carrying the reason. A new
> module that publishes an in-process contract should copy that gate along with the boundary.
>
> The **shape of the rule** (an interface, never the context; the same DTO on both transports where
> both exist; a build-enforced boundary) is unchanged and still exactly what to copy. For the *current*
> concrete example, read `api/Modules/Compass/Contracts/IDirectory.cs` directly and
> `specs/009-compass-integration-boundary/contracts/directory-boundary.md` (the seven-family table) and
> `contracts/boundary-authorization.md` (why each route group carries its own
> `.RequireAuthorization` rather than one shared gate — FR-017) rather than this narrative's original
> numbers.

The worked example is `api/Modules/Compass/Contracts/IDirectory.cs` — one method,
`GetEmployeeAsync(Guid id, CancellationToken)` — with `Services/CompassDirectoryService.cs` and
`Data/Repositories/CompassDirectoryRepository.cs` behind it. The endpoint handler depends on
`IDirectory` and **never touches the database context**; that is the habit the boundary exists to
prevent, and `grep` for context references in the Compass endpoint returns 0.

The identifier is a `Guid` because the directory record's key is a preserved uuid, and the route
constraint is `{id:guid}` so a non-uuid segment fails route binding before reaching the handler.

**(Historical, as of feature 001/002 — see the banner above for the current shape.)**

### The boundary is enforced by the build

`tests/unit/Architecture/CompassBoundaryTests.cs` makes the rule above mechanical rather than
aspirational. Seven tests, four rules:

1. **Nothing outside the module reaches into it.** No production type or file outside
   `LeadingEDJE.Leap.Api.Modules.Compass` may reference something inside it — except `api/Program.cs`,
   the composition root, which is the registration seam and the only sanctioned entry.
2. **The endpoint and service layers touch no data context.** They depend on the module's own
   interfaces. Data access lives in the repository layer, and nowhere else.
3. **The module's cross-boundary reads are exactly the allowlisted ones.** The scan looks for *any*
   sibling module — `Modules.<Anything-but-Compass>` — not just `Modules.Timesheet`, because reaching
   into OOTO is the same crossing wearing a different name. Three files legitimately cross, all of them
   into `Modules.Timesheet`, because the directory entities live there by owner decision:

   | File | Why it may cross |
   |---|---|
   | `Interfaces/ICompassDirectoryRepository.cs` | declares the read boundary in terms of the Timesheet-owned `Employee` |
   | `Data/Repositories/CompassDirectoryRepository.cs` | queries `Employee`/`Person` through the shared context — the only sanctioned place for data access |
   | `Services/CompassDirectoryService.cs` | projects `Person` to the module's own DTO in a private helper; touches no context, runs no query |

   The exception is bounded by **naming the files**, not by granting a general permission. A **fourth**
   Compass file acquiring a cross-module reference is a failing build, and an allowlist entry whose file
   stops crossing is *also* a failure, so the list cannot rot into permanent permission. When the
   directory relocation eventually happens, delete all three entries and the rule becomes absolute.
4. **The check cannot pass vacuously.** Every rule asserts a non-zero count of inspected files and
   types, and the repository root is resolved by walking up to `leap.slnx` and **throwing** if it is not
   found. A path-based check that resolves no files passes — that is the fail-open shape this repository
   has shipped repeatedly, so this one says so instead.

**What is deliberately out of scope, and must not be "fixed" on this check's account:** the
out-of-office module's direct employee read (**5** `.cs` files) and the platform layer's imports from
`Modules.Timesheet` (**63** `.cs` files) — both in §12 below. A
`AcceptedCrossModuleReads_AreCountedAndNotFlagged` positive control **counts** those reads, asserts the
count is greater than zero, asserts the specific named arrangement
(`api/Modules/Ooto/Services/OotoDirectoryService.cs`) is still among them, and asserts no rule flags
any of them. So the scoping is defended by a measurement rather than by an argument, and the control
fails loudly — telling you to widen the check — if those reads are ever refactored away, instead of
silently decaying into a tautology.

Each rule was proven to fire by inserting a deliberate violation and observing the failure, reverting
between each. What one looks like — this is rule three catching a Compass endpoint that reached into
OOTO, so it also demonstrates that the scan is not Timesheet-specific:

```
a Compass file reaches into a SIBLING MODULE without being on the named allowlist. The allowlist is
bounded on purpose: the directory entities live in the Timesheet module by owner decision, so exactly
the interface/repository/projection slice may cross, and only into Timesheet. Any other module is not
accepted at all. Route the read through ICompassDirectoryRepository, or -- if this genuinely is the
directory read -- add the file to AllowedTimesheetReads WITH a written reason.
  Offenders: api/Modules/Compass/Endpoints/CompassEmployeeEndpoints.cs -> Modules.Ooto
```

And what the non-vacuity guard looks like when the module folder moves without the check being updated
— note that it **throws**, taking four tests red, rather than quietly inspecting an empty set:

```
System.IO.DirectoryNotFoundException : The Compass boundary check found nothing to inspect:
'api/Modules/CompassMOVED' does not exist under repository root '/home/avery-quinn/leap'.
A path-based check that resolves no files PASSES, which is the fail-open shape this check exists to
avoid -- fix the path rather than letting the check run empty.
```

### One contract, two transports

The in-process interface and the versioned HTTP endpoint return the **same type**
(`Dtos/CompassEmployeeDto.cs`). That is deliberate: extracting the module into its own process later
becomes a **transport swap rather than a rewrite**. Do not introduce a second, endpoint-only response
shape — `tests/unit/Compass/CompassTransportContractTests.cs` reflects over both declared return types
and fails the build if they diverge. It was proven to fire: adding a second DTO produced

```
Compass has exactly one boundary contract; a second DTO means the transports have diverged:
  CompassEmployeeDto, CompassEmployeeSummaryDto
```

### Versioning

**Version your routes from the start:** `/api/compass/v1/employees/{id:guid}` — the original route
shape; it is `{id:int}` today (see the banner above §6's "The boundary rule"), but the versioning
lesson is about the `/v1` prefix, not the id type. The out-of-office
module's routes are unversioned because they inherited a legacy system's shapes and are now
contract-locked by an absorbed SPA. That was a **concession, not a convention** — do not copy it, and
do not "harmonise" those frozen routes with yours either.

Also note the deliberate difference in denial status: Compass gates with a **group-level policy**
(401 unauthenticated, 403 authenticated-without-role), while the out-of-office module gates
**in-handler and returns 401** for an under-privileged caller, for legacy parity. Use the policy path.

---

## 7. Registration and authorization: one extension each

A module registers itself in **two calls**:

```csharp
builder.Services.AddCompassServices();        // api/Modules/Compass/CompassServiceCollectionExtensions.cs
builder.Services.AddCompassAuthorization();   // api/Modules/Compass/CompassAuthorizationExtensions.cs
```

```csharp
app.MapCompassEmployeeEndpoints();
```

These were the **first** `IServiceCollection` extension methods in a composition root that previously
carried roughly 52 flat `AddScoped` calls and 18 inline policy definitions. **New modules follow the
extension form** — keep your registrations and policies inside your module, not scattered into the
composition root.

---

## 8. Roles: every module owns its own, and inherits nothing

**A module inherits no role from any other module — including root.** A timesheet SuperAdmin is *not*
a Compass admin, and a Compass Super Admin is *not* a timesheet root. Each module's root is root
**inside that module only**.

Compass's set, as an example of the shape:

| Policy | Accepts |
|---|---|
| `CompassSuperAdmin` | `Compass Super Admin` only |
| `CompassAdmin` | `Compass Admin`, `Compass Super Admin` |
| `CompassOps` | `Compass Ops`, `Compass Super Admin` |
| `CompassSales` | `Compass Sales`, `Compass Super Admin` |

Do **not** add a timesheet role to a module policy; it reverses an owner decision. The
out-of-office policies were the one place that had been done, and that exception was removed on
2026-09-10 — there is no longer a compound shape anywhere to copy.

### The direction that fails open

Most mistakes here fail **closed** — a group-name typo maps to nothing and grants nothing. Exactly one
direction fails **open**:

> **A module group mapped to another module's bare role string grants that role in the other module.**
> Mapping `Compass-Ops` to the bare string `Ops` grants **Ops in timesheet**. The nine bare
> strings (`EDJEr`, `Manager`, `TimesheetProcessor`, `Accounting`, `HR`, `Ops`, `PayrollProcessor`,
> `Admin`, `SuperAdmin`) belong to timesheet by history. Never map a module group to one of them.

Three guards exist because of that:

1. **`KnownRoles`** — a closed vocabulary (9 timesheet + 2 out-of-office + 4 Compass = 15).
2. **`GroupRoleMappingValidator`** — fail-fast **startup validation**. An unknown role string in
   configuration stops the application rather than logging and continuing.
3. **A denial matrix asserting both directions**, `tests/unit/Authorization/CompassRoleIsolationTests.cs`,
   driving the **real registered authorization service** rather than re-implementing the rule: 4 module
   roles × 20 other policies all denied, 11 other roles × 4 module policies all denied, **plus a
   positive control** of each module role against its own policy. The positive control is not optional —
   without it, a policy set that denied *everything* would pass both matrices and prove nothing.

It was proven to fire. Widening `CompassAdmin` to also accept `SuperAdmin` produced:

```
'SuperAdmin' SATISFIED Compass policy 'CompassAdmin'
Total: 7, Errors: 0, Failed: 1
```

### Group names are opaque configuration — copy them byte-exactly

Compass's groups are `Compass-SuperAdmin` / `Compass-SuperAdmin-dev`,
`Compass-Admin` / `Compass-Admin-dev`, `Compass-Ops` / `Compass-Ops-dev`,
`Compass-Sales` / `Compass-Sales-dev` — space-free like `Timesheet-Admin-dev`, verified from a
live Google SAML `Groups` attribute.

**Never rewrite them.** A group name is an opaque string from whoever owns the directory; a
mismatched name matches nothing and grants nothing, silently. A test reads the real
`api/appsettings.Development.json` and asserts the byte-exact strings; it was proven to fire when
`Compass-Ops` was rewritten as `Compass - Ops`.

Configuration lives in **three** surfaces that must agree: `api/appsettings.Development.json`, the Helm
values, and the mock IdP's user definitions.

### Scope is never inferred

Bootstrap root seeding, impersonation and the admin endpoints are **timesheet-scoped** and were
deliberately **not** extended to Compass. If a scope is not written down, **ask** — do not infer it.
One live consequence: with the Compass groups not yet created, there is currently **no path to a first
Compass root user** in a deployed environment. That is a fail-closed gap awaiting an owner decision,
not something to fix by widening a policy.

---

## 9. Front-end checklist

| Step | Detail |
|---|---|
| Workspace membership | Add `web/<module>` to the root `workspaces` array. The root `npm ci` then covers it — **do not** create a nested install. |
| Base path | `base: '/<module>/'` in `vite.config.ts`. This must agree with the nginx location prefix and the image's static directory. A mismatch in any one of the three serves a blank page. |
| Dev proxy | Add a `/<module>` entry to the timesheet SPA's Vite proxy pointing at your dev-server port, so **one browser origin** serves every SPA and the HttpOnly session cookie stays first-party — the property deployed nginx provides. |
| API base | `window.__API_BASE_URL__ || ''` — same-origin relative, so it works unchanged under a path mount. |
| Coverage | 98% tier, **zero exclusions**. If a file cannot reach it, write the test. |
| No bare `fetch` | Every API call goes through the module's `apiFetch` wrapper (`credentials: 'include'` plus 401 handling). A bare `fetch` drops the cookie cross-origin and skips re-login. |
| Routing | TanStack Router with **code-based** routes. Not the file-based form — see below. |

### Routing: code-based, and the coverage tier is why

Use `createRoute`/`createRouter` in a hand-written `routes/router.ts`. **Do not add
`@tanstack/router-plugin`.**

File-based routing emits a generated `routeTree.gen.ts`. A generated file cannot reach the 98% tier on
its own, so it needs a coverage exclusion — and the row above says there are none. The two constraints
collide, and the exclusion is the one that loses: Principle VI closes coverage gaps by writing the test,
and Principle II forbids the easy fix.

This is not hypothetical friction. Compass shipped its first three screens with **no router at all**
for exactly this reason, and `main.tsx` carried a comment saying so. Code-based routes resolve the
objection rather than overriding it: every route is an ordinary TypeScript module, importable and
unit-testable, and it counts toward the tier like any other file. Route resolution is then tested by
driving `createMemoryHistory` and asserting the matched route ids, which is a normal unit test.

**Features 004 and 005 reached this conclusion independently, on branches that never saw each other**,
and their routers merged without either having to be rewritten. Two teams deriving the same answer from
the same constraint is the strongest evidence available that it is the right one — so treat the choice as
settled rather than re-litigating it for the next module.

Two traps that cost time in that work, both cheap to avoid:

- **A literal path must be declared, and it must not be reachable as a parameter.** `/edjers/new` has to
  match its own route rather than `$edjerId`, or the add screen tries to *load* a record named "new" and
  renders a failure that reads like a backend problem. Assert it directly.
- **A `Link` mock in a unit test must interpolate params**, the way the router does. One that renders
  `to` verbatim makes `/edjers/$edjerId` look like a working href and hides a missing `params` prop
  entirely.

### `react-refresh/only-export-components` shapes your file layout

A `.tsx` module may export **components only**. A constant, a type-only helper or a class-name string
living beside a component fails lint, so it goes in a sibling `.ts` file. Compass has two of these
(`admin-sections.ts` beside `AdminLayout.tsx`, `ui-classes.ts` beside `ui.tsx`) and both exist for this
rule alone. Expect to need one; it is not a smell.

### The single-query-library-instance rule

`@tanstack/react-query` must resolve to **one** hoisted copy across the workspace. Two copies means
your `useQuery` cannot see the `QueryClientProvider` your own entry point mounted, and every page
throws "No QueryClient set".

**Unit tests pass in exactly that broken state** — they render with their own provider. Only a real
browser loading the real bundle observes it. So:

```bash
npm install && npm dedupe
npm ls @tanstack/react-query        # expect ONE version, deduped, no nested copy
```

and make sure your module has at least one **critical** end-to-end spec that loads the real page in a
browser. `web/timesheet/tests/e2e/compass.critical.spec.ts` exists for precisely this reason.

Also add your module to `make dev-all` (boot, readiness wait, and the URL in the summary block) and to
`make dev-down` (free its port — otherwise a later Playwright run silently reuses a stale server).

---

## 10. Routing checklist

**Two nginx configurations, maintained in lockstep:** `docker/nginx.conf` and
`deploy/docker/nginx.conf`. A change to one without the other is a latent production defect that no
local test catches. Diff your two blocks against each other and confirm they are equivalent.

Add the location **trio**, placed **before** the catch-all root location:

```nginx
location = /<module> {
    return 301 /<module>/$is_args$args;
}

location /<module>/ {
    add_header Cache-Control "no-store" always;
    try_files $uri /<module>/index.html$is_args$args;
}

location /<module>/assets/ {
    add_header Cache-Control "no-store" always;
}
```

And extend the regex location that 404s API-shaped paths nested under a module scope:

```nginx
location ~ ^/(ooto|timesheet|compass)/.+/(api|Auth|auth|Saml2)/ {
    return 404;
}
```

### Four rules, each earned the hard way

1. **The fallback must carry `$is_args$args`.** `try_files`' internal redirect **drops the query
   string**. Without it a query-parameterised deep link arrives with its arguments already gone. Proven
   by control: the shipped form yields `X-Debug-Args: [employeeId=42&mode=x]`, the bare form yields
   `X-Debug-Args: []`.
2. **No decision-making `if` in a location that relies on `try_files`.** Entering a TRUE `if` branch
   switches the request into nginx's implicit "if-location", which does **not** inherit `try_files`.
   Compute decisions with a top-level `map`; the only permitted `if` is one whose sole action is
   `return`. Reproduced deliberately: with a `set` inside an `if`, `/compass/` still returned **200**
   while `/compass/anything/deep` returned **404**.
3. **API-shaped paths nested under a module scope must 404.** When a build-time environment regression
   left a SPA with a base URL of the literal string `"undefined"`, it emitted relative
   `undefined/api/...` paths; the SPA fallback answered **200 + index.html**, the SPA read HTML where it
   expected JSON, treated it as a 401, redirected one level deeper, and looped hundreds of times. A hard
   404 fails fast instead.
4. **Caching is off** by owner decision: `no-store` on HTML entry points and hashed assets, plus
   `etag off`. `/sw.js` stays `no-cache`.

### Assert it over HTTP against the BUILT IMAGE

This is not optional, and a dev server is not a substitute:

```bash
docker build -f docker/web.Dockerfile --target prod -t leap-web-check .
docker run -d --name leap-web-check -p 8099:80 leap-web-check
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:8099/compass/
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:8099/compass/anything/deep
curl -s -o /dev/null -w '%{http_code}\n' 'http://localhost:8099/compass/deep/link?a=1'
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:8099/compass/x/api/v1/thing
curl -s -o /dev/null -D - http://localhost:8099/compass/ | grep -i cache-control
docker rm -f leap-web-check
```

> **A passing SPA root proves nothing about deep links.** `/compass/` is resolved by the `index`
> directive without ever consulting `try_files`. This exact failure has shipped: the whole test suite
> was green while every deployed deep link returned a raw nginx 404, because nobody had requested a
> path that does not exist on disk.

Then add the same assertions to the preview-smoke job so every future deploy checks them.

Finally, both `docker/web.Dockerfile` and `deploy/docker/web.Dockerfile` need your module's manifest
copied, its workspace added to the `npm ci` invocation, its source copied, its `npm run build` run, and
its `dist` copied into the image. Also in lockstep.

---

### A rejection your module relies on may be a 500 locally and a 400 everywhere else

*Learned in feature 004 US3 (2026-08-13), and it cost a debugging cycle.*

If your module needs a request to be **refused** rather than silently accepted — the usual case being a
field the caller must not be allowed to set, expressed with
`[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]` on the request record — then the
status code that refusal produces is **not the same in every environment by default**.

`RouteHandlerOptions.ThrowOnBadRequest` defaults to **`true` under Development** and **`false`
everywhere else**. So a body that cannot bind answers:

| Environment | Without the pin |
|---|---|
| Production | `400` |
| The `WebApplicationFactory` test host | `400` |
| Your local Development API | unhandled `BadHttpRequestException` → **`500`** |

The trap is the direction of the disagreement: the unit endpoint test asserts `400` and **passes**, and
only a real browser against a real local API shows the `500`. Compass found it in a Playwright spec, not
in the suite that was written to cover exactly that rule.

`api/Program.cs` now pins it for the whole application:

```csharp
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
```

Nothing is lost — the binding failure is still logged in full, it simply stops escaping as a server
error. **You inherit this**; the entry is here so that if someone ever removes that line, the symptom
("my 400 test passes but the E2E sees a 500") is searchable.

## 11. Gate checklist

Add your module to every path-scoped gate. **This trimmed workshop copy carries no `.github/`,
`.husky/` or `.claude` directory** — the CI workflows, git hooks and agent tooling this section
describes were part of the original monorepo's deploy/CI surface and did not travel with the
Compass-only trim. The list below is kept as the recipe for a real deployment of this app; treat
every row whose path doesn't exist in this copy as historical rather than something to go looking
for here.

| Gate | What to add |
|---|---|
| `scripts/check-coverage.sh` | your app root in the front-end app list and its path pattern |
| `.husky/pre-push` | change detection plus a block running your lint and build; put your **test** run behind `RUN_TESTS` like the others (see below) |
| root `package.json` lint-staged | a `web/<module>/**` glob |
| `.github/workflows/ci.yml` | lint, build and coverage steps, plus an assertion that coverage was **measured** |
| `.github/workflows/deploy-aws-preview.yml` | the smoke assertions from section 10 |
| a workflow dependency map, updated in the same commit as any workflow change | kept in the original monorepo at `docs/ops/github-actions-map.md`; not present in this trimmed copy |

**Local test runs are opt-in in a real deployment.** In the original monorepo, git hooks ran builds
and lint on every push but skipped the unit suite, the integration suite, the Compass Vitest run,
`scripts/check-coverage.sh` and Playwright unless a test flag was set explicitly. That narrowing is
safe only because CI runs every one of those suites on the same commit and blocks the merge — making
CI load-bearing for a module in a way local hooks alone are not. Getting your suite into the CI
workflow (the row above) is therefore not a nice-to-have you can defer to a follow-up PR — until it is
there, and reporting, **your tests run nowhere.**

> ### A path regex that matches zero files does not fail. It PASSES.
> This is the single most important sentence in this document. Before Compass was added to
> `scripts/check-coverage.sh`, a changed Compass file matched **nothing**, and the coverage gate
> **passed**. It was not protecting anything.
>
> Across the two phases that produced this module, **eight** gates were found silently fail-open or
> blind: a hook that never fired because of a missing `require`; a pre-push line whose `|| echo`
> turned any failure into a pass; a coverage settings file that was invalid XML and aborted every run
> for five waves; a missing coverage reporter that reported a frontend line rate of `0` on every pull
> request for the entire life of that SPA; a build pattern that never matched a front-end directory; an
> agent hook running the wrong app's checks under an unmeetable timeout; a load-dependent flake; and a
> lockfile that CI's npm rejected while the local npm accepted it.

**So: require a fire-observation per gate.** For each gate you touch, introduce a deliberate violation,
record the verbatim failure output and exit code, then revert and confirm green. Both phases did this
in an audit table — the Phase 49 gate audit, in git history at `archive/planning-2026-09-04`, is the format. A gate must prove it
**executed**, not merely that it did not complain.

One more: **the local toolchain is not the authority.** A lockfile that `npm ci` accepts locally can be
rejected by CI's npm, and CI is the one that matters. Check `engines` in `package.json` and use a
matching node version.

---

## 12. Known divergences — surfaced, not silently fixed

These are stated as facts with their status so nobody "tidies" one into a regression.

### The platform layer imports from one module's namespace

The platform code imports from `Modules.Timesheet` because the concrete repositories landed there
during the restructure. Defensible where it stands — but **not a precedent for a new module.** Your
module's platform-facing surface should be its own interfaces.

**37 platform files** cross this way. The Compass boundary check (§6) deliberately does not evaluate
them, and its positive control counts them so that scoping stays honest. **Do not refactor them on the
check's account.**

### The out-of-office module reads an employee entity directly out of another module

`OotoDirectoryService` injects the context and queries employees directly — exactly what the boundary
in section 6 forbids. **This is accepted and deliberate. Do not refactor it.**

Consequence for tooling: an architecture test enforcing the boundary must be **scoped so it does not
flag this**. A gate that is red the moment it is written is noise, and noise is how gates get ignored.

That is exactly how `tests/unit/Architecture/CompassBoundaryTests.cs` is scoped (§6). **5 out-of-office
files** cross into `Modules.Timesheet`; the check counts them and asserts it flags none of them.
Widening the check to cover this arrangement is a decision for *after* the directory relocation, not a
tidy-up.

### Roles are stored, though the intended design says they never are

The intended design is that roles are never persisted — they are derived at sign-in from directory
group membership. The implementation keeps a `user_roles` table as an **additive fallback**, and that
fallback is live. This is a design-versus-code gap in the design's favour. **It is flagged here, not
resolved.** Be aware that a role can come from that table as well as from a group.

---

## Appendix: the order it was actually done in

1. Empty schema migration on the existing context, verified against a live database (§1, §2).
2. Proof that the schema is a namespace, not a boundary (§1).
3. The module folder slice, the one-method boundary interface, and the compiler-trap measurement (§5, §6).
4. Roles, policies, the closed vocabulary, startup validation, and the two-direction denial matrix (§8).
5. The versioned endpoint, the registration extensions, and the one-contract test (§6, §7).
6. The front-end, workspace membership, and the query-library check (§9).
7. Gate plumbing plus a fire-observation for every gate (§11).
8. Routing in both nginx configurations and both images, asserted over HTTP against the built image (§10).
9. CI steps and the real preview-smoke assertions (§11).
10. A dedicated seeded test identity, dev-server plumbing, and a browser proof of the whole path plus
    both denial directions (§8, §9).
11. This document.
