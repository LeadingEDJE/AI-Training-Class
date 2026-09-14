# Compass entity configurations

**Compass owns 7 entities, each configured here.** `Employee`, `Client`, `ClientAssignment`, `Sow`,
`EmployeeType`, `InvoiceFrequencyType` and `BillableTimeCategory`, mapped to the `compass` schema by
`CompassEntityConfiguration<T>`.

*This file previously read "Compass owns ZERO entities today. This folder is intentionally empty." That
was true until PR #181 (2026-08-06). The folder existed so the module's shape was complete in git and
the first entity had an obvious home rather than being scattered into a shared folder — which is what
happened to the OOTO module and is the compromise Compass exists to avoid repeating. It served that
purpose; the guidance below is what survives it.*

## The one thing to know before adding an entity

`compass.employee` has a **functional unique index that is NOT in the EF model** —
`ux_employee_email_ci` on `lower(btrim(email))`, created as raw SQL by
`AddCompassEmployeeCaseInsensitiveEmailIndex`. EF Core's `HasIndex` takes properties, not expressions,
so it cannot be declared in `EmployeeConfiguration` at all, and re-adding a plain
`HasIndex(x => x.Email).IsUnique()` would ask the database for a second, weaker index on the same
column. The configuration says so in place. `CompassEmployeeEmailUniquenessTests` verifies the real one
against the live catalog.

## Adding an entity

The entity's `IEntityTypeConfiguration<T>` belongs **here**, and it must map the entity to the
`compass` schema.

**✅ This is now solved — inherit `CompassEntityConfiguration<T>` and you get it for free.** That
abstract base calls `ToTable(TableName, "compass", ...)` once for every Compass entity, and it is the
only `ToTable` call site in the repository. Columns still come back snake_case under the explicit
mapping; `CompassSchemaFromErdTests.CompassColumns_AreSnakeCase` asserts it against the **live
catalog** on every test run, so the question this README used to open with is answered by a standing
test rather than a one-off spike.

The original caution is kept below because it still applies to a **new module's first** schema mapping,
where no base class exists yet — and because the general rule holds: **do not trust the generated
migration file**, read the catalog. Verify against a live database:

```bash
docker compose up -d postgres

dotnet ef migrations add <Name> \
  --project api --output-dir Platform/Data/Migrations --context LeapDbContext

make migrate    # MUST succeed

# Inspect the LIVE catalog — reading the generated C# is not verification.
# $POSTGRES_DB is expanded INSIDE the container, where compose has set it, so this
# stays correct whatever `.env` names the database (`leap_dev` today). Do not
# hardcode the name here — the previous `timesheet_dev` was dropped on 2026-08-02.
docker compose exec -T postgres sh -c 'psql -U timesheet -d "$POSTGRES_DB" -c "\dn"'
docker compose exec -T postgres sh -c 'psql -U timesheet -d "$POSTGRES_DB" -c "\d compass.*"'
```

Confirm against the live database that the schema exists, the table is in `compass`, and the table and
column names are snake_case.

Then re-run the integration suite:

```bash
dotnet test --project tests/integration
```

The integration reset path already emits schema-qualified identifiers specifically so this step does
not break — but verify rather than assume.

Finally, delete or rewrite `tests/integration/Compass/CompassSchemaIsANamespaceTests.cs`. It proves the
cross-schema property with a throwaway probe table because no real Compass table existed; a join
between two real tables is strictly better evidence.

## Concurrent migrations

With several developers on one `LeapDbContext`, two people adding migrations at the same time will hit
a `LeapDbContextModelSnapshot` merge conflict. This is routine EF friction, not a sign that the single
context was the wrong choice. Recovery: take the other side's snapshot, delete your own migration,
re-run `dotnet ef migrations add` against the merged model, and commit the regenerated pair.
