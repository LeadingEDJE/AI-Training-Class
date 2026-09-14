#!/usr/bin/env bash
# cobertura-match.sh -- match a repo-relative file path against a Cobertura report's
# <class filename="..."> entries WITHOUT composing an absolute path on the caller's side.
#
# WHY THIS EXISTS (issue #480, defect 1)
#   check-coverage.sh used to build ABS_FILE="$REPO_ROOT/$file" -- a Git-Bash-style path
#   like /c/Users/.../api/Foo.cs on Windows -- and grep -F the Cobertura XML for that exact
#   string. But dotnet's coverage tool always records the HOST-NATIVE absolute path, which
#   on Windows is "C:\Users\...\api\Foo.cs" (backslashes, drive letter), never the POSIX
#   form Git Bash composes. The two never matched, so the backend coverage gate reported
#   "No test coverage found" for every changed file on Windows -- verified live: a real
#   report matched the POSIX-form path 0 times and the Windows-form path 12 times, for a
#   file that was actually 100% covered.
#
#   This is the THIRD instance of this general shape of bug in this one script (an earlier
#   one was BSD vs GNU grep -P). The fix this time is structural rather than another special
#   case: stop composing an absolute path on either side and match by SUFFIX instead. A
#   suffix match needs no absolute-path convention from either the machine that produced the
#   report or the machine reading it, so it is correct on Windows, macOS, and Linux, and for
#   a report whose filenames use backslashes OR forward slashes.
#
# WHAT THIS DOES NOT CHANGE
#   The compiler-generated-class filter (lambda closures, async state machines) is preserved
#   verbatim from check-coverage.sh's existing logic -- same two shapes, same portable
#   grep/sed style (no grep -P: macOS ships BSD grep, which rejects -P outright).
#
# PERFORMANCE (found in adversarial review of the #480 fix, before merge)
#   The first version of this function forked sed/tr/grep PER CANDIDATE LINE inside a `while
#   read` loop. That is correct but catastrophically slow specifically on Windows Git Bash,
#   where MSYS's fork emulation is expensive: ~117s for a single call against a realistic
#   project-sized report (~1,450 filename entries), called twice per changed backend file
#   from check-coverage.sh, vs ~8.5s for the identical call under WSL/Linux. Invisible in CI,
#   which never exercises check-coverage.sh against a real report on windows-latest. Rewritten
#   below as a small, FIXED number of whole-stream passes (one grep/grep -v/tr/grep/sed
#   pipeline total, regardless of report size) so process-spawn count no longer scales with
#   the number of matching lines. Do not reintroduce a per-line `while read` loop here.
#
# USAGE
#   . "${SCRIPT_DIR}/lib/cobertura-match.sh"
#   LINE_RATES=$(cobertura_line_rates_for_file "$COBERTURA_XML" "$rel_file")
#
#   $1 = path to a Cobertura XML file (readable).
#   $2 = a REPO-RELATIVE file path, always forward-slash (e.g.
#        "api/Modules/Timesheet/Services/TimeCategoryService.cs" -- what
#        `git diff --name-only` produces on every OS).
#
#   Writes one line-rate value per matching, non-filtered <class> entry to stdout (same
#   output contract as the inline pipeline it replaces -- the caller iterates the output to
#   check whether ALL entries are "1"). Diagnostics go to stderr. Returns non-zero only when
#   the arguments themselves are unusable; an empty match is a normal "nothing found" case
#   the caller distinguishes by checking for empty output, exactly as before.

