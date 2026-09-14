-- Synthetic volume generator for the populated-database migration rehearsal (issue #211, item D1).
--
-- WHY THIS EXISTS
--   Every quality gate in this repository migrates a freshly-created EMPTY database: the integration
--   suite builds one per Testcontainers run, `make e2e-stack-up` drops and recreates, and CI applies
--   the idempotent script to a virgin compose database. A deployed upgrade migrates a POPULATED one.
--   A migration that is only invalid in the presence of rows -- an ALTER that rewrites a large table
--   and holds ACCESS EXCLUSIVE for minutes, a NOT NULL added without a default, a CHECK or UNIQUE
--   that existing rows violate, a narrowed type -- passes every gate and fails the deploy.
--   No test can catch this for you. This is the harness that changes that.
--
-- WHY IT IS GENERIC RATHER THAN A HAND-WRITTEN FIXTURE
--   A fixture with hard-coded column lists is written against ONE schema version. The rehearsal
--   deliberately seeds at the BASELINE schema (the version currently deployed), not at HEAD, so a
--   hard-coded fixture would break every time a migration touched one of its tables -- and it would
--   break by silently inserting nothing, which is the worst possible failure for a gate. So this
--   discovers the columns it must fill from the live catalog at run time, and fails loudly when it
--   cannot.
--
-- WHAT IT FILLS
--   Every NOT NULL column that has no default and is not an identity column. Nullable columns are
--   left NULL on purpose: they cost insert time without changing what the rehearsal measures.
--   Foreign keys are satisfied automatically from rows already present in the parent table, so the
--   call order at the bottom of this file is a dependency order and must stay one.
--
-- SAFETY
--   Everything lives in the `rehearsal` schema and every generated value is visibly synthetic
--   ('synthetic-<n>'). This script must NEVER be run against a real database -- the driver script
--   (scripts/rehearse-migration.sh) only ever points it at a throwaway container it created itself.

\set ON_ERROR_STOP on

CREATE SCHEMA IF NOT EXISTS rehearsal;

-- Returns a SQL expression producing a synthetic value for one column, given its catalog row.
-- `g.i` is the generate_series counter in scope at the call site.
CREATE OR REPLACE FUNCTION rehearsal.value_expr(
    p_data_type text,
    p_max_len   integer
) RETURNS text LANGUAGE sql IMMUTABLE AS $$
    -- NOTE: single `%` on purpose. These strings are returned verbatim and spliced into the INSERT
    -- via format('%s', ...), which does NOT reprocess the substituted value -- so a `%%` here would
    -- survive into the SQL as a literal `%%` and fail with "operator does not exist: integer %%
    -- integer". Only the format() TEMPLATES below need doubling.
    SELECT CASE p_data_type
        WHEN 'integer'                     THEN '(g.i % 100000)::integer'
        WHEN 'bigint'                      THEN 'g.i::bigint'
        WHEN 'smallint'                    THEN '(g.i % 1000)::smallint'
        WHEN 'numeric'                     THEN 'round((random() * 8)::numeric, 2)'
        WHEN 'double precision'            THEN '(random() * 8)::double precision'
        WHEN 'real'                        THEN '(random() * 8)::real'
        WHEN 'boolean'                     THEN 'false'
        WHEN 'uuid'                        THEN 'gen_random_uuid()'
        WHEN 'jsonb'                       THEN '''{}''::jsonb'
        WHEN 'json'                        THEN '''{}''::json'
        WHEN 'date'                        THEN '(CURRENT_DATE - (g.i % 900))'
        WHEN 'timestamp without time zone' THEN '(LOCALTIMESTAMP - ((g.i % 900) * INTERVAL ''1 day''))'
        WHEN 'timestamp with time zone'    THEN '(now() - ((g.i % 900) * INTERVAL ''1 day''))'
        WHEN 'interval'                    THEN 'INTERVAL ''1 hour'''
        WHEN 'bytea'                       THEN '''\x00''::bytea'
        WHEN 'character varying'           THEN
            CASE WHEN p_max_len IS NULL THEN '(''synthetic-'' || g.i)'
                 ELSE format('left(''synthetic-'' || g.i, %s)', p_max_len) END
        WHEN 'character'                   THEN
            CASE WHEN p_max_len IS NULL THEN '(''synthetic-'' || g.i)'
                 ELSE format('left(''synthetic-'' || g.i, %s)', p_max_len) END
        WHEN 'text'                        THEN '(''synthetic-'' || g.i)'
        ELSE NULL
    END;
$$;

-- Resolves which schema a table currently lives in.
--
-- WHY THIS IS NECESSARY AND NOT OVER-ENGINEERING
--   The rehearsal seeds at the BASELINE schema, and one of the migrations in this project's own
--   history (20260810202407_MoveModuleTablesToSchemas) MOVES thirteen timesheet tables from `public`
--   to `timesheet`. So the schema a table lives in is a function of the baseline you chose, and a
--   hard-coded schema in the call list below is wrong for every baseline on the other side of that
--   move. Resolving it here makes the call list baseline-independent.
--
--   `relkind = 'r'` matters: that migration also leaves an automatically-updatable COMPATIBILITY VIEW
--   behind at the old `public` location, so after the move a name resolves to BOTH a view in `public`
--   and a table in `timesheet`. Seeding through the view would work, but it would report the wrong
--   location and mask which object is real. Ordinary tables only.
CREATE OR REPLACE FUNCTION rehearsal.locate(p_table text) RETURNS text LANGUAGE sql STABLE AS $$
    SELECT n.nspname
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relname = p_table
      AND c.relkind = 'r'
      AND n.nspname IN ('public', 'timesheet', 'ooto', 'compass')
    ORDER BY CASE n.nspname WHEN 'public' THEN 1 ELSE 0 END   -- prefer a module schema
    LIMIT 1;
$$;

-- Schema-resolving wrapper. This is what the call list uses.
--
-- p_overrides maps a column name to a raw SQL expression, for columns a generic type-driven value
-- cannot satisfy. That is not a hypothetical: the Compass tables carry CHECK constraints asserting a
-- closed value set -- `employee.state_of_residence IN (<US state codes>)` and
-- `sow.sow_type IN ('InitialContract','SowExtension','LegacyMigrated')` -- and 'synthetic-1' violates
-- both. An override is the honest fix; loosening the value generator would make it lie about the
-- column's real domain.
CREATE OR REPLACE FUNCTION rehearsal.fill_auto(
    p_table     text,
    p_rows      integer,
    p_overrides jsonb DEFAULT '{}'::jsonb
)
RETURNS bigint LANGUAGE plpgsql AS $$
DECLARE
    resolved text := rehearsal.locate(p_table);
BEGIN
    IF resolved IS NULL THEN
        RAISE EXCEPTION
            'rehearsal.fill_auto: no ordinary table named "%" in public/timesheet/ooto/compass at this '
            'schema version. The rehearsal seeds at the BASELINE schema, so a table created by a '
            'PENDING migration is legitimately absent -- remove it from the call list, or move the '
            'baseline forward.', p_table;
    END IF;
    RETURN rehearsal.fill(resolved, p_table, p_rows, p_overrides);
END;
$$;

-- Fills p_rows synthetic rows into p_schema.p_table. Returns the number of rows inserted.
CREATE OR REPLACE FUNCTION rehearsal.fill(
    p_schema    text,
    p_table     text,
    p_rows      integer,
    p_overrides jsonb DEFAULT '{}'::jsonb
) RETURNS bigint LANGUAGE plpgsql AS $$
DECLARE
    col          record;
    fk_ref       record;
    col_list     text[] := '{}';
    val_list     text[] := '{}';
    expr         text;
    parent_ids   text;
    parent_count integer;
    stmt         text;
    inserted     bigint;
BEGIN
    IF to_regclass(format('%I.%I', p_schema, p_table)) IS NULL THEN
        RAISE EXCEPTION
            'rehearsal.fill: relation %.% does not exist at this schema version. The rehearsal seeds '
            'at the BASELINE schema, so a table added by a PENDING migration is legitimately absent -- '
            'remove it from the call list, or move the baseline forward.', p_schema, p_table;
    END IF;

    FOR col IN
        SELECT c.column_name, c.data_type, c.character_maximum_length
        FROM information_schema.columns c
        WHERE c.table_schema = p_schema
          AND c.table_name   = p_table
          AND c.is_nullable  = 'NO'
          AND c.column_default IS NULL
          AND c.is_identity   = 'NO'
          AND c.is_generated  = 'NEVER'
        ORDER BY c.ordinal_position
    LOOP
        -- An explicit override wins over everything, including FK resolution: a caller that names a
        -- column has more context than this function can derive.
        IF p_overrides ? col.column_name THEN
            col_list := col_list || format('%I', col.column_name);
            val_list := val_list || (p_overrides ->> col.column_name);
            CONTINUE;
        END IF;

        -- Is this column the referencing side of a foreign key? If so, draw values from the parent
        -- rather than synthesizing one, so the insert does not violate the constraint.
        --
        -- COMPOSITE KEYS ARE HANDLED, NOT SKIPPED. The first version matched only `conkey[1]` and
        -- filtered on `array_length(conkey, 1) = 1`, so a column in a MULTI-COLUMN foreign key fell
        -- through to a generic synthetic value and the insert died on a foreign-key violation whose
        -- message said nothing about why. There is no composite foreign key in this schema today
        -- (measured: 27 single-column, 0 composite), so this is a latent path -- but it is one a
        -- single migration could introduce, and the failure would land on whoever added it rather
        -- than on whoever wrote this.
        --
        -- `src_att.attnum = ANY(con.conkey)` matches the column WHEREVER it sits in the key, and
        -- `array_position` pairs it with the referenced column at the SAME position, so a two-column
        -- key maps col1 -> ref1 and col2 -> ref2 rather than both to ref1.
        --
        -- ref_order_cols is what keeps a composite draw VALID. Each column of the key is filled by a
        -- separate pass through this loop, so they must agree on which parent ROW each generated row
        -- points at -- otherwise column 1 comes from parent row 3 and column 2 from parent row 7 and
        -- the tuple exists in neither. Every pass orders the parent by the full referenced-column
        -- list and takes the same slice, so index k is the same parent row for every column.
        SELECT rel_ns.nspname AS ref_schema,
               rel_cl.relname AS ref_table,
               att.attname    AS ref_column,
               (SELECT string_agg(quote_ident(a2.attname), ', ' ORDER BY k.ord)
                  FROM unnest(con.confkey) WITH ORDINALITY AS k(attnum, ord)
                  JOIN pg_attribute a2 ON a2.attrelid = con.confrelid
                                      AND a2.attnum   = k.attnum) AS ref_order_cols,
               array_length(con.conkey, 1) AS key_width
          INTO fk_ref
        FROM pg_constraint con
        JOIN pg_class      src_cl ON src_cl.oid = con.conrelid
        JOIN pg_namespace  src_ns ON src_ns.oid = src_cl.relnamespace
        JOIN pg_class      rel_cl ON rel_cl.oid = con.confrelid
        JOIN pg_namespace  rel_ns ON rel_ns.oid = rel_cl.relnamespace
        JOIN pg_attribute  src_att ON src_att.attrelid = con.conrelid
                                  AND src_att.attnum   = ANY(con.conkey)
        JOIN pg_attribute  att     ON att.attrelid = con.confrelid
                                  AND att.attnum   = con.confkey[array_position(con.conkey, src_att.attnum)]
        WHERE con.contype  = 'f'
          AND src_ns.nspname = p_schema
          AND src_cl.relname = p_table
          AND src_att.attname = col.column_name
        LIMIT 1;

        IF fk_ref.ref_table IS NOT NULL THEN
            EXECUTE format(
                'SELECT count(*), coalesce(array_agg(v ORDER BY ord)::text, ''{}'') '
                'FROM (SELECT %I AS v, row_number() OVER (ORDER BY %s) AS ord '
                '      FROM %I.%I ORDER BY %s LIMIT 500) s',
                fk_ref.ref_column, fk_ref.ref_order_cols,
                fk_ref.ref_schema, fk_ref.ref_table, fk_ref.ref_order_cols)
            INTO parent_count, parent_ids;

            IF parent_count = 0 THEN
                RAISE EXCEPTION
                    'rehearsal.fill: %.%.% references %.%, which is EMPTY. Seed the parent first -- '
                    'the call list at the bottom of seed-volume.sql is a dependency order.',
                    p_schema, p_table, col.column_name, fk_ref.ref_schema, fk_ref.ref_table;
            END IF;

            expr := format('(%L::text[])[1 + (g.i %% %s)]::%s',
                           parent_ids, parent_count,
                           format_type(
                               (SELECT atttypid FROM pg_attribute
                                WHERE attrelid = format('%I.%I', p_schema, p_table)::regclass
                                  AND attname  = col.column_name), NULL));
        ELSE
            expr := rehearsal.value_expr(col.data_type, col.character_maximum_length);

            IF expr IS NULL THEN
                RAISE EXCEPTION
                    'rehearsal.fill: no synthetic value known for %.%.% of type "%". Add a branch to '
                    'rehearsal.value_expr -- do NOT skip the column, because a NOT NULL column left '
                    'unfilled makes this whole insert fail and a rehearsal that inserts nothing is '
                    'worse than no rehearsal.',
                    p_schema, p_table, col.column_name, col.data_type;
            END IF;
        END IF;

        col_list := col_list || format('%I', col.column_name);
        val_list := val_list || expr;
    END LOOP;

    IF array_length(col_list, 1) IS NULL THEN
        -- Every column is nullable, defaulted or generated. A bare row is still a row.
        stmt := format('INSERT INTO %I.%I SELECT FROM generate_series(1, %s) AS g(i)',
                       p_schema, p_table, p_rows);
    ELSE
        stmt := format('INSERT INTO %I.%I (%s) SELECT %s FROM generate_series(1, %s) AS g(i)',
                       p_schema, p_table,
                       array_to_string(col_list, ', '),
                       array_to_string(val_list, ', '),
                       p_rows);
    END IF;

    EXECUTE stmt;
    GET DIAGNOSTICS inserted = ROW_COUNT;

    RAISE NOTICE 'seeded % rows into %.%', inserted, p_schema, p_table;
    RETURN inserted;
END;
$$;

-- ── Call list ───────────────────────────────────────────────────────────────────────────────────
-- DEPENDENCY ORDER: parents before children. Row counts are chosen so the tables that dominate a
-- real ALTER's rewrite time dominate here too -- time_entries is by far the largest table in the
-- schema (one row per category per timesheet per week), timesheets next, then the audit log.
--
-- Scale with :rows from the driver (`-v rows=...`); the multipliers below keep the ratios sane at
-- any scale. Reference data (time_categories, timesheet_periods) is normally written by the
-- application's ReferenceDataSeeder, which does not run here, so it is seeded thinly to give the
-- foreign keys something to point at.
\if :{?rows}
\else
  \set rows 100000
\endif

BEGIN;

-- psql does NOT substitute `:vars` inside a dollar-quoted body, so the row count is handed to the
-- DO block through a session setting instead of interpolated into it.
\o /dev/null
SELECT set_config('rehearsal.rows', :'rows', false);
\o

DO $seed$
DECLARE
    n integer := current_setting('rehearsal.rows')::integer;
BEGIN
    -- Schemas are RESOLVED, not named -- see rehearsal.locate above. `timesheets`, `time_entries`,
    -- `time_categories` and `timesheet_periods` live in `public` before
    -- 20260810202407_MoveModuleTablesToSchemas and in `timesheet` after it, and the rehearsal must
    -- work from a baseline on either side.
    --
    -- Reference / parent data, thin. `timesheet_periods` is here because `timesheets.week_end_date`
    -- is a FOREIGN KEY to it -- found by rehearsal.fill's own FK detection on the first real run,
    -- not by reading the model. Its primary key is the week-ending date, so the row count must stay
    -- well under the 900-day spread rehearsal.value_expr generates for a `date`, or the generated
    -- values collide on the PK.
    PERFORM rehearsal.fill_auto('timesheet_periods', 200);
    PERFORM rehearsal.fill_auto('time_categories',   25);
    PERFORM rehearsal.fill_auto('people',            GREATEST(n / 500, 50));

    -- Volume tables.
    PERFORM rehearsal.fill_auto('timesheets',   GREATEST(n / 10, 100));
    PERFORM rehearsal.fill_auto('time_entries', n);
    PERFORM rehearsal.fill_auto('audit_logs',   GREATEST(n / 10, 100));

    -- ── Compass (the `compass` schema, 7 tables) ────────────────────────────────────────────────
    -- Added because the first version of this fixture touched NONE of them, so every Compass table
    -- was rehearsed at zero rows and any migration against one would have passed vacuously --
    -- exactly the empty-database blind spot this harness exists to remove, reintroduced inside the
    -- harness itself. Compass is the active development area, so this was the likeliest gap to bite.
    --
    -- The dev environment's CompassDirectorySeeder is NOT reused here: it is application code
    -- compiled from HEAD, and the rehearsal seeds at the BASELINE schema. It also tops out at 97
    -- employees / 28 clients, which is far too thin to measure an ALTER's rewrite time.
    --
    -- Volumes are proportional but modest -- Compass is a directory, not a transaction log, so its
    -- realistic worst case is thousands of rows, not the hundreds of thousands `time_entries` sees.
    --
    -- Lookup tables first (no FKs), then client/employee, then client_assignment, then sow.
    -- NOTE THE ORDER OF THE LAST TWO: `sow.client_assignment_id` is an FK to `client_assignment`,
    -- so the assignment is the PARENT. The reverse reads more natural -- a SOW sounds like it comes
    -- before the assignment it governs -- and the first draft of this list had it backwards.
    -- rehearsal.fill's FK detection caught it and named the direction.
    PERFORM rehearsal.fill_auto('employee_type',           10);
    PERFORM rehearsal.fill_auto('invoice_frequency_type',  6);
    PERFORM rehearsal.fill_auto('client',                  GREATEST(n / 500, 50));

    -- state_of_residence is CHECK-constrained to the US state codes, so the generic
    -- 'synthetic-<n>' would be rejected. Overriding the column is honest; loosening the value
    -- generator would make it misrepresent the column's real domain.
    PERFORM rehearsal.fill_auto('employee', GREATEST(n / 200, 100),
                                '{"state_of_residence": "''OH''"}'::jsonb);

    PERFORM rehearsal.fill_auto('billable_time_category',  GREATEST(n / 500, 50));

    PERFORM rehearsal.fill_auto('client_assignment',       GREATEST(n / 100, 200));

    -- sow_type is CHECK-constrained to three names. 'InitialContract' also satisfies the other two
    -- constraints on this table: ck_sow_rate_increase_requires_extension holds because the generic
    -- boolean value is false, and ck_sow_end_on_or_after_start holds because both date columns get
    -- the same generated expression.
    PERFORM rehearsal.fill_auto('sow', GREATEST(n / 200, 100),
                                '{"sow_type": "''InitialContract''"}'::jsonb);

    -- OOTO (the `ooto` schema, 2 tables) ---------------------------------------------------------
    -- Added for the same reason the Compass block above was: neither OOTO table was touched here,
    -- so a migration against one was rehearsed at ZERO rows and passed vacuously. That is the blind
    -- spot this harness exists to remove, and it was still open inside the harness for this schema.
    --
    -- `public.employees` comes first and is seeded here rather than with the timesheet tables above,
    -- because OOTO is the only thing in this fixture that needs it: out_of_office_events.employee_id
    -- is a real cascading FK onto it, and rehearsal.fill raises when an FK parent is empty. It hangs
    -- off `people`, which the timesheet block already seeded.
    --
    -- Order: employees, then the type lookup, then the events that reference both.
    PERFORM rehearsal.fill_auto('employees', GREATEST(n / 200, 100));
    PERFORM rehearsal.fill_auto('out_of_office_types', 10);

    -- The volume table of the pair. Proportional but modest: OOTO records absences, so its realistic
    -- worst case is thousands of rows rather than the hundreds of thousands `time_entries` sees. It
    -- needs to be non-trivial because an ALTER here builds an index, which scans every row.
    PERFORM rehearsal.fill_auto('out_of_office_events', GREATEST(n / 25, 200));
END
$seed$;

COMMIT;

-- ANALYZE so the pending migrations' planner decisions are made against real statistics rather than
-- against the empty-table defaults every other gate leaves in place.
ANALYZE;
