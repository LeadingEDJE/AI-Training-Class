#!/usr/bin/env bash
set -euo pipefail

# One-context invariant guard (Phase 49, plan 49-01).
#
# LEAP is ONE application with submodules. Compass tables live in a `compass` Postgres schema for
# organizational clarity, but they are mapped on the SINGLE existing LeapDbContext, recorded in the
# SINGLE default migration-history table. That is an owner decision, and it buys a specific property:
# a single SaveChangesAsync can span modules, cross-schema joins work in one query on one connection,
# and cross-module navigation properties and foreign keys are possible.
#
# A second DbContext would destroy all of that. So would a second migration-history table (two
# migration timelines that can diverge) or a model-wide default schema (which silently re-homes every
# existing table). This script fails loudly if any of the three regress.
#
# It PRINTS every measured number rather than only a verdict. A gate that merely does not complain is
# indistinguishable from a gate that never ran -- this repository has been bitten by that five times.

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m'

info() { echo -e "${GREEN}[one-context]${NC} $*"; }
fail() { echo -e "${RED}[one-context]${NC} $*"; }

# Comments are stripped before counting. This file, the migration's explanatory prose, and the
# module READMEs all legitimately NAME these forbidden constructs in order to explain why they are
# forbidden -- counting comment text would make the guard self-invalidating.
code_only() {
  find api -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
    | xargs -0 grep -h "$1" 2>/dev/null \
    | grep -v '^[[:space:]]*//' \
    | grep -v '^[[:space:]]*\*' \
    | grep -c . || true
}

FAILED=0

# Invariant 1: exactly one EF context registration in the composition root.
CONTEXT_REGISTRATIONS=$(code_only 'AddDbContext')
info "EF context registrations (AddDbContext):        ${CONTEXT_REGISTRATIONS}  (required: exactly 1)"
if [ "$CONTEXT_REGISTRATIONS" -ne 1 ]; then
  fail "INVARIANT VIOLATED: one-context -- expected exactly 1 AddDbContext registration, found ${CONTEXT_REGISTRATIONS}."
  fail "  A second EF context means two SaveChangesAsync boundaries and two migration histories."
  FAILED=1
fi

# Invariant 2: no custom migrations-history table. One model, one __EFMigrationsHistory in public.
HISTORY_TABLE_CONFIG=$(code_only 'MigrationsHistoryTable')
info "Custom migrations-history configurations:       ${HISTORY_TABLE_CONFIG}  (required: exactly 0)"
if [ "$HISTORY_TABLE_CONFIG" -ne 0 ]; then
  fail "INVARIANT VIOLATED: one-migration-history -- found ${HISTORY_TABLE_CONFIG} custom history-table configuration(s)."
  fail "  A second migration history is two timelines that can diverge."
  FAILED=1
fi

# Invariant 3: no model-wide default schema. Schemas are per-entity opt-in, never global.
DEFAULT_SCHEMA_CONFIG=$(code_only 'HasDefaultSchema')
info "Model-wide default-schema configurations:       ${DEFAULT_SCHEMA_CONFIG}  (required: exactly 0)"
if [ "$DEFAULT_SCHEMA_CONFIG" -ne 0 ]; then
  fail "INVARIANT VIOLATED: no-default-schema -- found ${DEFAULT_SCHEMA_CONFIG} model-wide default-schema configuration(s)."
  fail "  A model-wide default schema silently re-homes every existing table."
  FAILED=1
fi

# Invariant 4: no Compass-specific EF context type is ever declared.
COMPASS_CONTEXT_DECLS=$(find api -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' -print0 \
  | xargs -0 grep -hE '(class|record|interface)[[:space:]]+CompassDbContext' 2>/dev/null \
  | grep -v '^[[:space:]]*//' \
  | grep -v '^[[:space:]]*\*' \
  | grep -c . || true)
info "Compass-specific EF context declarations:       ${COMPASS_CONTEXT_DECLS}  (required: exactly 0)"
if [ "$COMPASS_CONTEXT_DECLS" -ne 0 ]; then
  fail "INVARIANT VIOLATED: no-compass-context -- found ${COMPASS_CONTEXT_DECLS} Compass-specific context declaration(s)."
  fail "  Compass entities belong on the existing LeapDbContext. This reverses an owner decision."
  FAILED=1
fi

if [ "$FAILED" -ne 0 ]; then
  fail "One-context invariants FAILED. See the named invariant(s) above."
  exit 1
fi

info "All four one-context invariants hold."
exit 0
