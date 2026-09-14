#!/usr/bin/env bash
# cobertura-match.selftest.sh -- fast, dependency-free fixture test of cobertura-match.sh.
#
# WHY THIS EXISTS
#   issue #480's defect 1 (a Windows-absolute-path vs POSIX-absolute-path mismatch) and the
#   third-of-its-kind BSD-vs-GNU grep bug it followed both had one thing in common: nothing
#   ran the affected script on more than one OS before it merged. This selftest is the
#   durable fix for that -- it runs cobertura_line_rates_for_file() against a synthetic
#   fixture on ubuntu-latest, macos-latest AND windows-latest in CI (see the
#   `script-portability` job in .github/workflows/ci.yml), so a regression in the matcher
#   fails a pull request instead of surfacing three months later on someone's Mac or Windows
#   machine.
#
#   Deliberately dependency-free: no dotnet, no npm, no jq, no bc. Only bash/grep/sed/tr,
#   which are present on GitHub's ubuntu-latest, macos-latest AND windows-latest runners
#   (the last via Git Bash) without any setup step. That is what makes it cheap enough to
#   run on all three OSes on every pull request.
#
# WHAT IT PROVES
#   1. A Windows-style absolute filename in the report ("C:\fake\repo\api\Foo.cs") resolves
#      for the repo-relative path "api/Foo.cs" -- defect 1's exact shape.
#   2. A POSIX-style absolute filename ("/fake/repo/api/Bar.cs") resolves the same way --
#      proving the matcher does not depend on either side's absolute-path convention.
#   3. A compiler-generated class entry is filtered out and does not leak into the result.
#   4. The suffix match requires a path-segment boundary: "api/Foo.cs" must NOT pick up the
#      line-rate belonging to the decoy "api/OldFoo.cs", even though the decoy path IS a
#      textual suffix-of-a-suffix of the real one at the character level.
#   5. A file with ZERO matching entries in the report returns an EMPTY result -- and, just as
#      importantly, does not abort THIS SCRIPT. This selftest runs under `set -euo pipefail`,
#      the same as every real caller, so it is the assertion that would have caught the
#      pipefail bug found in PR review: cobertura_line_rates_for_file()'s internal pipeline
#      exits non-zero when its `grep -E` stage matches nothing (pipefail takes the rightmost
#      NON-ZERO stage, not literally the last command), which previously took the whole
#      calling script down via `set -e` the instant a file had no coverage entries at all --
#      the exact "no coverage found" case this function exists to report. Fixed with a
#      trailing `|| true` inside cobertura-match.sh.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=scripts/lib/cobertura-match.sh
. "$SCRIPT_DIR/cobertura-match.sh"

TMPDIR_SELFTEST="$(mktemp -d)"
trap 'rm -rf "$TMPDIR_SELFTEST"' EXIT

FIXTURE="$TMPDIR_SELFTEST/fixture.cobertura.xml"

# One synthetic Cobertura report covering all four assertions above. Real dotnet-produced
# reports put each <class> element on its own line (no line wrapping inside a tag) -- this
# fixture matches that shape because cobertura_line_rates_for_file() assumes it.
cat > "$FIXTURE" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<coverage line-rate="0.75" version="1.9">
  <packages>
    <package name="Fake">
      <classes>
        <class name="Fake.Foo" filename="C:\fake\repo\api\Foo.cs" line-rate="1" branch-rate="1">
        </class>
        <class name="Fake.OldFoo" filename="C:\fake\repo\api\OldFoo.cs" line-rate="0.25" branch-rate="0.25">
        </class>
        <class name="Fake.Bar" filename="/fake/repo/api/Bar.cs" line-rate="0.5" branch-rate="0.5">
        </class>
        <class name="Fake.Baz" filename="/fake/repo/api/Baz.cs" line-rate="0.9" branch-rate="0.9">
        </class>
        <class name="Fake.Baz+&lt;&gt;c__DisplayClass0_0" filename="/fake/repo/api/Baz.cs" line-rate="0" branch-rate="0">
        </class>
        <class name="Fake.Baz+&lt;DoWorkAsync&gt;d__0" filename="/fake/repo/api/Baz.cs" line-rate="0" branch-rate="0">
        </class>
      </classes>
    </package>
  </packages>
</coverage>
XML

FAILURES=0

# Compares actual output ($1) against every expected line ($2, newline-separated) with no
# extras and no omissions -- plain bash string comparison, no diff/comm dependency.
assert_exact_set() {
  local description="$1"
  local actual="$2"
  local expected="$3"

  local actual_sorted expected_sorted
  actual_sorted=$(printf '%s\n' "$actual" | sed '/^$/d' | sort)
  expected_sorted=$(printf '%s\n' "$expected" | sed '/^$/d' | sort)

  if [ "$actual_sorted" = "$expected_sorted" ]; then
    echo "  PASS: $description"
  else
    echo "  FAIL: $description"
    echo "        expected: $(printf '%s' "$expected_sorted" | tr '\n' ',')"
    echo "        actual:   $(printf '%s' "$actual_sorted" | tr '\n' ',')"
    FAILURES=$((FAILURES + 1))
  fi
}

