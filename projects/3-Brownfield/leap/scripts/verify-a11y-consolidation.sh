#!/usr/bin/env bash
#
# Coverage proof for the Phase 2 accessibility consolidation (docs/TEST-STRATEGY.md Rule 2).
#
# Every assertion removed from `web/timesheet/tests/e2e/compass-accessibility.critical.spec.ts` must
# name a replacement that EXISTS. This checks each one is present in the file that now owns it.
#
# **It asserts its own item count before iterating.** A `for` loop over an empty list prints nothing
# and exits 0, reporting a clean bill of health over no work at all — the shape docs/TEST-STRATEGY.md
# § "Before deleting a test" step 2 exists to forbid. The guard below is that step, not a formality.
set -euo pipefail

cd "$(dirname "$0")/.."

# Each row: <what was removed> | <file that now covers it> | <literal that proves it is there>
#
# The literal is matched with `grep -F`, so it is a fixed string from the replacement file, not a
# pattern that could quietly match something else.
REPLACEMENTS=(
  "axe: compass index|web/compass/tests/e2e/wcag-sweep.critical.spec.ts|name: 'Compass index'"
  "axe: Team Directory|web/compass/tests/e2e/wcag-sweep.critical.spec.ts|name: 'Team Directory'"
  "axe: lookup administration|web/compass/tests/e2e/wcag-sweep.critical.spec.ts|name: 'Lookup administration'"
  "axe: EDJEr list|web/compass/tests/e2e/wcag-sweep.critical.spec.ts|name: 'EDJEr list'"
  "axe: EDJEr form|web/compass/tests/e2e/wcag-sweep.critical.spec.ts|name: 'EDJEr add form'"
  "axe: client list|web/compass/tests/e2e/wcag-sweep.critical.spec.ts|name: 'Client list'"
  "axe: client form|web/compass/tests/e2e/wcag-sweep.critical.spec.ts|name: 'Client add form'"
  "overflow: lookups|web/compass/tests/e2e/helpers/compass-routes.ts|name: 'admin-lookups'"
  "overflow: Team Directory|web/compass/tests/e2e/helpers/compass-routes.ts|name: 'team-directory'"
  "overflow: EDJEr screens|web/compass/tests/e2e/helpers/compass-routes.ts|name: 'admin-edjer-new'"
  "overflow: client screens|web/compass/tests/e2e/helpers/compass-routes.ts|name: 'admin-client-new'"
  "overflow: Gate E page scroll|web/compass/tests/e2e/helpers/scroll-region.ts|the PAGE scrolls horizontally (Gate E)"
  "keyboard: nav focus indicator|web/compass/tests/e2e/nav-keyboard.critical.spec.ts|visible focus indicator when focused"
  "keyboard: nav tab order|web/compass/tests/e2e/nav-keyboard.critical.spec.ts|Tab walks the nav links in their rendered order"
  "keyboard: EDJEr switch|web/compass/tests/unit/components/ui.test.tsx|toggles from the keyboard"
  "label-in-name: drill-in|web/compass/tests/unit/features/team-directory/TeamDirectoryPage.test.tsx|never an aria-label (WCAG 2.5.3)"
)

expected=16
count=${#REPLACEMENTS[@]}

# ---- the non-vacuity guard, BEFORE the loop ----
if [ "$count" -eq 0 ]; then
  echo "FAIL: zero replacements to check — this script would pass over nothing." >&2
  exit 1
fi
if [ "$count" -ne "$expected" ]; then
  echo "FAIL: expected $expected replacements, found $count. Update \$expected deliberately." >&2
  exit 1
fi

echo "Checking $count replacements (expected $expected)..."
echo

failed=0
for row in "${REPLACEMENTS[@]}"; do
  removed=${row%%|*}
  rest=${row#*|}
  file=${rest%%|*}
  literal=${rest#*|}

  if [ ! -f "$file" ]; then
    printf 'MISSING FILE  %-32s -> %s\n' "$removed" "$file"
    failed=$((failed + 1))
    continue
  fi

  if grep -qF -- "$literal" "$file"; then
    printf 'ok            %-32s -> %s\n' "$removed" "$file"
  else
    printf 'NOT FOUND     %-32s -> %s :: %s\n' "$removed" "$file" "$literal"
    failed=$((failed + 1))
  fi
done

echo

# The deleted file must actually be gone, or this whole script is checking a consolidation that did
# not happen.
if [ -f web/timesheet/tests/e2e/compass-accessibility.critical.spec.ts ]; then
  echo "FAIL: compass-accessibility.critical.spec.ts still exists — nothing was consolidated." >&2
  exit 1
fi

if [ "$failed" -ne 0 ]; then
  echo "FAIL: $failed of $count replacements are missing." >&2
  exit 1
fi

echo "PASS: all $count replacements present, and the superseded spec is gone."
