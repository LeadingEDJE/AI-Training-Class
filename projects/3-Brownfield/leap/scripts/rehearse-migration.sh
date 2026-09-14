#!/usr/bin/env bash
# rehearse-migration.sh -- apply pending migrations to a POPULATED database and report what happens.
#
# Issue #211 item D1. Companion runbook: docs/ops/populated-migration-rehearsal.md
#
# THE GAP THIS CLOSES
#   Every gate in this repository migrates an EMPTY database (integration suite: fresh Testcontainer;
#   e2e-stack-up: drop and recreate; CI: idempotent script against a virgin compose database). A
#   deployed upgrade migrates a POPULATED one. Since Phase 51 the migration runs in a pre-rollout
#   Helm hook, so a migration that is only invalid in the presence of rows does not degrade -- it
#   FAILS THE RELEASE. And because migrations do not roll back while `--atomic` reverts the
#   application, a partial failure can leave the schema ahead of the running code.
#
#   No test can catch this for you. This script is the thing you run instead.
#
# WHAT IT DOES
#   1. Starts a throwaway PostgreSQL 16 container (role `timesheet`, matching every deployed
#      environment -- which also reproduces the issue #208 role-name/schema-name collision).
#   2. Migrates it to the BASELINE (the migration currently deployed to the target environment).
#   3. Seeds synthetic volume at that baseline schema (scripts/migration-rehearsal/seed-volume.sql).
#   4. Applies each pending migration ONE AT A TIME, timing it individually.
#   5. Compares row counts before and after, every table, and fails on any loss that was not
#      DECLARED in scripts/migration-rehearsal/expected-row-loss.tsv. A declaration makes the check
#      stricter for the table it names -- exact delta, and a declaration that does not fire fails.
#   6. Prints the slowest migration next to the migration Job's activeDeadlineSeconds budget.
#
# WHAT IT DOES NOT PROVE
#   - It uses SYNTHETIC data. A constraint that only real data violates will not be caught here.
#     Uniform synthetic values can also be kinder to an index build than skewed real data.
#   - It runs `dotnet ef database update`, not the `efbundle` the deploy runs. Same migrations, same
#     order, different host process.
#   - It says nothing about backward compatibility (expand/contract). That is a separate question
#     this script cannot answer: it measures whether the migration SUCCEEDS on rows, not whether the
#     PREVIOUS application version still works afterwards.
#   - Container-local disk and CPU are not RDS. Treat timings as a lower bound and a relative
#     signal, not a prediction.

set -euo pipefail

RED=$'\033[0;31m'; GREEN=$'\033[0;32m'; YELLOW=$'\033[0;33m'; BLUE=$'\033[0;34m'; RESET=$'\033[0m'
fail()  { printf '%s[FAIL]%s %s\n' "$RED"    "$RESET" "$1" >&2; }
pass()  { printf '%s[ok]%s   %s\n' "$GREEN"  "$RESET" "$1"; }
info()  { printf '%s[info]%s %s\n' "$YELLOW" "$RESET" "$1"; }
step()  { printf '\n%s==>%s %s\n'  "$BLUE"   "$RESET" "$1"; }

# ── Defaults ────────────────────────────────────────────────────────────────────────────────────
BASELINE=""
TARGET=""
ROWS=100000
# 15432 rather than something in the 5xxxx range: on Windows, ports above ~49152 fall inside the
# dynamic/Hyper-V excluded range and `docker run -p` fails with "An attempt was made to access a
# socket in a way forbidden by its access permissions" -- which reads like a permissions problem and
# is really a reserved-range problem. Measured: 55432 blocked, 15432/15433/25432/5433 free.
PORT=15432
PORT_EXPLICIT=0
PORT_FALLBACKS=(15433 25432 5433)
DB_NAME="leap_rehearsal"
DB_USER="timesheet"          # deliberately matches deployed environments (see issue #208)
DB_PASS="rehearsal-local"
CONTAINER="leap-migration-rehearsal"
KEEP=0
LIST_ONLY=0
# The migration Job's activeDeadlineSeconds (deploy/helm/leap/values.yaml). Read, not hard-coded,
# so this comparison cannot silently drift away from the value that actually kills the Job.
DEADLINE=$(grep -E '^\s+activeDeadlineSeconds:' deploy/helm/leap/values.yaml | grep -oE '[0-9]+' | head -1)
DEADLINE_SOURCE="deploy/helm/leap/values.yaml"

