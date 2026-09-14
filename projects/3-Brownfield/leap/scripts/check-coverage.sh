#!/usr/bin/env bash
set -euo pipefail

# Coverage enforcement script for changed files only.
# Enforces 100% line coverage on new/changed frontend and backend code.

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

# shellcheck source=scripts/lib/cobertura-match.sh
. "$REPO_ROOT/scripts/lib/cobertura-match.sh"

# shellcheck source=scripts/lib/coverage-json-match.sh
. "$REPO_ROOT/scripts/lib/coverage-json-match.sh"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

info()  { echo -e "${GREEN}[coverage]${NC} $*"; }
warn()  { echo -e "${YELLOW}[coverage]${NC} $*"; }
fail()  { echo -e "${RED}[coverage]${NC} $*"; }

# Check for jq (needed for frontend coverage JSON parsing)
if ! command -v jq &>/dev/null; then
  fail "jq is required for coverage checking. Install with: sudo apt install jq"
  exit 1
fi

# Determine changed files. Callers (e.g. CI) can pre-specify a diff base via BASE_REF
# (e.g. BASE_REF=origin/main), which is expanded below into a three-dot merge-base range;
# the pre-push default compares against the remote tracking branch instead.
if [ -n "${BASE_REF:-}" ]; then
  CHANGED=$(git diff --name-only --diff-filter=d "${BASE_REF}...HEAD" 2>/dev/null || echo "")
else
  # Diff-range semantics: the first term keeps TWO dots on purpose -- @{push} is this
  # branch's own remote tip, so a tree diff between the two tips is the intended meaning.
  # The main-branch fallback needs THREE dots: two dots is a tree diff between tips, so a
  # branch that is merely behind the main branch gets other people's merged commits
  # attributed to it and this gate then demands full coverage on code the branch never
  # touched. Three dots is the merge-base diff -- this branch's own commits only.
  CHANGED=$(git diff --name-only --diff-filter=d @{push}.. 2>/dev/null || git diff --name-only --diff-filter=d origin/main...HEAD 2>/dev/null || git diff --name-only --diff-filter=d HEAD~1 2>/dev/null || echo "")
fi

if [ -z "$CHANGED" ]; then
  info "No changed files detected -- skipping coverage check."
  exit 0
fi

# Filter out docs/planning/claude-only changes
CODE_FILES=$(echo "$CHANGED" | grep -vE '^\.(planning|claude)/|^docs/|^screenshots/' || true)

if [ -z "$CODE_FILES" ]; then
  info "Only docs/planning files changed -- skipping coverage check."
  exit 0
fi

