#!/usr/bin/env bash
# coverage-json-match.sh -- match an app-relative file path against an Istanbul/v8
# `coverage-final.json`'s keys WITHOUT assuming either side's path convention.
#
# WHY THIS EXISTS (the FRONTEND twin of issue #480 defect 1)
#   #480 fixed the BACKEND half of this bug and stopped there. `check-coverage.sh`'s frontend
#   path had the identical defect and kept it:
#
#       REL_FROM_APP="${file#${APP}/}"                     # src/features/.../Foo.tsx
#       COVERAGE_KEY=$(jq -r "keys[]" ... | grep -F "$REL_FROM_APP" | head -1)
#
#   Vitest/v8 records the HOST-NATIVE absolute path, which on Windows is
#   "C:\repo\web\compass\src\features\...\Foo.tsx" -- backslashes, drive letter -- never the
#   forward-slash form `git diff` produces. `grep -F` therefore matched nothing, and every
#   changed front-end file reported "No test coverage found" while the coverage report itself
#   said 100%. Measured on a real report: the forward-slash form matched 0 keys, the file was
#   at 100% statements/functions/branches.
#
#   That is the FOURTH instance of this shape in that one script (BSD-vs-GNU `grep -P`, the
#   Cobertura path mismatch, a generics regex that swallowed every generic type, and this).
#   All four failed CLOSED, which is why each one looked like a real coverage gap.
#
# WHAT IT FIXES BEYOND THE PLATFORM BUG
#   `grep -F` was a SUBSTRING match, so `src/Foo.tsx` would also have matched a key for
#   `src/OldFoo.tsx` and silently reported the WRONG file's coverage. This matches on a
#   PATH-SEGMENT BOUNDARY instead: the normalised key must either equal the suffix or end with
#   "/" + the suffix. The decoy case is asserted in coverage-json-match.selftest.sh.
#
# DEPENDENCY NOTE
#   This uses `jq`, unlike its sibling `cobertura-match.sh`, which is deliberately
#   dependency-free. That is not an inconsistency: `check-coverage.sh` already hard-fails
#   without `jq` at the top of the file, and its whole frontend path is built on it, so `jq`
#   is a settled requirement here rather than a new one. The selftest FAILS rather than skips
#   when `jq` is absent -- a skipped selftest is exactly the vacuous gate this file exists
#   because of.
#
# USAGE
#   . "${SCRIPT_DIR}/lib/coverage-json-match.sh"
#   key=$(coverage_json_key_for_file "$COVERAGE_JSON" "src/features/clients/Foo.tsx")
#
#   $1 = path to a readable coverage-final.json.
#   $2 = an APP-RELATIVE file path, always forward-slash (what `git diff --name-only`
#        produces, with the workspace prefix stripped by the caller).
#
#   Prints the matching key verbatim -- callers index the JSON with it, so it must NOT be
#   normalised on the way out. Prints nothing when there is no match; that is a normal
#   "nothing found" case the caller distinguishes by checking for empty output, and it does
#   NOT return non-zero, so a caller running under `set -e` is not taken down by it.

coverage_json_key_for_file() {
  local coverage_json="$1"
  local rel_path="$2"

  if [ -z "$coverage_json" ] || [ -z "$rel_path" ]; then
    echo "coverage_json_key_for_file: usage: coverage_json_key_for_file <coverage-final.json> <app-relative-path>" >&2
    return 1
  fi

  if [ ! -r "$coverage_json" ]; then
    echo "coverage_json_key_for_file: cannot read ${coverage_json}" >&2
    return 1
  fi

  # The whole match happens inside jq so the normalisation and the boundary test travel
  # together. `gsub("\\\\"; "/")` rewrites Windows separators; the two-way test after it is
  # what enforces the path-segment boundary the old substring grep did not.
  #
  # `// empty` keeps a no-match quiet: without it jq prints `null`, which is a non-empty
  # string and would read to the caller as a found key.
  jq -r --arg suffix "$rel_path" '
    [
      keys[]
      | select(
          (gsub("\\\\"; "/")) as $normalised
          | $normalised == $suffix or ($normalised | endswith("/" + $suffix))
        )
    ]
    | first
    // empty
  ' "$coverage_json" 2>/dev/null || true
}
