#!/usr/bin/env bash
set -euo pipefail

# CI coverage enforcement.
#
# Runs the same per-file 100% coverage check as the local pre-push hook, but
# scoped to the branch-vs-main diff (origin/main...HEAD) rather than the
# pre-push hook's "commits not yet on the remote tracking branch" scope.
#
# This is a thin wrapper around scripts/check-coverage.sh — the exclusion
# list and threshold logic live there as the single source of truth.
#
# Usage:
#   bash scripts/check-coverage-ci.sh                 # diffs origin/main...HEAD
#   BASE_REF=origin/release bash scripts/check-coverage-ci.sh
#
# Required: origin/main (or $BASE_REF) must be fetched. In GitHub Actions,
# set fetch-depth: 0 on actions/checkout, or fetch the base ref explicitly.
#
# ---------------------------------------------------------------------------
# Report reuse and tier scoping (Phase 50, plan 50-04). Both are optional; with
# neither set the behaviour is exactly what it always was.
#
#   COVERAGE_TIER=backend|web/compass|all   (default: all)
#       Enforce one tier only, so each tier's check can run alongside the job
#       that produced its report. An unrecognised value is a hard failure.
#
#   COVERAGE_BACKEND_REPORT=<file-or-dir>
#       Read this already-produced Cobertura report instead of re-running the
#       unit suite. A directory is searched for *.cobertura.xml.
#
#   COVERAGE_REPORT_WEB_COMPASS=<coverage-final.json>
#       Read this already-produced per-app report instead of re-running vitest.
#
# A named report that is missing, or that names zero files, is a HARD FAILURE.
# There is deliberately NO silent fallback to re-running the suite: broken
# wiring must look broken, not merely slow.
#
# Example (CI, backend tier reusing the unit suite's report):
#   COVERAGE_TIER=backend COVERAGE_BACKEND_REPORT=./coverage-results \
#     bash scripts/check-coverage-ci.sh
# ---------------------------------------------------------------------------

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

BASE_REF="${BASE_REF:-origin/main}"

# CI bar: 98% per-file floor (vs. 100% on local pre-push). The pre-push hook
# keeps 100% as an aim-higher local discipline; this is the merge gate.
THRESHOLD_OVERRIDE="${THRESHOLD_OVERRIDE:-0.98}"

# Ensure the base ref exists locally so git diff can resolve it.
if ! git -C "$REPO_ROOT" rev-parse --verify --quiet "$BASE_REF" >/dev/null; then
  echo "error: base ref '$BASE_REF' not found locally" >&2
  echo "In GitHub Actions, set fetch-depth: 0 on actions/checkout or run 'git fetch origin main' first." >&2
  exit 1
fi

# Pass the optional reuse/scoping inputs through explicitly. `exec` would inherit any
# already-exported variable anyway, but naming them here is what makes them
# discoverable -- an undocumented pass-through is how a capability goes unused.
COVERAGE_TIER="${COVERAGE_TIER:-all}"
COVERAGE_BACKEND_REPORT="${COVERAGE_BACKEND_REPORT:-}"
COVERAGE_REPORT_WEB_COMPASS="${COVERAGE_REPORT_WEB_COMPASS:-}"

export BASE_REF THRESHOLD_OVERRIDE
export COVERAGE_TIER COVERAGE_BACKEND_REPORT
export COVERAGE_REPORT_WEB_COMPASS
exec bash "$REPO_ROOT/scripts/check-coverage.sh"
