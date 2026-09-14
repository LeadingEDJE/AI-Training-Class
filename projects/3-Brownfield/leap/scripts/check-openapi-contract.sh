#!/usr/bin/env bash
# The #141 OpenAPI diff gate. Two checks over the /api/compass/v1 boundary:
#
#   1. FRESHNESS — the committed snapshot equals what the code generates right now. A stale snapshot
#      means someone changed the v1 surface without regenerating, so the diff in check 2 would be a
#      lie. Read the printed diff; do not trust an exit code (this repo has shipped gates that passed
#      because they never ran).
#
#   2. NO BREAKING CHANGE vs the base branch — additive changes stay in v1; anything breaking
#      (removing/renaming a field or path, narrowing a type, making an optional field required, a
#      new required parameter, ...) must go to v2 instead (FR-082, ADR-004 D7/E2). oasdiff classifies
#      the change semantically; this is the check the design contract said review alone could not give.
#
# Usage: scripts/check-openapi-contract.sh [BASE_REF]   (BASE_REF defaults to origin/main)
# Requires: dotnet, python3, and oasdiff on PATH (CI installs oasdiff; locally: see the error below).
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

BASE_REF="${1:-origin/main}"
SNAPSHOT="api/Modules/Compass/Contracts/compass-v1.openapi.json"
EMITTED="api/obj/openapi/LeadingEDJE.Leap.Api_compass-v1.json"

# Normalize EMITTED into $2 exactly as generate-openapi-snapshot.sh writes the committed snapshot,
# via the shared normalizer (LF newlines + embedded-CR stripped from string values), so the freshness
# diff is not fooled by CRLF vs LF between a Windows dev and the Linux runner. `python3` explicitly,
# matching every other script in this repo (check-migrate-hook.sh, verify-helm-preview.sh, ci.yml) —
# a bare `python` shim is often absent on Linux. On Windows Git Bash, add a `python3` shim on PATH.
normalize() { python3 "$REPO_ROOT/scripts/lib/normalize-openapi.py" "$1" "$2"; }

# ── Check 1: freshness ───────────────────────────────────────────────────────────────────────────
echo "[openapi-gate] regenerating the compass-v1 document from the code..."
dotnet build api/LeadingEDJE.Leap.Api.csproj -p:OpenApiGenerateDocumentsOnBuild=true --nologo -v quiet

if [[ ! -f "$EMITTED" ]]; then
  echo "[openapi-gate] ERROR: no emitted document at $EMITTED — is AddOpenApi(\"compass-v1\", ...) still registered?" >&2
  exit 1
fi

FRESH="$(mktemp)"
trap 'rm -f "$FRESH" "${BASE_SNAPSHOT:-}"' EXIT
normalize "$EMITTED" "$FRESH"

if ! diff -u "$SNAPSHOT" "$FRESH" > /tmp/openapi-freshness.diff 2>&1; then
  echo "[openapi-gate] ❌ FRESHNESS: the committed snapshot is stale — the /api/compass/v1 surface changed but"
  echo "               $SNAPSHOT was not regenerated. Run:  bash scripts/generate-openapi-snapshot.sh"
  echo "               and commit the result in the same change. Diff:"
  cat /tmp/openapi-freshness.diff
  exit 1
fi
echo "[openapi-gate] ✅ FRESHNESS: committed snapshot matches the generated surface."

# ── Check 2: no breaking change vs the base branch ───────────────────────────────────────────────
if ! command -v oasdiff > /dev/null 2>&1; then
  echo "[openapi-gate] ERROR: oasdiff is not installed." >&2
  echo "               CI installs it; locally:  go install github.com/tufin/oasdiff@latest" >&2
  echo "               or:  docker run --rm -v \"\$PWD:/w\" tufin/oasdiff breaking /w/<base> /w/<rev>" >&2
  exit 1
fi

# ── The base ref itself must be REACHABLE ────────────────────────────────────────────────────────
# Two failures look identical to `git cat-file -e "$BASE_REF:$SNAPSHOT"` and mean opposite things:
#
#   (a) $BASE_REF does not resolve here at all  — a shallow clone, a fork runner, a local run before
#       `git fetch`. Nothing was compared. This MUST fail.
#   (b) $BASE_REF resolves and genuinely has no snapshot — a brand-new contract. Nothing to compare
#       against yet, which is legitimately a skip.
#
# Collapsing the two printed "✅ gate passed" for (a) as well, so renaming or moving $SNAPSHOT — or
# running anywhere origin/main is not fetched — silently disabled the breaking-change half while the
# job stayed green. That is the fail-open shape docs/platform/adding-a-module.md records finding
# eight times across Phases 48-49, and this script's own header warns not to trust an exit code.
if ! git rev-parse --verify --quiet "${BASE_REF}^{commit}" > /dev/null 2>&1; then
  echo "[openapi-gate] ❌ BASE REF UNREACHABLE: '$BASE_REF' does not resolve in this clone, so the" >&2
  echo "               additive-only check could not run at all. This is a FAILURE, not a skip —" >&2
  echo "               passing here would report a green gate that compared nothing." >&2
  echo "               In CI: check actions/checkout has fetch-depth: 0." >&2
  echo "               Locally:  git fetch origin main" >&2
  exit 1
fi

if ! git cat-file -e "$BASE_REF:$SNAPSHOT" 2>/dev/null; then
  echo "[openapi-gate] ℹ️  $BASE_REF resolves but carries no $SNAPSHOT (new contract) — nothing to"
  echo "               diff against; skipping the breaking check. This is the ONLY skip this gate"
  echo "               allows, and it stops applying the moment the snapshot lands on the base."
  echo "[openapi-gate] ✅ gate passed."
  exit 0
fi

BASE_SNAPSHOT="$(mktemp)"
git show "$BASE_REF:$SNAPSHOT" > "$BASE_SNAPSHOT"

echo "[openapi-gate] running oasdiff breaking ($BASE_REF → working tree)..."
# --fail-on WARN, NOT ERR. `oasdiff breaking` only ever reports BREAKING changes; ERR vs WARN is how
# severe the break is, not whether it is one. This contract is additive-only (FR-082), so ANY breaking
# change must fail — and removing a response field, which every consumer of the field breaks on, is
# classified WARN (`response-optional-property-removed`), not ERR. Failing only on ERR would wave it
# through. Verified by construction: a removed field and a removed path both fail at WARN; an added
# optional field and an identical spec both pass. (`oasdiff breaking` also exits 0 without --fail-on
# even when it prints errors, so the flag is what makes this a gate at all.)
if ! oasdiff breaking "$BASE_SNAPSHOT" "$SNAPSHOT" --fail-on WARN; then
  echo "[openapi-gate] ❌ BREAKING: the change above breaks the /api/compass/v1 contract. v1 is additive-only —"
  echo "               make the change additive, or introduce /api/compass/v2 with a support window (ADR-004)."
  exit 1
fi
echo "[openapi-gate] ✅ NO BREAKING CHANGE: the /api/compass/v1 change is additive."
echo "[openapi-gate] ✅ gate passed."