cobertura_line_rates_for_file() {
  local cobertura_xml="$1"
  local rel_file="$2"

  if [ -z "$cobertura_xml" ] || [ -z "$rel_file" ]; then
    echo "cobertura_line_rates_for_file: usage: cobertura_line_rates_for_file <cobertura.xml> <repo-relative-file>" >&2
    return 1
  fi

  if [ ! -f "$cobertura_xml" ]; then
    echo "cobertura_line_rates_for_file: '${cobertura_xml}' is not a file" >&2
    return 1
  fi

  # Escape ERE metacharacters in rel_file so it is matched LITERALLY by the grep -E below --
  # a repo path routinely contains "." (e.g. "TimeCategoryService.cs"), which is ERE
  # "any character" unless escaped. Unescaped, "TimeCategoryServiceXcs" would satisfy a
  # pattern built from "TimeCategoryService.cs". `&` in the sed replacement means "the text
  # just matched"; `\\&` inserts a literal backslash before it.
  local escaped_rel_file
  escaped_rel_file=$(printf '%s' "$rel_file" | sed 's/[.[\^$()+?*{}|]/\\&/g')

  # ONE PASS over the report -- see the PERFORMANCE header comment above for why this is not
  # a per-line loop. Stages, in order:
  #   1. grep -F: cheap pre-filter to lines carrying a filename attribute at all.
  #   2. grep -v: drop compiler-generated classes (lambda closures / <>c / <>c__DisplayClass
  #      and async state machines / <Method>d__N) BEFORE the suffix match, so their filenames
  #      never reach it. Preserved verbatim from check-coverage.sh's prior inline logic:
  #      portable BRE (no grep -P -- BSD grep on macOS rejects it outright), matching only
  #      the two shapes the C# compiler actually emits, NOT any name merely containing angle
  #      brackets (which would also swallow a legitimate generic type like
  #      CompassEntityConfiguration&lt;TEntity&gt;).
  #   3. tr: normalize backslashes to forward slashes ACROSS THE WHOLE STREAM in one pass, so
  #      a Windows absolute path ("C:\repo\api\Foo.cs") and a POSIX one
  #      ("/repo/api/Foo.cs") compare the same way regardless of which OS produced the report.
  #   4. grep -E: the suffix match itself, anchored on a literal "/" immediately before the
  #      needle -- deliberate, NOT decorative. A bare suffix match with no leading "/" would
  #      let, e.g., "api/Services/TimeCategoryService.cs" also match
  #      "api/Services/OldTimeCategoryService.cs", because the shorter path IS a textual
  #      suffix of the longer one. Requiring "/" immediately before the needle means the
  #      match can only succeed at a real path-segment boundary. `[^"]*` cannot cross the
  #      attribute's closing quote, so this only ever matches inside the filename="..."
  #      value, never bleeding into a neighbouring attribute. Covered by the decoy fixtures
  #      in cobertura-match.selftest.sh.
  #   5. sed: extract the line-rate value from whatever survived.
  #
  # `|| true` at the very end is NOT decorative. Under `pipefail` (every caller runs `set
  # -euo pipefail`), a pipeline's exit status is the rightmost NON-ZERO status among ALL
  # stages, not just the last one -- so when grep -E (stage 4) finds no match, the pipeline
  # reports failure even though sed (stage 5, the actual last command) exits 0 on empty
  # input. A file with genuinely zero matching entries is exactly the case this function
  # exists to report (a real "no coverage" or a path-matching bug) -- it must come back as a
  # normal EMPTY STRING, not a process failure. Without this, `LINE_RATES=$(cobertura_line_
  # rates_for_file ...)` in check-coverage.sh aborts the ENTIRE script via `set -e` the
  # instant it hits the first such file -- silently, before printing "No test coverage found",
  # before FAILED=1, before checking any remaining file. Confirmed by reproducing the exact
  # caller context (a script with `set -euo pipefail` sourcing this file) -- found in PR review,
  # not caught by this session's own earlier verification because that testing did not run
  # under `set -e` itself. Covered by cobertura-match.selftest.sh's not-in-report assertion.
  grep -F 'filename="' "$cobertura_xml" 2>/dev/null \
    | grep -v -e 'name="[^"]*&lt;&gt;' -e 'name="[^"]*&gt;d__' \
    | tr '\\' '/' \
    | grep -E "filename=\"[^\"]*/${escaped_rel_file}\"" \
    | sed -n 's/.*line-rate="\([^"]*\)".*/\1/p' \
    || true
}
