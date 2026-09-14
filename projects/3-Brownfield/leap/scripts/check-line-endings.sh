#!/usr/bin/env bash
#
# Guard against stale-CRLF tracked shell scripts (issue #480, defect 2).
#
# `set -euo pipefail\r` (a CRLF-corrupted line) is rejected by bash as an invalid option
# name. `set -e` never takes effect, and the script exits 0 having run nothing -- a script
# that silently does nothing is indistinguishable from one that passed. `*.sh text eol=lf`
# in .gitattributes is supposed to keep every tracked shell script LF-only, but git does NOT
# retroactively renormalize a file that was already checked out with CRLF before the
# attribute existed -- only `git add --renormalize` (or a fresh clone) fixes an
# already-committed file. This script is the mechanism that keeps that from recurring
# silently: it runs in CI, on every OS, and fails loudly rather than exiting 0 on a corrupted
# script.
#
# ---------------------------------------------------------------------------------------------
# WHY THE COMMITTED BLOB, NOT THE WORKING TREE. `file "$f"` inspects the working-tree copy,
# whose line endings depend on the checking-out machine's `core.autocrlf` -- a developer with
# autocrlf=true sees CRLF locally even when the committed blob is clean LF, and a developer
# with autocrlf=false (or a Linux/macOS clone) sees whatever the blob actually holds. Those
# give DIFFERENT verdicts for the IDENTICAL commit, which makes `file` useless as a gate: it
# would pass or fail depending on who ran it, not on what was committed. `git show "HEAD:$f"`
# reads the object database directly -- the same bytes on any machine, regardless of local
# checkout config -- so this check gives the same verdict everywhere.
#
# WHY THIS SCRIPT'S FAIL-OPEN MODE IS AN ABSENCE CHECK, NOT AN EMPTY LOOP (see
# check-stale-engine-claims.sh for the fuller version of this argument, which this mirrors).
# A path that matches no files reports zero violations and exits 0 -- indistinguishable from
# success. So the corpus size is asserted against a floor BEFORE any file is inspected.
# ---------------------------------------------------------------------------------------------
set -euo pipefail

cd "$(dirname "$0")/.."

# ---- the corpus, from git rather than a glob so an untracked stray cannot skew it ----
CORPUS=$(git ls-files '*.sh' | sort)
corpus_count=$(printf '%s\n' "$CORPUS" | grep -c . || true)

# Floor, not an exact count: shell scripts are added routinely and this guard must not
# become a tripwire on that. It exists only to prove the search reached the tree at all.
# Measured 2026-08-28: 45 tracked *.sh files (issue #480's own changes included).
# Re-measured 2026-09-04: 40, after the planning tooling took 9 shell scripts with it. The floor
# dropped with the corpus so the margin stays wide enough to absorb ordinary churn.
CORPUS_FLOOR=30

if [ "$corpus_count" -lt "$CORPUS_FLOOR" ]; then
  echo "FAIL: scanned only $corpus_count *.sh files, below the floor of $CORPUS_FLOOR." >&2
  echo "      The corpus is wrong or the tree moved -- this check would pass over nothing." >&2
  exit 1
fi

echo "Scanning $corpus_count tracked *.sh file(s) (floor $CORPUS_FLOOR) for CRLF in the COMMITTED blob..."
echo

violations=""
violation_count=0
while IFS= read -r f; do
  [ -z "$f" ] && continue

  # Read the COMMITTED BLOB, not the working-tree copy -- see the header comment above.
  #
  # NOT a text-pattern match for a CR byte or its rendered form. Two approaches were tried
  # and rejected while writing this script, both for the same reason: a script ABOUT CRLF
  # detection inevitably contains the very escape sequences it is trying to detect, as
  # literal source text.
  #
  #   1. `grep -q $'\r'` directly against the blob. Git for Windows' bundled grep does
  #      universal-newline handling on its own line-tokenization: it strips a trailing CR
  #      immediately before LF before a pattern is ever applied to the line, so this
  #      reports NO MATCH against a file that is genuinely CRLF end-to-end (confirmed with
  #      `od -An -tx1`: real `0d 0a` bytes in the stream, zero matches from that same
  #      stream, on this exact Windows/Git-Bash stack). Silent fail-open in the tool meant
  #      to catch fail-open CRLF.
  #   2. `cat -A | grep -q '\^M'` (cat -A renders a real CR as the two printable characters
  #      `^M`, sidestepping problem 1). This is CLOSER but has its own false positive: this
  #      very file's comments and this very script's own literal pattern text contain the
  #      two-character sequence `^M` / `\r` as ordinary prose about CRLF, which `cat -A`
  #      passes through unchanged -- so grepping for that text matches THIS FILE even
  #      though its blob is clean LF (caught by running this exact check against itself
  #      during development).
  #
  # A BYTE-COUNT COMPARISON has neither problem: it never inspects the escape-sequence
  # TEXT a CR might be rendered as, only whether stripping actual CR bytes changes the
  # byte count. `tr -d '\r'` is confirmed NOT to suffer grep's universal-newline stripping
  # on this stack (verified: stripping is a no-op, same byte count, on a clean file; it
  # removes exactly the CR count on a raw CRLF fixture).
  blob_bytes=$(git show "HEAD:$f" | wc -c)
  stripped_bytes=$(git show "HEAD:$f" | tr -d '\r' | wc -c)
  if [ "$blob_bytes" != "$stripped_bytes" ]; then
    violations="${violations}${f}"$'\n'
    violation_count=$((violation_count + 1))
  fi
done <<< "$CORPUS"

echo
if [ "$violation_count" -gt 0 ]; then
  echo "FAIL: $violation_count tracked *.sh file(s) are CRLF in the committed blob:" >&2
  printf '%s' "$violations" | sed 's/^/      /' >&2
  echo >&2
  echo "Fix: git add --renormalize -- '*.sh' && git commit" >&2
  exit 1
fi

echo "PASS: $corpus_count tracked *.sh file(s) scanned, none are CRLF in the committed blob."