usage() {
  cat <<EOF
Usage: bash scripts/rehearse-migration.sh --baseline <MigrationId> [options]

  --baseline <id>   REQUIRED. The migration the target environment is currently ON. Everything
                    after it in the ordered list is what gets rehearsed. See --list.
                    Pass `auto` to resolve it from the git merge base with BASE_REF
                    (default origin/main) -- the pending set then becomes exactly the
                    migrations this branch adds. That is the CI mode; it needs a full
                    checkout (fetch-depth: 0).
                    To read it from the deployed dev database:
                      SELECT "MigrationId" FROM public."__EFMigrationsHistory"
                      ORDER BY "MigrationId" DESC LIMIT 1;
  --target <id>     Stop after this migration. Default: the newest one.
  --rows <n>        Synthetic rows in the largest table. Default: ${ROWS}.
  --port <n>        Host port for the throwaway container. Default: ${PORT}.
  --deadline <s>    Override the migration Job budget this compares against. Default: read from
                    deploy/helm/leap/values.yaml (migrations.activeDeadlineSeconds).
  --keep            Leave the container running afterwards so you can inspect it.
  --list            Print the ordered migration list and exit.

Intentional deletions:
  A contract migration that retires a row on purpose declares it in
  scripts/migration-rehearsal/expected-row-loss.tsv. The declaration is not a mute switch: the
  loss must match EXACTLY, a declaration that does not fire fails the run, and undeclared loss
  anywhere still fails.
  -h, --help        This message.

Examples:
  bash scripts/rehearse-migration.sh --list
  bash scripts/rehearse-migration.sh --baseline 20260810121031_AddAuditLogEffectiveRoles
  bash scripts/rehearse-migration.sh --baseline 20260810121031_AddAuditLogEffectiveRoles --rows 1000000
  bash scripts/rehearse-migration.sh --baseline auto --rows 50000        # CI mode
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    --baseline) BASELINE="${2:-}"; shift 2 ;;
    --target)   TARGET="${2:-}";   shift 2 ;;
    --rows)     ROWS="${2:-}";     shift 2 ;;
    --port)     PORT="${2:-}"; PORT_EXPLICIT=1; shift 2 ;;
    # Exists so the budget-exceeded branch is REACHABLE without editing chart values. A gate whose
    # red path has never run is indistinguishable from one that always passes, and that branch cannot
    # otherwise be exercised without a migration that genuinely takes eight minutes.
    --deadline) DEADLINE="${2:-}"; DEADLINE_SOURCE="--deadline override"; shift 2 ;;
    --keep)     KEEP=1;            shift ;;
    --list)     LIST_ONLY=1;       shift ;;
    -h|--help)  usage; exit 0 ;;
    *) fail "unknown argument: $1"; usage >&2; exit 2 ;;
  esac
done

# ── Preflight ───────────────────────────────────────────────────────────────────────────────────
step "Preflight"

# Docker is only needed to RUN a rehearsal. `--list` reads the migration chain from the project with
# `--no-connect` and never touches a database, so requiring a daemon for it turned the first command
# the runbook tells you to run into a dead end on a machine where Docker simply is not started.
if [ "$LIST_ONLY" -eq 0 ]; then
  command -v docker >/dev/null || { fail "docker is required"; exit 1; }
  docker info >/dev/null 2>&1 || { fail "the docker daemon is not running"; exit 1; }
  pass "docker daemon reachable"
else
  info "--list: skipping the Docker checks (listing migrations never touches a database)"
fi

command -v dotnet >/dev/null || { fail "dotnet is required"; exit 1; }

# The EF tooling MAJOR must match the project's EF Core major. An older dotnet-ef cannot load a
# 10.0 model and the error it gives does not say so plainly, so check it here where it is cheap.
EF_VERSION=$(dotnet ef --version 2>/dev/null | grep -oE '[0-9]+\.[0-9]+\.[0-9]+' | head -1 || true)
if [ -z "$EF_VERSION" ]; then
  fail "dotnet-ef is not installed. Install it with: dotnet tool install --global dotnet-ef --version 10.0.10"
  exit 1
fi
if [ "${EF_VERSION%%.*}" -lt 10 ]; then
  fail "dotnet-ef ${EF_VERSION} is too old -- this project is EF Core 10 (CI pins 10.0.10)."
  info "Fix with: dotnet tool update --global dotnet-ef --version 10.0.10"
  exit 1
fi
pass "dotnet-ef ${EF_VERSION}"

[ -n "$DEADLINE" ] || { fail "could not read migrations.activeDeadlineSeconds from values.yaml"; exit 1; }
pass "migration Job budget: ${DEADLINE}s (${DEADLINE_SOURCE})"

