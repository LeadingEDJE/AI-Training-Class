#!/usr/bin/env bash
# comparison-point.sh -- resolve WHAT to compare HEAD against, for the migration gates.
#
# WHY THIS IS SHARED CODE AND NOT COPIED INTO EACH SCRIPT
#   Both migration gates ask the same question -- "what did this branch/commit introduce?" -- and both
#   answered it with `git merge-base HEAD origin/main`. That answer is WRONG in the same way for both
#   on any run where HEAD IS the base branch (a push to main, the Tuesday schedule, a
#   workflow_dispatch): the merge base is then HEAD ITSELF, so each script ends up comparing HEAD to
#   HEAD and passing unconditionally.
#
#   `rehearse-migration.sh` was fixed for this first. `check-migrations-immutable.sh` was not -- it
#   carried the identical logic and kept the identical hole, so a tampered migration COMMITTED on
#   main exited 0. Fixing one instance of a defect and leaving its twin is what this file exists to
#   stop: there is now one implementation, and fixing it fixes every caller.
#
# THE FALLBACK
#   When the merge base is HEAD, compare against the FIRST PARENT instead. On main after a merge,
#   `HEAD^` is the previous main, so "what this introduced" is exactly the merged pull request's
#   changes -- the same question, asked of a merge commit. With no parent at all there is no delta to
#   express and the caller is told to pass an explicit baseline rather than being handed a value that
#   silently passes.
#
# USAGE
#   . "${SCRIPT_DIR}/lib/comparison-point.sh"
#   if ! read -r COMPARE_REF COMPARE_MODE < <(resolve_comparison_point "$BASE_REF"); then exit 1; fi
#
#   Writes "<ref><TAB><human-readable mode>" on stdout; diagnostics go to stderr. Returns non-zero
#   when no comparison point can be resolved -- callers MUST check, because the failure modes here
#   are precisely the ones that otherwise turn into a silent pass.

# Resolves the commit to compare HEAD against. $1 = base ref (e.g. origin/main).
resolve_comparison_point() {
  local base_ref="$1"
  local merge_base parent head

  if [ -z "$base_ref" ]; then
    echo "resolve_comparison_point: no base ref given" >&2
    return 1
  fi

  if ! merge_base=$(git merge-base HEAD "$base_ref" 2>/dev/null); then
    echo "resolve_comparison_point: could not compute a merge base against '${base_ref}'." >&2
    echo "  In CI, check out with fetch-depth: 0 and fetch the base ref first." >&2
    return 1
  fi

  head=$(git rev-parse HEAD)

  if [ "$merge_base" != "$head" ]; then
    printf '%s\t%s\n' "$merge_base" "merge base with ${base_ref}"
    return 0
  fi

  # Degenerate: HEAD is at (or is an ancestor of) the base branch.
  if parent=$(git rev-parse -q --verify 'HEAD^' 2>/dev/null); then
    printf '%s\t%s\n' "$parent" "first parent (HEAD is at ${base_ref}, so the merge base is HEAD itself)"
    return 0
  fi

  echo "resolve_comparison_point: HEAD is at '${base_ref}' and has no parent, so there is no delta" >&2
  echo "  to express. Pass an explicit baseline instead of accepting a comparison that always passes." >&2
  return 1
}