assert_not_contains() {
  local description="$1"
  local actual="$2"
  local forbidden_value="$3"

  if printf '%s\n' "$actual" | grep -Fxq "$forbidden_value"; then
    echo "  FAIL: $description (found forbidden value '$forbidden_value' in: $(printf '%s' "$actual" | tr '\n' ',')  )"
    FAILURES=$((FAILURES + 1))
  else
    echo "  PASS: $description"
  fi
}

echo "Running cobertura-match.sh selftest against a synthetic fixture..."
echo

# 1. Windows-style absolute path resolves by suffix -- issue #480 defect 1's exact shape.
RESULT_FOO=$(cobertura_line_rates_for_file "$FIXTURE" "api/Foo.cs")
assert_exact_set "Windows-style absolute filename resolves api/Foo.cs to line-rate 1" \
  "$RESULT_FOO" "1"

# 2. POSIX-style absolute path resolves the same way -- no absolute-path convention assumed.
RESULT_BAR=$(cobertura_line_rates_for_file "$FIXTURE" "api/Bar.cs")
assert_exact_set "POSIX-style absolute filename resolves api/Bar.cs to line-rate 0.5" \
  "$RESULT_BAR" "0.5"

# 3. The compiler-generated entries for Baz.cs (the lambda-closure <>c__DisplayClass shape
#    and the async-state-machine >d__ shape) must NOT leak into the result -- only the real
#    class entry's line-rate (0.9) should appear.
RESULT_BAZ=$(cobertura_line_rates_for_file "$FIXTURE" "api/Baz.cs")
assert_exact_set "compiler-generated Baz.cs entries are filtered, only the real entry remains" \
  "$RESULT_BAZ" "0.9"

# 4. THE SUFFIX FALSE-POSITIVE CASE. api/Foo.cs must not pick up api/OldFoo.cs's line-rate
#    (0.25), even though "OldFoo.cs" ends in the literal substring "Foo.cs". Re-uses
#    RESULT_FOO from assertion 1: it must be EXACTLY "1", not "1" and "0.25" together.
assert_not_contains "api/Foo.cs does not pick up the api/OldFoo.cs decoy's line-rate (0.25)" \
  "$RESULT_FOO" "0.25"

# 4b. THE SAME CASE, but at the point where it actually bites: a BARE filename with no
#    directory prefix at all. "api/Foo.cs" (full path) never collides with
#    "api/OldFoo.cs" -- the "Old" breaks the "api/" continuity, so the full-path form is
#    safe with or without the path-segment-boundary guard (confirmed by temporarily
#    weakening the guard during development: assertion 4 above still passed). The guard
#    earns its keep specifically for a SHORT needle, matched purely at the final path
#    component: "Foo.cs" (bare) DOES end with a naive, unguarded "*Foo.cs" pattern against
#    "api/OldFoo.cs" (its own tail IS "Foo.cs"), and a caller that ever passes a bare
#    filename -- or two files that share a name where one sits at the needle's own
#    directory boundary -- would silently pick up the wrong file without the "/" (or
#    start-of-string) requirement. This assertion is the one that actually goes RED if the
#    boundary guard is removed; verified by hand during development by weakening
#    `*/"$rel_file"` to `*"$rel_file"` in cobertura-match.sh, which turned exactly this
#    assertion red while leaving every other assertion in this file green.
RESULT_BARE_FOO=$(cobertura_line_rates_for_file "$FIXTURE" "Foo.cs")
assert_not_contains "bare 'Foo.cs' does not pick up the api/OldFoo.cs decoy's line-rate (0.25)" \
  "$RESULT_BARE_FOO" "0.25"

# 5. THE PIPEFAIL-ABORT CASE. A file genuinely absent from the report must resolve to an
#    empty string WITHOUT killing this script via `set -e` -- if it did, execution would stop
#    dead right here and none of the PASS/FAIL reporting below would ever run. Reaching the
#    assert_exact_set call at all is therefore part of what this assertion proves, not just
#    its result.
RESULT_MISSING=$(cobertura_line_rates_for_file "$FIXTURE" "api/DefinitelyNotInReport.cs")
assert_exact_set "a file with zero matching entries resolves to empty, without aborting the script" \
  "$RESULT_MISSING" ""

echo
if [ "$FAILURES" -gt 0 ]; then
  echo "FAIL: $FAILURES assertion(s) failed." >&2
  exit 1
fi

echo "PASS: all cobertura-match.sh selftest assertions succeeded."