# ── The ordered migration list ──────────────────────────────────────────────────────────────────
# --no-connect so this works with no database in existence yet. Filter to the timestamp-prefixed
# ids so build chatter cannot end up in the list.
step "Reading the migration list"
mapfile -t ALL_MIGRATIONS < <(
  dotnet ef migrations list --no-connect --project api --context LeapDbContext 2>/dev/null \
    | grep -oE '^[0-9]{14}_[A-Za-z0-9_]+' || true
)
# A selector that matches nothing PASSES unless you check it -- twelve gates were found fail-open
# this way across phases 48-50. Check it.
[ "${#ALL_MIGRATIONS[@]}" -gt 0 ] || { fail "no migrations parsed -- does 'dotnet ef migrations list' build?"; exit 1; }
pass "${#ALL_MIGRATIONS[@]} migrations in the chain"

if [ "$LIST_ONLY" -eq 1 ]; then
  printf '%s\n' "${ALL_MIGRATIONS[@]}"
  exit 0
fi

[ -n "$BASELINE" ] || { fail "--baseline is required (see --list)"; usage >&2; exit 2; }

# ── --baseline auto: resolve from the merge base ─────────────────────────────────────────────────
# For CI there is no deployed environment to read a baseline from, and the right question at
# authoring time is a different one anyway: "do the migrations THIS BRANCH ADDS survive a populated
# database?" So the baseline is the newest migration already present on the base branch, which makes
# the pending set exactly the branch's own new migrations.
#
# MERGE BASE, NOT THE BASE TIP. `git ls-tree origin/main` would describe main as it is NOW; if main
# has moved on since the branch forked, a migration added on main and absent here would be read as
# the baseline and the rehearsal would test the wrong delta. `git merge-base` is the fork point.
# (The same tip-versus-merge-base distinction the `changes` job's diff comments warn about, which
# once cost this repository a coverage gate.)
#
# This is NOT a substitute for the manual pre-promotion run. If main is ahead of what is deployed,
# CI rehearses main -> branch while a real promotion rehearses deployed -> branch. Both are wanted.
if [ "$BASELINE" = "auto" ]; then
  step "Resolving the baseline from the merge base"
  BASE_REF="${BASE_REF:-origin/main}"

  # Resolution is SHARED with check-migrations-immutable.sh (scripts/lib/comparison-point.sh), so
  # the degenerate "HEAD is the base branch" case is handled once for both gates rather than fixed
  # in one and left in the other -- which is exactly what happened before this was extracted.
  # shellcheck source=scripts/lib/comparison-point.sh
  SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  . "${SCRIPT_DIR}/lib/comparison-point.sh"

  if ! COMPARISON=$(resolve_comparison_point "$BASE_REF"); then
    fail "could not resolve a comparison point against ${BASE_REF}."
    info "Locally, pass an explicit --baseline instead (see --list)."
    exit 1
  fi
  COMPARE_REF="${COMPARISON%%$'\t'*}"
  COMPARE_MODE="${COMPARISON#*$'\t'}"
  pass "comparing against $(git rev-parse --short "$COMPARE_REF") -- ${COMPARE_MODE}"
  # Migration ids present at the comparison point. .Designer.cs and the snapshot are excluded: only the
  # migration file itself names a migration.
  mapfile -t BASE_MIGRATIONS < <(
    git ls-tree -r --name-only "$COMPARE_REF" -- api/Platform/Data/Migrations/ 2>/dev/null \
      | grep -vE '\.Designer\.cs$' \
      | grep -oE '[0-9]{14}_[A-Za-z0-9_]+\.cs$' \
      | sed 's/\.cs$//' \
      | sort
  )

  if [ "${#BASE_MIGRATIONS[@]}" -eq 0 ]; then
    fail "no migrations found at the comparison point -- refusing to guess a baseline."
    info "A zero-length list here would otherwise be treated as 'baseline = nothing', which"
    info "rehearses the ENTIRE chain against an empty database and proves nothing about this branch."
    exit 1
  fi

  BASELINE="${BASE_MIGRATIONS[${#BASE_MIGRATIONS[@]} - 1]}"
  pass "baseline resolved to ${BASELINE} via ${COMPARE_MODE}"
  pass "  ${#BASE_MIGRATIONS[@]} migration(s) present at $(git rev-parse --short "$COMPARE_REF")"
fi

BASE_IDX=-1
for i in "${!ALL_MIGRATIONS[@]}"; do
  [ "${ALL_MIGRATIONS[$i]}" = "$BASELINE" ] && BASE_IDX=$i && break