# ============================================================
# Exclusion patterns (files never checked for coverage)
# ============================================================
is_excluded() {
  local file="$1"
  case "$file" in
    api/Platform/Data/Migrations/*) return 0 ;;
    api/Program.cs) return 0 ;;
    api/Platform/Data/DesignTimeDbContextFactory.cs) return 0 ;;
    *.Designer.cs) return 0 ;;
    tests/unit/*) return 0 ;;
    tests/integration/*) return 0 ;;
    *.config.*) return 0 ;;
    # Interfaces have no executable code -- coverage tools cannot instrument them.
    # ONE entry still covers all of them after the 48-12 module split: in a POSIX `case`
    # glob `*` matches `/` too, so this single pattern spans api/Platform/Interfaces/*,
    # api/Modules/{Timesheet,Ooto}/Interfaces/* and the nested Interfaces/Reports/*.
    # Splitting it per module would GROW the exclusion set from 33 entries to 35, which
    # the exclusion guard reads as a relaxation -- and would buy nothing.
    api/*/Interfaces/*) return 0 ;;
    # Static const-only classes have no executable IL -- coverage tools cannot instrument them
    api/Platform/Authorization/RolePolicy.cs) return 0 ;;
    # Static const-only class (claim type / setting strings); compiler inlines const fields
    # at use sites, no IL emitted, Cobertura emits no entry. User-approved exclusion 2026-04-24.
    api/Platform/Auth/AuthConstants.cs) return 0 ;;
    # Static const-only class (named non-human audit principals); identical shape to RolePolicy.cs
    # and AuthConstants.cs above -- the compiler inlines const fields at use sites, emits no IL and
    # no sequence points, so Cobertura emits no entry at all and the file surfaces as "No test
    # coverage found" rather than as a low percentage. The VALUE is asserted directly by
    # AuditEffectiveRolesTests.MigrationPrincipal_CannotCollideWithAHumanActor, which pins that it is
    # non-empty and NOT parseable as a Guid -- load-bearing, because every human actor is written as
    # an EdjeId, so a parseable system principal would be indistinguishable from a person.
    # User-approved exclusion 2026-08-10 (PR #194).
    api/Platform/Authorization/AuditPrincipals.cs) return 0 ;;
    # Abstract POCO base with only auto-properties -- .NET coverage tool does not
    # emit a Cobertura entry for this file; inherited property accessors are
    # attributed to the derived type. Behavior is tested in AuditableEntityTests
    # via a concrete derived entity.
    api/Platform/Domain/AuditableEntity.cs) return 0 ;;
    # Pure enum -- no instrumentable IL, coverage tools emit no Cobertura entry. Identical shape to
    # the TimesheetStatus.cs / TimeCategoryType.cs / NudgeRecipient.cs exclusions, and it fails the
    # same way: the file has no entry in the report at all, which surfaces as "No test coverage
    # found" rather than as a low percentage.
    # Unlike those three, the values here are asserted DIRECTLY --
    # CompassEntityModelTests.SowType_HasExactlyTheThreeValuesTheSpecDefines pins the three names,
    # which is load-bearing because they persist verbatim (HasConversion<string>) and are constrained
    # by ck_sow_type_is_known, so a rename is a schema change rather than a refactor.
    # User-approved exclusion 2026-08-07 (PR #186).
    api/Modules/Compass/SowType.cs) return 0 ;;
    # Pure enum -- no instrumentable IL, so Cobertura emits no entry at all and the file surfaces as
    # "No test coverage found" rather than as a low percentage. Identical shape to the SowType.cs
    # entry above. No test can close this: there is nothing to instrument.
    # Like SowType.cs, the values are asserted DIRECTLY rather than waived --
    # ClientStatusDerivationTests.ClientStatus_HasExactlyTheThreeValuesTheSpecDefines pins the three
    # names, load-bearing because the value space must stay closed AND TOTAL for the Client Directory
    # to sort on it. (Three since issue #274 added Former; the pin was renamed with it.)
    # User-approved exclusion 2026-08-12 (PR #212).
    api/Modules/Compass/ClientStatus.cs) return 0 ;;
    # Pure enum -- no instrumentable IL, so Cobertura emits no entry at all and the file surfaces as
    # "No test coverage found" rather than as a low percentage. Identical shape to the ClientStatus.cs
    # entry above, and added for the same reason at the same time: issue #274 split client status from
    # assignment currency into two types so a client-level caller cannot silently lose Former.
    # The values are asserted DIRECTLY rather than waived --
    # ClientStatusDerivationTests.AssignmentStatus_HasExactlyTheTwoValuesAnAssignmentCanHold pins the
    # two names, load-bearing because they are the WIRE words the assignment-history rows publish.
    api/Modules/Compass/AssignmentStatus.cs) return 0 ;;
    # Pure enum -- no instrumentable IL, coverage tools emit no Cobertura entry.
    # Same shape as TimesheetStatus.cs and TimeCategoryType.cs exclusions.
    # User-approved-pending exclusion 2026-04-28 (orchestrator-authorized overnight).
    api/Platform/Domain/NudgeRecipient.cs) return 0 ;;
    # Pure enum -- four members, no methods, no statements, so no instrumentable IL and Cobertura
    # emits no entry at all: the file surfaces as "No test coverage found" rather than as a low
    # percentage. Identical shape to the SowType.cs / ClientStatus.cs entries above. No test can
    # close this; there is nothing to instrument.
    # Like those two, the values are asserted DIRECTLY rather than waived --
    # DashboardCategoryParserTests pins each of the four members against the exact route segment it
    # parses from. That is load-bearing twice over: those segments are the published drill-down
    # URLs, and CompassDashboardRepository.GetBreakdownAsync THROWS on any member it has no branch
    # for (CompassDashboardRepositoryTests.GetBreakdownAsync_UnknownCategory_Throws_...), so adding
    # a fifth member without its breakdown fails loudly rather than rendering an empty panel.
    # User-approved exclusion 2026-08-18.
    api/Modules/Compass/Services/Read/DashboardCategory.cs) return 0 ;;
    # POCO with ONLY bare auto-properties: every member is `{ get; init; }` over a value type with
    # no property initializer, so the compiler emits no field-initializer IL and the accessors carry
    # [CompilerGenerated], which this script's own filter strips. Cobertura emits no entry for the
    # file. Identical shape to the AuditableEntity.cs exclusion above.
    # NOT generalisable to `Dtos/*`, and the sibling proves why: DashboardBreakdownRowDto.cs is NOT
    # excluded and reports 100%, because its `= string.Empty` initializers DO generate instrumentable
    # IL. The difference is the initializers, not the DTO-ness. Adding `= default` here purely to
    # manufacture IL was considered and rejected as noise serving only the coverage tool.
    # The VALUES are asserted directly -- CompassDashboardRepositoryTests exercises all four tile
    # counts through GetCountsAsync, and the integration suite's
    # EachTilesCount_EqualsItsOwnBreakdownsRowCount ties each one to its own breakdown.
    # User-approved exclusion 2026-08-18.
    api/Modules/Compass/Dtos/Read/SalesDashboardDto.cs) return 0 ;;
    # The published directory contract. An interface has no executable code to instrument --
    # the same reason `api/*/Interfaces/*` above is excluded -- but spec 013 (#451) moved
    # IDirectory out of an Interfaces/ folder into the published Contracts/ seam, so it no
    # longer matches that glob and the gate would fail it with "No test coverage found".
    # ICompassLookup.cs:8-14 records that exact failure happening once before.
    #
    # This REPLACES the pre-existing dead entry `api/Integrations/TpsClientService.cs`, whose
    # own comment recorded that it had matched nothing since Phase 43 deleted the real TPS
    # HTTP client, and that it was kept only because removing it would shrink the exclusion
    # set. Swapping a pattern that matches nothing for one that matches exactly one file keeps
    # the arm count at 39 -- the coverage-exclusion guard counts `|` alternatives and would flag
    # growth -- and it retires that recorded debt at the same time.
    # Deliberately NOT written as `api/*/Contracts/*` or `api/*/Contracts/I*.cs`: both were
    # tested and over-exempt, the first silently excusing all six moved DTO files.
    api/Modules/Compass/Contracts/IDirectory.cs) return 0 ;;
    # Real Slack HTTP client -- genuinely untestable at unit level.
    # Consumers tested via MockSlackClient. See decision D-04.
    # This entry's path was ALREADY stale before the move: the file has lived in the
    # Services/Slack/ subfolder, never at Services/SlackClient.cs. Corrected here to the
    # real file's new location.
    api/Platform/Services/Slack/SlackClient.cs) return 0 ;;
    # EF Core repositories -- delegate to DbContext/DbSet, require real PostgreSQL.
    # Covered by integration tests (Testcontainers). InMemory doubles used for unit tests.
    # In a POSIX case glob * matches / too, so this single pattern covers both
    # api/Platform/Data/Repositories/ and api/Modules/*/Data/Repositories/ (issue #447).
    api/*/Data/Repositories/*Repository.cs) return 0 ;;
    # DbContext -- EF Core infrastructure, requires database connection.
    # Covered by integration tests (Testcontainers).
    api/Platform/Data/LeapDbContext.cs) return 0 ;;
    # ADR-007's audit purge. Requires real PostgreSQL for every line that matters: ExecuteDeleteAsync,
    # an explicit transaction, and `SET LOCAL leap.audit_retention_purge` read by a database TRIGGER
    # (issue #317). The EF InMemory provider the unit suite uses supports none of the three -- it has
    # no ExecuteDelete, no real transaction, and no triggers at all, so a unit test could only assert
    # that a fake was called.
    # Covered by AuditRetentionServiceTests -- 10 integration tests (Testcontainers), including the two
    # that assert what must remain IMPOSSIBLE: an ordinary delete is still refused, and an UPDATE is
    # refused even inside a purge transaction.
    # NOT waived alongside it, because they are genuinely unit-testable and are unit-tested:
    # AuditRetentionJob.cs and AuditRetentionOptions.cs.
    # User-approved exclusion 2026-08-21 (#317).
    api/Platform/Services/AuditRetentionService.cs) return 0 ;;
  esac
  return 1
}

# Per-file frontend coverage thresholds for components where 100% statement
# coverage is impractical in unit tests. Default is 1 (100%).
# Each entry MUST have a comment explaining why it can't reach 100%.
get_frontend_coverage_threshold() {
  local file="$1"
  case "$file" in
    *) echo "${THRESHOLD_OVERRIDE:-1}" ;;
  esac
}

# Per-file coverage thresholds for files with async state machine IL branches
# that cannot reach 100% in unit tests. Default is 1 (100%).
# Each entry MUST have a comment explaining why it can't reach 100%.
get_coverage_threshold() {
  local file="$1"
  case "$file" in
    *) echo "${THRESHOLD_OVERRIDE:-1}" ;;
  esac
}

FAILED=0

# ============================================================
# REPORT REUSE + TIER SCOPING (Phase 50, plan 50-04)
# ============================================================
# This block changes WHERE THE NUMBERS COME FROM. It does not change a single
# accept/reject decision: the exclusion function, the per-file threshold logic, the
# override table and the diff-range logic above are all untouched.
#
# WHY. In CI this script was the single most expensive step in the entire pipeline --
# 343 s, 38% of the critical-path job. Essentially all of it was recomputation: by the
# time it ran, the SAME JOB had already produced a backend Cobertura report, a
# timesheet coverage report and a Compass coverage report. This script threw all three
# away and re-ran the suites from scratch just to read per-file rates.
#
# So: when a caller names an already-produced report, read it. When no caller names
# one, produce it exactly as before -- that is the local pre-push path, and it must not
# change.
#
#   COVERAGE_BACKEND_REPORT           path to a .cobertura.xml, or a directory to search
#   COVERAGE_REPORT_WEB_COMPASS       path to web/compass's coverage-final.json
#   COVERAGE_TIER                     all (default) | backend | web/compass
#
# ** COVERAGE_BACKEND_REPORT: ONLY EVER PASS A REPORT PRODUCED WITHOUT
#    `--coverage-settings coverage.settings.xml`. **
# That settings file's <Exclude> list is far larger than the 33-entry exclusion set
# below -- it also drops NotificationService.cs, SesEmailSender.cs, both Slack
# processor services, the storage services and ~25 more. Those files are excluded from
# the AGGREGATE line-rate denominator deliberately, but they remain subject to the
# PER-FILE floor enforced here. Feed this variable a settings-filtered report and every
# one of those files disappears from it -- silently exempting them. That is a weakening,
# not a speed-up. `ci.yml` therefore does NOT reuse its aggregate backend report, and
# says so at the call site. (It fails closed rather than exempting, because a file with
# no entry is reported as "No test coverage found" -- which is how the mistake was
# caught in run 30721464667 instead of shipping.)
#
# NO SILENT FALLBACK. A named report that does not exist, or that yields no per-file
# data, is a HARD FAILURE naming the path. Quietly re-running the suite instead would
# make broken wiring look like a slow-but-working gate -- which is the entire failure
# family this phase exists to remove. Fail loudly; the operator fixes the wiring.
COVERAGE_TIER="${COVERAGE_TIER:-all}"

case "$COVERAGE_TIER" in
  all|backend|web/compass) ;;
  *)
    fail "Unrecognised COVERAGE_TIER '${COVERAGE_TIER}'. Expected one of: all, backend, web/compass."
    exit 1
    ;;
esac

if [ "$COVERAGE_TIER" != "all" ]; then
  info "Tier scope: ${COVERAGE_TIER} (other tiers are enforced by their own invocation)."
fi

# Map an app path to its report-override variable name: web/compass -> WEB_COMPASS.
app_report_var() {
  local app="$1"
  echo "COVERAGE_REPORT_$(echo "$app" | tr 'a-z/-' 'A-Z__')"
}

# Should this tier run in this invocation?
tier_selected() {
  [ "$COVERAGE_TIER" = "all" ] || [ "$COVERAGE_TIER" = "$1" ]
}

# ============================================================
# FRONTEND COVERAGE CHECK
# ============================================================
# Per-MODULE front-end enforcement (Phase 49). This block used to match only
# web/timesheet/src and read only the timesheet coverage JSON, so a changed file in any other
# front-end matched NOTHING and the script simply PASSED -- a path regex that matches zero files
# does not fail, it succeeds silently. Compass arrives with five developers behind this gate, so
# the block is generalised rather than copy-pasted: the app root is derived from the changed path,
# each app's coverage command runs in its own directory, and each app's own coverage JSON is read.
#
# The OOTO SPA (web/ooto) stays OUT of per-file enforcement on purpose: it is an isolated nested
# install with its own vitest coverage config, and this gate has never measured it. Enrolling it
# would demand a coverage floor on a tree nobody has measured -- a separate decision with its own
# cost, not a side effect of adding Compass.
FRONTEND_APPS="web/compass"

FRONTEND_FILES=""
while IFS= read -r f; do
  [ -z "$f" ] && continue
  if [[ "$f" =~ ^web/compass/src/.*\.(ts|tsx)$ ]]; then
    if ! is_excluded "$f"; then
      FRONTEND_FILES="${FRONTEND_FILES}${f}"$'\n'
    fi
  fi
done <<< "$CHANGED"
FRONTEND_FILES=$(echo "$FRONTEND_FILES" | sed '/^$/d')

if [ -z "$FRONTEND_FILES" ]; then
  info "No frontend source files changed -- skipping frontend coverage."
else
  for APP in $FRONTEND_APPS; do
    if ! tier_selected "$APP"; then
      continue
    fi

    APP_FILES=$(echo "$FRONTEND_FILES" | grep "^${APP}/" || true)
    if [ -z "$APP_FILES" ]; then
      # SAY SO rather than falling silent. A tier that enforced nothing must announce it,
      # because "no output" and "nothing to do" have been confused here before.
      [ "$COVERAGE_TIER" != "all" ] && info "${APP}: 0 changed file(s) to enforce."
      continue
    fi

    # PRINT what is being enforced, not just the verdict. A gate must prove it EXECUTED --
    # silence has been mistaken for success in this repo five separate times.
    info "Checking ${APP} coverage for $(echo "$APP_FILES" | wc -l | tr -d ' ') file(s):"
    while IFS= read -r listed; do
      [ -n "$listed" ] && info "  - ${listed}"
    done <<< "$APP_FILES"

    APP_REPORT_VAR=$(app_report_var "$APP")
    SUPPLIED_REPORT="${!APP_REPORT_VAR:-}"

    if [ -n "$SUPPLIED_REPORT" ]; then
      # REUSE PATH. The caller has already produced this app's coverage in an earlier
      # step; re-running vitest here would recompute it for nothing.
      if [ ! -f "$SUPPLIED_REPORT" ]; then
        fail "${APP}: ${APP_REPORT_VAR} names '${SUPPLIED_REPORT}', which does not exist. Refusing to silently re-run the suite -- fix the wiring."
        FAILED=1
        continue
      fi
      COVERAGE_JSON="$SUPPLIED_REPORT"
      info "${APP}: reusing coverage report ${COVERAGE_JSON} (not re-running the suite)."
    else
      # PRODUCE PATH -- unchanged local pre-push behaviour.
      pushd "$REPO_ROOT/$APP" > /dev/null
      npx vitest run --coverage 2>&1 | tail -5 || true
      popd > /dev/null

      COVERAGE_JSON="$REPO_ROOT/$APP/coverage/coverage-final.json"
      info "${APP}: produced coverage report ${COVERAGE_JSON}."
    fi

    if [ ! -f "$COVERAGE_JSON" ]; then
      # FAIL, never skip. A missing coverage file is exactly the shape of the incident where an
      # invalid settings file aborted every coverage run for five waves and nothing noticed.
      fail "${APP}: coverage JSON not generated at $COVERAGE_JSON -- the coverage run did not produce results, so nothing was measured."
      FAILED=1
      continue
    fi

    # A report that exists but holds nothing is the same failure wearing a different hat.
    if [ "$(jq -r 'keys | length' "$COVERAGE_JSON" 2>/dev/null || echo 0)" -eq 0 ]; then
      fail "${APP}: coverage report ${COVERAGE_JSON} names ZERO files -- nothing was measured."
      FAILED=1
      continue
    fi

    while IFS= read -r file; do
      [ -z "$file" ] && continue
      # coverage-final.json keys are HOST-NATIVE absolute paths, so this must not assume
      # either side's separator -- see scripts/lib/coverage-json-match.sh. The previous
      # `grep -F` over raw keys matched NOTHING on Windows (backslash keys vs a
      # forward-slash suffix) and reported every changed front-end file as uncovered while
      # the report said 100%. It was also a SUBSTRING match, so it could return a
      # different file's coverage; the shared matcher requires a path-segment boundary.
      REL_FROM_APP="${file#${APP}/}"

      COVERAGE_KEY=$(coverage_json_key_for_file "$COVERAGE_JSON" "$REL_FROM_APP")

      if [ -z "$COVERAGE_KEY" ]; then
        fail "No test coverage found for $file"
        FAILED=1
        continue
      fi

      UNCOVERED=$(jq -r --arg key "$COVERAGE_KEY" \
        '.[$key].s | to_entries[] | select(.value == 0) | .key' \
        "$COVERAGE_JSON" 2>/dev/null || true)

      TOTAL_STMTS=$(jq -r --arg key "$COVERAGE_KEY" '.[$key].s | length' "$COVERAGE_JSON" 2>/dev/null || echo "0")

      if [ -n "$UNCOVERED" ]; then
        UNCOVERED_COUNT=$(echo "$UNCOVERED" | wc -l | tr -d ' ')
        COVERED_STMTS=$((TOTAL_STMTS - UNCOVERED_COUNT))

        # Check frontend per-file threshold (similar to backend get_coverage_threshold)
        FE_THRESHOLD=$(get_frontend_coverage_threshold "$file")
        if [ "$FE_THRESHOLD" != "1" ] && [ "$TOTAL_STMTS" -gt 0 ]; then
          ACTUAL_RATE=$(echo "scale=4; $COVERED_STMTS / $TOTAL_STMTS" | bc)
          PASSES=$(echo "$ACTUAL_RATE >= $FE_THRESHOLD" | bc -l)
          if [ "$PASSES" -eq 1 ]; then
            info "$file: ${COVERED_STMTS}/${TOTAL_STMTS} covered (threshold=${FE_THRESHOLD})"
            continue
          fi
        fi

        # Get line numbers for uncovered statements
        UNCOVERED_LINES=$(for stmt_id in $UNCOVERED; do
          jq -r --arg key "$COVERAGE_KEY" --arg id "$stmt_id" \
            '.[$key].statementMap[$id].start.line' "$COVERAGE_JSON" 2>/dev/null
        done | sort -n | uniq | tr '\n' ',' | sed 's/,$//')
        fail "$file: $UNCOVERED_COUNT uncovered statement(s) at line(s): $UNCOVERED_LINES"
        FAILED=1
      else
        # Report the MEASURED number, not a bare verdict.
        info "$file: 100% covered (${TOTAL_STMTS}/${TOTAL_STMTS} statements)"
      fi
    done <<< "$APP_FILES"
  done
fi

# ============================================================
# BACKEND COVERAGE CHECK
# ============================================================
BACKEND_FILES=""
while IFS= read -r f; do
  [ -z "$f" ] && continue
  # Only api/**/*.cs files (NOT test projects). Both test projects now live OUTSIDE the
  # API root (tests/unit, tests/integration), so ^api/ already excludes them -- but the two
  # negative guards stay on purpose: ^tests/ documents the new layout, and the
  # /(Tests|IntegrationTests)/ clause stops a future helper directory UNDER api/ that happens
  # to be named for tests from leaking into the per-file backend gate.
  if [[ "$f" =~ ^api/.*\.cs$ ]] && [[ ! "$f" =~ ^tests/ ]] && [[ ! "$f" =~ /(Tests|IntegrationTests)/ ]]; then
    if ! is_excluded "$f"; then
      BACKEND_FILES="${BACKEND_FILES}${f}"$'\n'
    fi
  fi
done <<< "$CHANGED"
BACKEND_FILES=$(echo "$BACKEND_FILES" | sed '/^$/d')

if ! tier_selected "backend"; then
  : # another invocation owns the backend tier
elif [ -z "$BACKEND_FILES" ]; then
  info "No backend source files changed -- skipping backend coverage."
  [ "$COVERAGE_TIER" != "all" ] && info "backend: 0 changed file(s) to enforce."
else
  info "Checking backend coverage for $(echo "$BACKEND_FILES" | wc -l | tr -d ' ') file(s)..."

  COVERAGE_DIR=""
  if [ -n "${COVERAGE_BACKEND_REPORT:-}" ]; then
    # REUSE PATH. The caller already ran the unit suite with coverage; re-running it here
    # is the single most expensive redundant step in the pipeline.
    if [ -d "$COVERAGE_BACKEND_REPORT" ]; then
      COBERTURA_XML=$(find "$COVERAGE_BACKEND_REPORT" -name "*.cobertura.xml" -type f 2>/dev/null | head -1 || true)
      if [ -z "$COBERTURA_XML" ]; then
        fail "backend: COVERAGE_BACKEND_REPORT names directory '${COVERAGE_BACKEND_REPORT}' but it holds no *.cobertura.xml. Refusing to silently re-run the suite -- fix the wiring."
        FAILED=1
      fi
    elif [ -f "$COVERAGE_BACKEND_REPORT" ]; then
      COBERTURA_XML="$COVERAGE_BACKEND_REPORT"
    else
      fail "backend: COVERAGE_BACKEND_REPORT names '${COVERAGE_BACKEND_REPORT}', which is neither a file nor a directory. Refusing to silently re-run the suite -- fix the wiring."
      FAILED=1
      COBERTURA_XML=""
    fi
    [ -n "${COBERTURA_XML:-}" ] && info "backend: reusing coverage report ${COBERTURA_XML} (not re-running the unit suite)."
  else
    # PRODUCE PATH -- unchanged local pre-push behaviour.
    COVERAGE_DIR="/tmp/coverage-results-$$"
    rm -rf "$COVERAGE_DIR"

    # Run dotnet test with Microsoft.Testing.Extensions.CodeCoverage (MTP runner)
    dotnet test --project "$REPO_ROOT/tests/unit" \
      --coverage --coverage-output-format cobertura \
      --results-directory "$COVERAGE_DIR" 2>&1 | tail -5 || true

    # Find the generated Cobertura XML (MTP generates UUID-named files)
    COBERTURA_XML=$(find "$COVERAGE_DIR" -name "*.cobertura.xml" -type f 2>/dev/null | head -1 || true)
    [ -n "$COBERTURA_XML" ] && info "backend: produced coverage report ${COBERTURA_XML}."
  fi

  if [ -z "${COBERTURA_XML:-}" ]; then
    if [ -z "${COVERAGE_BACKEND_REPORT:-}" ]; then
      fail "Cobertura XML not generated. Ensure Microsoft.Testing.Extensions.CodeCoverage is installed."
      FAILED=1
    fi
  else
    # Diagnostic pre-pass: distinguish "the report is broken / path-matching is broken"
    # (issue #480 defect 1's shape) from "these files genuinely lack coverage". Computed
    # BEFORE the per-file loop below so the loud diagnostic prints before, not interleaved
    # with, the individual failures it explains. cobertura_line_rates_for_file is a cheap
    # grep/sed pass over an already-produced XML file, so calling it here and again below
    # costs nothing like re-running the suite.
    BACKEND_FILES_COUNT=$(echo "$BACKEND_FILES" | grep -c . || true)
    BACKEND_FILES_RESOLVED=0
    while IFS= read -r file; do
      [ -z "$file" ] && continue
      if [ -n "$(cobertura_line_rates_for_file "$COBERTURA_XML" "$file")" ]; then
        BACKEND_FILES_RESOLVED=$((BACKEND_FILES_RESOLVED + 1))
      fi
    done <<< "$BACKEND_FILES"

    if [ "$BACKEND_FILES_COUNT" -gt 0 ] && [ "$BACKEND_FILES_RESOLVED" -eq 0 ]; then
      fail "0 of $BACKEND_FILES_COUNT changed backend file(s) resolved ANY Cobertura entry -- this looks like a broken report or a path-matching bug, not a real coverage gap."
    fi

    while IFS= read -r file; do
      [ -z "$file" ] && continue

      # Matched by REPO-RELATIVE SUFFIX against the Cobertura filename, not by composing
      # an absolute path here -- see scripts/lib/cobertura-match.sh for why (issue #480
      # defect 1: Cobertura always records the HOST-NATIVE absolute path, which a
      # Git-Bash-composed POSIX-style path on Windows never matched). That helper also
      # preserves the compiler-generated-class filter this loop used to apply inline.
      LINE_RATES=$(cobertura_line_rates_for_file "$COBERTURA_XML" "$file")

      if [ -z "$LINE_RATES" ]; then
        fail "No test coverage found for $file"
        FAILED=1
        continue
      fi

      # Check if ALL class entries for this file have line-rate = 1
      ALL_COVERED=true
      WORST_RATE=""
      while IFS= read -r rate; do
        if [ "$rate" != "1" ]; then
          ALL_COVERED=false
          WORST_RATE="$rate"
        fi
      done <<< "$LINE_RATES"

      if $ALL_COVERED; then
        info "$file: 100% covered (line-rate=1.0)"
      else
        # Check per-file threshold (for files with async state machine branches)
        THRESHOLD=$(get_coverage_threshold "$file")
        MEETS_THRESHOLD=true
        while IFS= read -r rate; do
          if [ "$(echo "$rate >= $THRESHOLD" | bc 2>/dev/null)" != "1" ]; then
            MEETS_THRESHOLD=false
          fi
        done <<< "$LINE_RATES"

        if $MEETS_THRESHOLD && [ "$THRESHOLD" != "1" ]; then
          PERCENT=$(echo "$WORST_RATE * 100" | bc 2>/dev/null || echo "$WORST_RATE")
          info "$file: ${PERCENT}% covered (threshold=${THRESHOLD}, async state machine branches)"
        else
          PERCENT=$(echo "$WORST_RATE * 100" | bc 2>/dev/null || echo "$WORST_RATE")
          fail "$file: insufficient coverage (line-rate=$WORST_RATE, ${PERCENT}%)"
          FAILED=1
        fi
      fi
    done <<< "$BACKEND_FILES"
  fi

  # Cleanup -- only ever our own temp directory. On the reuse path COVERAGE_DIR is empty
  # and the caller's report must survive: it is uploaded as a CI artifact.
  [ -n "$COVERAGE_DIR" ] && rm -rf "$COVERAGE_DIR"
fi

# ============================================================
# RESULT
# ============================================================
if [ "$FAILED" -ne 0 ]; then
  fail "Coverage check FAILED -- new/changed code must have 100% test coverage."
  exit 1
else
  info "Coverage check PASSED -- all changed files have 100% coverage."
  exit 0
fi
