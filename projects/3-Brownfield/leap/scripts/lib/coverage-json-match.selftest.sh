#!/usr/bin/env bash
# coverage-json-match.selftest.sh -- fixture test of coverage-json-match.sh.
#
# WHY THIS EXISTS
#   The same argument as its sibling `cobertura-match.selftest.sh`, and this file is the proof
#   the argument was right: #480 fixed the BACKEND path matcher and added a cross-OS selftest
#   for it, and the FRONTEND matcher two hundred lines down the same script kept the identical
#   bug. A selftest that covers one half of a script is a selftest that tells you the other
#   half is fine.
#
#   Runs on ubuntu-latest, macos-latest AND windows-latest in the `script-portability` job, so
#   a platform regression fails a pull request rather than surfacing on someone's machine as a
#   phantom coverage gap.
#
# WHAT IT PROVES, and every assertion below was checked against the OLD matcher to confirm it
# actually discriminates -- two of them flip, which is what makes this file worth having.
#   1. A Windows-style absolute key resolves for a forward-slash app-relative path. FLIPS.
#   2. A POSIX-style absolute key resolves the same way, so neither convention is assumed.
#   3. The match requires a PATH-SEGMENT BOUNDARY, using a decoy that the old substring match
#      genuinely returns -- see the comment at that assertion for why the obvious decoy does
#      not discriminate.
#   4. A file with no entry returns EMPTY and does not abort the caller. This script runs under
#      `set -euo pipefail` exactly as `check-coverage.sh` does, so it is the assertion that
#      catches the pipefail-abort shape that bit the Cobertura matcher in review.
#   5. The returned key is VERBATIM, not normalised -- callers index the JSON with it, so a
#      helpfully-slash-corrected key would report `null` coverage for a covered file. FLIPS.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=scripts/lib/coverage-json-match.sh
. "${SCRIPT_DIR}/coverage-json-match.sh"

# FAIL, never skip. A selftest that quietly does nothing when a tool is missing is the vacuous
# gate this whole file exists because of.
if ! command -v jq >/dev/null 2>&1; then
  echo "FAIL: jq is not available, so this selftest cannot run. check-coverage.sh already" >&2
  echo "      hard-requires jq, so this is a broken environment rather than a reason to skip." >&2
  exit 1
fi

WORKDIR="$(mktemp -d)"
trap 'rm -rf "${WORKDIR}"' EXIT
FIXTURE="${WORKDIR}/coverage-final.json"

# Keys deliberately mix conventions in ONE report, which is not artificial: the file is written
# by whichever machine ran the tests, and this repo is worked on from Windows and Linux both.
#
# Written with printf rather than a heredoc: the backslashes in the Windows-style keys are the
# entire point of the fixture, and they must survive into the file as JSON `\\` escapes.
{
  printf '{\n'
  printf '  "C:\\\\fake\\\\repo\\\\web\\\\compass\\\\src\\\\features\\\\clients\\\\ClientFormPage.tsx": {\n'
  printf '    "s": { "0": 1, "1": 1 }\n'
  printf '  },\n'
  printf '  "/fake/repo/web/compass/src/features/lookups/LookupSection.tsx": {\n'
  printf '    "s": { "0": 1, "1": 0 }\n'
  printf '  },\n'
  printf '  "/fake/repo/web/compass/vendorsrc/Foo.tsx": {\n'
  printf '    "s": { "0": 0 }\n'
  printf '  }\n'
  printf '}\n'
} > "${FIXTURE}"

# The fixture is only useful if it parses AND carries a real backslash. Assert both, or every
# assertion below could pass against a mangled file.
jq -e 'length == 3' "${FIXTURE}" > /dev/null \
  || { echo "FAIL: fixture does not parse as JSON with 3 keys" >&2; exit 1; }
jq -r 'keys[]' "${FIXTURE}" | grep -q 'C:\\fake' \
  || { echo "FAIL: fixture lost its Windows-style backslashes, so case 1 proves nothing" >&2; exit 1; }

failures=0
check() {
  local label="$1" expected="$2" actual="$3"
  if [ "${actual}" = "${expected}" ]; then
    echo "  PASS: ${label}"
  else
    echo "  FAIL: ${label}" >&2
    echo "        expected: '${expected}'" >&2
    echo "        actual:   '${actual}'" >&2
    failures=$((failures + 1))
  fi
}

# 1. The defect: a Windows-style key, looked up by a forward-slash path. Empty under the old
#    `grep -F` matcher, which is how every changed front-end file read as uncovered on Windows.
check "Windows-style key resolves for a forward-slash path" \
  'C:\fake\repo\web\compass\src\features\clients\ClientFormPage.tsx' \
  "$(coverage_json_key_for_file "${FIXTURE}" 'src/features/clients/ClientFormPage.tsx')"

# 2. Neither side's convention is assumed.
check "POSIX-style key resolves the same way" \
  '/fake/repo/web/compass/src/features/lookups/LookupSection.tsx' \
  "$(coverage_json_key_for_file "${FIXTURE}" 'src/features/lookups/LookupSection.tsx')"

# 3. THE BOUNDARY DECOY, and its shape is deliberate. `vendorsrc/Foo.tsx` CONTAINS the string
#    `src/Foo.tsx`, so the old substring match returns it and reports ITS coverage (0%) for a
#    file that is not in the report at all -- a silently wrong answer rather than a visible
#    failure. `endswith("/" + suffix)` rejects it because the segment is `vendorsrc`, not `src`.
#
#    The OBVIOUS decoy -- a sibling `src/OldFoo.tsx` -- does NOT discriminate, and an earlier
#    version of this file used it and proved nothing: `src/Foo.tsx` is not a substring of
#    `src/OldFoo.tsx` at all, so both matchers reject it for free. Found by running the old
#    matcher against this file and reading which assertions actually flipped, rather than
#    assuming the ones I had written were the ones doing the work.
check "src/Foo.tsx does NOT pick up the vendorsrc/Foo.tsx substring decoy" \
  '' \
  "$(coverage_json_key_for_file "${FIXTURE}" 'src/Foo.tsx')"

# 4. A genuine miss is empty, and did not abort this script -- reaching here proves it.
check "a file with no entry resolves to empty, without aborting" \
  '' \
  "$(coverage_json_key_for_file "${FIXTURE}" 'src/features/nothing/Absent.tsx')"

# 5. The key comes back verbatim, so `jq '.[$key]'` can find it. Asserted by using it.
verbatim_key="$(coverage_json_key_for_file "${FIXTURE}" 'src/features/clients/ClientFormPage.tsx')"
statements="$(jq -r --arg key "${verbatim_key}" '.[$key].s | length' "${FIXTURE}")"
check "the returned key indexes the report (2 statements)" '2' "${statements}"

echo
if [ "${failures}" -eq 0 ]; then
  echo "PASS: all coverage-json-match.sh selftest assertions succeeded."
else
  echo "FAIL: ${failures} coverage-json-match.sh selftest assertion(s) failed." >&2
  exit 1
fi