done
[ "$BASE_IDX" -ge 0 ] || { fail "baseline '${BASELINE}' is not in the migration list (see --list)"; exit 1; }

PENDING=("${ALL_MIGRATIONS[@]:$((BASE_IDX + 1))}")
if [ -n "$TARGET" ]; then
  # VALIDATE BEFORE TRIMMING. The first version appended until it saw the target and broke; an
  # unmatched target simply never broke, so the trimmed list came out EQUAL TO THE FULL PENDING SET
  # and the run silently rehearsed everything the caller had asked to stop short of. A selector that
  # matches nothing must fail loudly -- the same rule this script already applies to the migration
  # list and to the seeded row count, and the rule twelve gates were found breaking across
  # phases 48-50.
  TARGET_FOUND=0
  for m in "${PENDING[@]}"; do
    if [ "$m" = "$TARGET" ]; then TARGET_FOUND=1; break; fi
  done
  if [ "$TARGET_FOUND" -eq 0 ]; then
    fail "--target '${TARGET}' is not in the pending set. It would select nothing and silently"
    fail "rehearse the WHOLE pending set instead. Pending after ${BASELINE}:"
    printf '         %s\n' "${PENDING[@]}" >&2
    exit 2
  fi
  TRIMMED=()
  for m in "${PENDING[@]}"; do
    TRIMMED+=("$m")
    if [ "$m" = "$TARGET" ]; then break; fi
  done
  PENDING=("${TRIMMED[@]}")
fi

if [ "${#PENDING[@]}" -eq 0 ]; then
  info "nothing pending after ${BASELINE} -- there is no migration to rehearse."
  info "Comparison point: ${COMPARE_MODE:-explicit --baseline}."
  info "That is legitimate when the branch adds no migration. It is NOT legitimate if you expected"
  info "one -- check that the comparison point above names what you think it does."
  exit 0
fi
pass "${#PENDING[@]} migration(s) to rehearse after ${BASELINE}"
printf '       %s\n' "${PENDING[@]}"

# ── Declared intentional row loss ───────────────────────────────────────────────────────────────
#
# A contract migration that retires a row on purpose has to be able to say so. The alternative was
# weakening the row-loss check for everyone, which is how a gate stops meaning anything.
#
# The declaration makes this gate STRICTER for the table it names, not weaker: the loss must be
# EXACTLY the declared delta, and a declaration that does not fire fails the run. Undeclared loss
# still fails as before. See the file's own header for the reasoning and the format.
#
# bash 3.2 COMPATIBLE ON PURPOSE. `declare -A` would be the obvious way to hold these and it is a
# bash 4 feature -- macOS ships 3.2.57, so an associative array here would work in CI (ubuntu, bash
# 5) and break every local run of a script whose whole point is that it also runs by hand before a
# production promotion. Newline-delimited records keyed by awk, matching how BEFORE/AFTER are
# already handled below.
EXPECTED_LOSS_FILE="scripts/migration-rehearsal/expected-row-loss.tsv"
DECLARED=""        # schema.table \t delta \t migration \t reason  -- PENDING migrations only
DECLARED_N=0
INERT_N=0

if [ -f "$EXPECTED_LOSS_FILE" ]; then
  LINE_NO=0
  while IFS= read -r raw || [ -n "$raw" ]; do
    LINE_NO=$((LINE_NO + 1))
    case "$raw" in ''|'#'*) continue ;; esac

    d_mig=$(printf '%s' "$raw" | cut -f1)
    d_tbl=$(printf '%s' "$raw" | cut -f2)
    d_delta=$(printf '%s' "$raw" | cut -f3)
    d_why=$(printf '%s' "$raw" | cut -f4-)

    # Every field is validated even for migrations that are not pending. A typo sitting in an inert
    # entry would only be discovered on the run that needed it to work, which is the worst moment.
    if [ -z "$d_mig" ] || [ -z "$d_tbl" ] || [ -z "$d_delta" ] || [ -z "$d_why" ]; then
      fail "${EXPECTED_LOSS_FILE}:${LINE_NO} needs four TAB-separated fields:"
      fail "  <migration_id> <schema.table> <negative delta> <reason>"
      fail "got: ${raw}"
      exit 2
    fi
    case "$d_tbl" in *.*) ;; *)
      fail "${EXPECTED_LOSS_FILE}:${LINE_NO} table '${d_tbl}' must be schema-qualified (e.g. compass.employee_type)"
      exit 2 ;;
    esac
    case "$d_delta" in
      -[0-9]*) ;;
      DROP) ;;
      *) fail "${EXPECTED_LOSS_FILE}:${LINE_NO} delta '${d_delta}' must be a NEGATIVE integer (rows REMOVED from a surviving table), or DROP (the whole table is intentionally dropped)"
         exit 2 ;;
    esac

    KNOWN=0
    for m in "${ALL_MIGRATIONS[@]}"; do
      if [ "$m" = "$d_mig" ]; then KNOWN=1; break; fi
    done
    if [ "$KNOWN" -eq 0 ]; then
      fail "${EXPECTED_LOSS_FILE}:${LINE_NO} names migration '${d_mig}', which is not in the chain."
      fail "A typo here would grant nothing and be discovered only by the run that needed it."
      exit 2
    fi

    IS_PENDING=0
    for m in "${PENDING[@]}"; do
      if [ "$m" = "$d_mig" ]; then IS_PENDING=1; break; fi
    done
    if [ "$IS_PENDING" -eq 1 ]; then
      DECLARED="${DECLARED}${d_tbl}\t${d_delta}\t${d_mig}\t${d_why}\n"
      DECLARED_N=$((DECLARED_N + 1))
    else
      INERT_N=$((INERT_N + 1))
    fi
  done < "$EXPECTED_LOSS_FILE"
