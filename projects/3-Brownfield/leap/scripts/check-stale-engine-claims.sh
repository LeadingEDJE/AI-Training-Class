#!/usr/bin/env bash
#
# Guard for the Phase 4 comment routing (docs/TEST-STRATEGY.md, pyramid-rebalance F4).
#
# Since issue #416, `enginesFor()` in .github/scripts/detect-modules.cjs returns chromium ALONE for
# `pull_request`; Firefox and WebKit run only post-merge and on the Tuesday schedule. Thirty-three
# test files nonetheless claimed, in a comment or in a `test.describe` title, that they "run in all
# 3 browsers". Nobody updated them because nothing could: it is CI configuration restated in source,
# with no mechanism keeping the copies honest. This script is that mechanism.
#
# ---------------------------------------------------------------------------------------------
# WHY THIS SCRIPT'S FAIL-OPEN MODE IS THE OPPOSITE OF THE OTHER verify-* SCRIPTS' — READ BEFORE
# EDITING. Those iterate a list and can pass over an empty one. This one asserts an ABSENCE, so a
# vacuous pass does not come from an empty loop: it comes from searching the wrong corpus. A path
# that matches no files reports zero violations and exits 0 — indistinguishable from success. So the
# corpus size is asserted against a floor BEFORE any pattern runs, and the patterns are themselves
# proven to fire (see "Proving it RED" at the bottom).
# ---------------------------------------------------------------------------------------------
set -euo pipefail

cd "$(dirname "$0")/.."

# ---- the corpus, from git rather than a glob so an untracked stray cannot skew it ----
#
# Newline-delimited rather than a bash array on purpose: macOS ships bash 3.2, which has no
# `mapfile`, and this script has to run identically on a developer's Mac and on CI's ubuntu.
CORPUS=$(git ls-files 'web/*/tests/*.ts' 'web/*/tests/*.tsx' | sort)
corpus_count=$(printf '%s\n' "$CORPUS" | grep -c . || true)

# Floor, not an exact count: test files are added routinely and this guard must not become a
# tripwire on that. It exists only to prove the search reached the tree at all. Measured 2026-09-14
# (this trimmed Compass-only copy has no web/timesheet tests, so the floor is set well below its
# 106-file corpus rather than the pre-trim repo's 200).
CORPUS_FLOOR=90

if [ "$corpus_count" -lt "$CORPUS_FLOOR" ]; then
  echo "FAIL: scanned only $corpus_count test files, below the floor of $CORPUS_FLOOR." >&2
  echo "      The corpus paths are wrong or the tree moved — this check would pass over nothing." >&2
  exit 1
fi

# Each row: <human name> | <extended regex>
#
# These match the claim, not one phrasing of it. The original 33 sites used four different
# wordings ("These tests run", "these tests run", the compass-project listing, and the title
# suffix), which is precisely why a grep for one exact line would have missed a quarter of them.
#
# DELIBERATELY NOT MATCHED, do not "complete" these patterns without reading TEST-STRATEGY.md Rule 5:
# 22 Compass spec headers open with `// CRITICAL: runs under the compass-chromium-critical project.`
# That is a restatement of `testMatch` — weak for the same reason — but it is TRUE today, so it is
# not misinformation and it is scheduled for the judged comment pass instead. Widening a pattern to
# `runs under .* project` turns this gate red on 22 files that nobody has agreed to change yet.
PATTERNS=(
  "comment: runs in all 3 browsers|run(s)? in all (3|three) browsers"
  "comment: names the demoted engines as coverage|CRITICAL:.*(Chromium, Firefox|firefox,webkit|Firefox, WebKit)"
  "title: [critical - all browsers]|\[critical[^]]*all browsers[^]]*\]"
)

pattern_count=${#PATTERNS[@]}
if [ "$pattern_count" -eq 0 ]; then
  echo "FAIL: zero patterns — this script would pass over nothing." >&2
  exit 1
fi

echo "Scanning $corpus_count test files (floor $CORPUS_FLOOR) for $pattern_count stale-claim patterns..."
echo

violations=0
for row in "${PATTERNS[@]}"; do
  name=${row%%|*}
  regex=${row#*|}

  # `|| true` is safe HERE and only here: grep exits 1 on no-match, which is the passing case for an
  # absence check. It is not swallowing an error — the corpus floor above is what proves the search ran.
  # /dev/null is passed BEFORE the xargs-appended paths so grep always sees two or more files and
  # therefore always prefixes its output with the filename, even if the corpus ever shrank to one.
  hits=$(printf '%s\n' "$CORPUS" | xargs grep -nE "$regex" /dev/null 2>/dev/null || true)

  if [ -n "$hits" ]; then
    echo "FAIL: $name"
    printf '%s\n' "$hits" | sed 's/^/      /'
    echo
    violations=$((violations + 1))
  else
    echo "  ok  $name"
  fi
done

echo
if [ "$violations" -gt 0 ]; then
  cat >&2 <<'MSG'
FAIL: a test file claims it runs on Firefox or WebKit.

A pull request runs chromium only (enginesFor() in .github/scripts/detect-modules.cjs). State the
engine set in the workflow, which is the only place that can be true, and not in the source it
gates. If a test genuinely needs a demoted engine, say WHY in one line — for example
web/compass/tests/e2e/nav-keyboard.critical.spec.ts, which exists for a WebKit-only defect — and
phrase it as a requirement, not as a description of what CI does.
MSG
  exit 1
fi

echo "PASS: $corpus_count test files scanned, no stale engine claims."

# Proving it RED (do this after editing a pattern — an absence check that cannot fire is decoration):
#
#   f=web/compass/tests/e2e/smoke.critical.spec.ts
#   printf '// CRITICAL: These tests run in all 3 browsers (Chromium, Firefox, WebKit).\n' | cat - "$f" > /tmp/x && cp /tmp/x "$f"
#   bash scripts/check-stale-engine-claims.sh   # must FAIL, naming the file and line
#   git checkout -- "$f"