fi

DECLARED=$(printf '%b' "$DECLARED")

if [ "$DECLARED_N" -gt 0 ]; then
  info "${DECLARED_N} declared intentional row deletion(s) apply to this pending set:"
  printf '%s\n' "$DECLARED" | awk -F'\t' 'NF>=3 {printf "       %-32s %s  (%s)\n", $1, $2, $3}'
  info "Each must occur EXACTLY as declared -- see ${EXPECTED_LOSS_FILE}."
fi
[ "$INERT_N" -gt 0 ] && info "${INERT_N} declaration(s) in ${EXPECTED_LOSS_FILE} are inert (their migration is already in the baseline)."

# ── Throwaway database ──────────────────────────────────────────────────────────────────────────
cleanup() {
  if [ "$KEEP" -eq 1 ]; then
    info "--keep: container '${CONTAINER}' left running on port ${PORT}"
    info "  psql: docker exec -it ${CONTAINER} psql -U ${DB_USER} -d ${DB_NAME}"
    info "  stop: docker rm -f ${CONTAINER}"
  else
    docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

step "Starting a throwaway PostgreSQL 16"
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true

# Try the chosen port, then the fallbacks -- unless the caller named one explicitly, in which case a
# bind failure is theirs to see rather than something to quietly route around.
CANDIDATES=("$PORT")
[ "$PORT_EXPLICIT" -eq 0 ] && CANDIDATES+=("${PORT_FALLBACKS[@]}")

STARTED=0
for candidate in "${CANDIDATES[@]}"; do
  if docker run -d --name "$CONTAINER" \
      -e POSTGRES_USER="$DB_USER" \
      -e POSTGRES_PASSWORD="$DB_PASS" \
      -e POSTGRES_DB="$DB_NAME" \
      -p "${candidate}:5432" \
      postgres:16 >/dev/null 2>/tmp/rehearse-port-$$.log; then
    PORT="$candidate"
    STARTED=1
    break
  fi
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  info "port ${candidate} unavailable, trying the next one"
done

if [ "$STARTED" -eq 0 ]; then
  fail "could not bind any of: ${CANDIDATES[*]}"
  tail -3 /tmp/rehearse-port-$$.log >&2 || true
  rm -f /tmp/rehearse-port-$$.log
  exit 1
fi
rm -f /tmp/rehearse-port-$$.log
pass "container started on port ${PORT}"

for _ in $(seq 1 60); do
  if docker exec "$CONTAINER" pg_isready -U "$DB_USER" -d "$DB_NAME" >/dev/null 2>&1; then
    READY=1; break
  fi
  sleep 1
done
[ "${READY:-0}" -eq 1 ] || { fail "database never became ready"; exit 1; }
pass "database ready"

# 127.0.0.1, not localhost: the EF tooling below runs on the HOST and reaches this container through
# its published port. On Windows `localhost` resolves to ::1 first, where Docker Desktop's WSL2 relay
# can accept a connection and then abort it -- which reads as an unreachable database against a
# container this script has just watched become ready. Matches the default in the Makefile.
export POSTGRES_HOST=127.0.0.1
export POSTGRES_PORT="$PORT"
export POSTGRES_DB="$DB_NAME"
export POSTGRES_USER="$DB_USER"
export POSTGRES_PASSWORD="$DB_PASS"

psql_q() { docker exec -i "$CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -qtAX -c "$1"; }

# Row counts for every ordinary table in every module schema, as
# `oid<TAB>count<TAB>schema<TAB>table`.
#
# KEYED ON THE pg_class OID, AND THAT IS THE WHOLE POINT.
#   Postgres preserves a table's OID across both `ALTER TABLE ... RENAME` and
#   `ALTER TABLE ... SET SCHEMA`, and assigns a new one on drop-and-recreate. So the OID is the only
#   identifier that answers the question this comparison actually asks -- "is this the same table,
#   and does it still hold its rows?" -- without being fooled by the table being called something
#   else afterwards.
#
#   Two earlier versions each got this wrong in an opposite direction, and both shipped:
#     * Keyed on `schema.table`: the very first real run rehearsed
#       20260810202407_MoveModuleTablesToSchemas, every moved table looked like it had ceased to
#       exist, the comparison SKIPPED it, and 20,000 seeded rows went unverified while the script
#       printed "no row loss in any table". A FALSE PASS, on the migration shape most likely to lose
#       rows.
#     * Keyed on the bare table name: a same-schema `RenameTable` then read as a table dropped with
#       its rows, so a safe rename FAILED the gate with a message claiming data loss that had not
#       happened. A FALSE FAIL -- and the more corrosive of the two, because a gate that cries wolf
#       on correct migrations is a gate people start passing with --no-verify.
#
#   The OID keys both correctly, and lets the report name what actually happened: moved, renamed, or
#   genuinely gone. A drop-and-recreate DOES surface as gone-plus-new, which is the honest answer --
#   the old table's rows are not in the new one unless something copied them.
#
# Uses a live catalog walk rather than a hard-coded list so a table ADDED by a migration is seen too.
row_counts() {
  psql_q "
    SELECT c.oid::text || E'\t' ||
           (xpath('/row/c/text()',
                  query_to_xml(format('SELECT count(*) AS c FROM %I.%I', n.nspname, c.relname),
                               false, true, '')))[1]::text::bigint
           || E'\t' || n.nspname || E'\t' || c.relname
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE c.relkind = 'r'
      AND n.nspname IN ('public','timesheet','ooto','compass')
      AND c.relname <> '__EFMigrationsHistory'
    ORDER BY n.nspname, c.relname;"
}

# ── Baseline ────────────────────────────────────────────────────────────────────────────────────
step "Migrating to the baseline: ${BASELINE}"
dotnet ef database update "$BASELINE" --project api --context LeapDbContext >/dev/null
pass "at baseline"

# ── Seed ────────────────────────────────────────────────────────────────────────────────────────
step "Seeding synthetic volume (largest table: ${ROWS} rows)"
SEED_START=$SECONDS
docker exec -i "$CONTAINER" psql -U "$DB_USER" -d "$DB_NAME" -qX \
  -v rows="$ROWS" -v ON_ERROR_STOP=1 \
  -f - < scripts/migration-rehearsal/seed-volume.sql
pass "seeded in $((SECONDS - SEED_START))s"

BEFORE=$(row_counts)
TOTAL_BEFORE=$(printf '%s\n' "$BEFORE" | awk -F'\t' '{s+=$2} END {print s+0}')
# Seeding nothing would make every assertion below vacuously true. Refuse that.
[ "$TOTAL_BEFORE" -gt 0 ] || { fail "the database is still empty after seeding -- the rehearsal would prove nothing"; exit 1; }
pass "${TOTAL_BEFORE} rows across $(printf '%s\n' "$BEFORE" | grep -c . ) tables"
printf '%s\n' "$BEFORE" | awk -F'\t' '$2 > 0 {printf "       %-40s %s\n", $1, $2}'

# ── Apply pending, one at a time, timed ─────────────────────────────────────────────────────────
step "Applying ${#PENDING[@]} pending migration(s) against a populated database"
TIMINGS=""
SLOWEST=0
SLOWEST_NAME=""
FAILED=""

for m in "${PENDING[@]}"; do
  printf '     %-58s ' "$m"
  START=$SECONDS
  if dotnet ef database update "$m" --project api --context LeapDbContext >/tmp/rehearse-$$.log 2>&1; then
    ELAPSED=$((SECONDS - START))
    printf '%s%ds%s\n' "$GREEN" "$ELAPSED" "$RESET"
  else
    ELAPSED=$((SECONDS - START))
    printf '%sFAILED after %ds%s\n' "$RED" "$ELAPSED" "$RESET"
    FAILED="$m"
    echo "--- migration output ---" >&2
    tail -40 /tmp/rehearse-$$.log >&2
    rm -f /tmp/rehearse-$$.log
    break
  fi
  rm -f /tmp/rehearse-$$.log
  TIMINGS="${TIMINGS}${m}\t${ELAPSED}\n"
  if [ "$ELAPSED" -gt "$SLOWEST" ]; then SLOWEST=$ELAPSED; SLOWEST_NAME=$m; fi
done

# ── Report ──────────────────────────────────────────────────────────────────────────────────────
step "Result"

if [ -n "$FAILED" ]; then
  fail "migration ${FAILED} FAILED against a populated database."
  info "It would have passed every gate in this repository, all of which migrate an empty one."
  info "In a deploy this fails the pre-rollout Helm hook and therefore fails the release."
  info "Re-run with --keep to inspect the database at the point of failure."
  exit 1
fi

AFTER=$(row_counts)
LOSS=0
MOVES=0
RENAMES=0
VERIFIED=0
DECLARED_MATCHED=""
while IFS=$'\t' read -r oid before_n before_schema before_tbl; do
  [ -n "$oid" ] || continue
  after_n=$(printf      '%s\n' "$AFTER" | awk -F'\t' -v o="$oid" '$1 == o {print $2}')
  after_schema=$(printf '%s\n' "$AFTER" | awk -F'\t' -v o="$oid" '$1 == o {print $3}')
  after_tbl=$(printf    '%s\n' "$AFTER" | awk -F'\t' -v o="$oid" '$1 == o {print $4}')

  if [ -z "$after_n" ]; then
    # This relation no longer exists AT ALL -- not renamed, not moved, since either would have kept
    # the OID. Dropped, or dropped and recreated (a recreate gets a new OID and appears as a new
    # table). Either way the rows it held are not in it any more.
    if [ "$before_n" -gt 0 ]; then
      # A DROP is all-or-nothing, so its declaration is count-independent (delta DROP), unlike a
      # deletion from a surviving table. It is still not a mute switch: it fires only for the exact
      # table this migration drops, undeclared drops below still FAIL, and the did-not-fire check
      # further down breaks the build if the declaration stops matching.
      DROP_KEY="${before_schema}.${before_tbl}"
      DROP_DECLARED=$(printf '%s\n' "$DECLARED" | awk -F'\t' -v k="$DROP_KEY" '$1 == k && $2 == "DROP" {print "yes"; exit}')
      if [ "$DROP_DECLARED" = "yes" ]; then
        info "DECLARED table DROP of ${DROP_KEY}: ${before_n} row(s) removed with the table (as declared)"
        printf '%s\n' "$DECLARED" | awk -F'\t' -v k="$DROP_KEY" '$1 == k && $2 == "DROP" {printf "         %s: %s\n", $3, $4}'
        DECLARED_MATCHED="${DECLARED_MATCHED}${DROP_KEY}\n"
      else
        fail "table ${before_schema}.${before_tbl} was DROPPED and it held ${before_n} rows"
        LOSS=1
      fi
    else
      info "table ${before_schema}.${before_tbl} no longer exists (was empty)"
    fi
    continue
  fi

  if [ "$before_schema" != "$after_schema" ]; then
    MOVES=$((MOVES + 1))
    info "moved:   ${before_tbl}  ${before_schema} -> ${after_schema}  (${before_n} rows carried)"
  fi
  if [ "$before_tbl" != "$after_tbl" ]; then
    RENAMES=$((RENAMES + 1))
    info "renamed: ${before_schema}.${before_tbl} -> ${after_schema}.${after_tbl}  (${before_n} rows carried)"
  fi

  if [ "$after_n" -lt "$before_n" ]; then
    ACTUAL_DELTA=$((after_n - before_n))
    KEY="${after_schema}.${after_tbl}"

    # Sum, not first-match: two pending migrations may each retire a row from the same lookup table,
    # and taking only the first would fail a correctly-declared pair.
    EXPECTED_DELTA=$(printf '%s\n' "$DECLARED" | awk -F'\t' -v k="$KEY" '$1 == k {s += $2} END {print s+0}')

    if [ "$EXPECTED_DELTA" -ne 0 ] && [ "$ACTUAL_DELTA" -eq "$EXPECTED_DELTA" ]; then
      # Declared and exact. Reported at [info] volume rather than passed over in silence: a deletion
      # nobody sees in the log is one nobody reviews.
      info "DECLARED row deletion in ${KEY}: ${before_n} -> ${after_n} (${ACTUAL_DELTA}, as declared)"
      printf '%s\n' "$DECLARED" | awk -F'\t' -v k="$KEY" '$1 == k {printf "         %s: %s\n", $3, $4}'
      DECLARED_MATCHED="${DECLARED_MATCHED}${KEY}\n"
    elif [ "$EXPECTED_DELTA" -ne 0 ]; then
      # A declaration exists and the arithmetic disagrees. Worth its own message: "data loss" alone
      # would send the reader hunting for an undeclared deletion that is in fact declared wrongly.
      fail "DECLARED DELTA MISMATCH in ${KEY}: ${before_n} -> ${after_n} (${ACTUAL_DELTA}), declared ${EXPECTED_DELTA}"
      fail "The migration and ${EXPECTED_LOSS_FILE} disagree. Fix whichever is wrong -- do not"
      fail "widen the declaration to match a deletion you did not intend."
      DECLARED_MATCHED="${DECLARED_MATCHED}${KEY}\n"
      LOSS=1
    else
      fail "DATA LOSS in ${KEY}: ${before_n} -> ${after_n}"
      LOSS=1
    fi
  else
    [ "$before_n" -gt 0 ] && VERIFIED=$((VERIFIED + 1))
  fi
done <<< "$BEFORE"

# A declaration that never fired. This is the check that stops the file becoming a standing
# exemption: once a migration stops deleting, its entry breaks the build until someone removes it,
# so no line can sit here granting a pass for a deletion that no longer happens.
if [ "$DECLARED_N" -gt 0 ]; then
  while IFS=$'\t' read -r d_tbl d_delta d_mig d_why; do
    [ -n "$d_tbl" ] || continue
    if ! printf '%b' "$DECLARED_MATCHED" | grep -qxF "$d_tbl"; then
      fail "DECLARATION DID NOT FIRE: ${EXPECTED_LOSS_FILE} declares ${d_delta} on ${d_tbl}"
      fail "for ${d_mig}, and that table lost nothing. The declaration is stale or the migration"
      fail "changed -- remove the line rather than leaving a standing exemption behind."
      LOSS=1
    fi
  done <<< "$DECLARED"
fi

if [ "$LOSS" -eq 0 ]; then
  if [ "$DECLARED_N" -gt 0 ]; then
    pass "no UNDECLARED row loss: ${VERIFIED} non-empty table(s) verified, ${MOVES} schema move(s) and ${RENAMES} rename(s) followed, ${DECLARED_N} declared deletion(s) occurred exactly as declared"
  else
    pass "no row loss: ${VERIFIED} non-empty table(s) verified, ${MOVES} schema move(s) and ${RENAMES} rename(s) followed"
  fi
else
  fail "row loss detected -- see above"
  exit 1
fi

printf '\n     %-58s %s\n' "MIGRATION" "SECONDS"
printf '%b' "$TIMINGS" | awk -F'\t' 'NF==2 {printf "     %-58s %s\n", $1, $2}'

printf '\n'
pass "all ${#PENDING[@]} migration(s) applied to a populated database (${TOTAL_BEFORE} rows)"
info "slowest single migration: ${SLOWEST_NAME:-n/a} at ${SLOWEST}s"
info "migration Job budget (activeDeadlineSeconds): ${DEADLINE}s (${DEADLINE_SOURCE})"

if [ "$SLOWEST" -gt "$DEADLINE" ]; then
  fail "the slowest migration EXCEEDS the Job budget -- in a deploy the Job would be killed mid-migration."
  info "Raise migrations.activeDeadlineSeconds AND the caller's helm_timeout together; the deadline"
  info "must stay BELOW the helm timeout or a wedged migration reports as an ambiguous helm timeout."
  exit 1
fi

# Local container disk is faster than RDS and synthetic rows are kinder than skewed real ones, so a
# result merely NEAR the budget is not a comfortable result.
HALF=$((DEADLINE / 2))
if [ "$SLOWEST" -gt "$HALF" ]; then
  info "${YELLOW}within budget, but past half of it.${RESET} Local disk beats RDS and synthetic rows are"
  info "uniform -- treat this as close, not safe, and consider raising the budget before deploying."
fi

printf '\n'
info "REMEMBER what this did not test: expand/contract backward compatibility. A green run here means"
info "the migration SUCCEEDS on rows -- not that the PREVIOUS application version still works against"
info "the new schema, which is what a failed atomic rollback leaves you needing. See"
info "docs/ops/populated-migration-rehearsal.md."
