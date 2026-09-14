#!/usr/bin/env bash
# check-migrate-hook.sh — the shape gate for Phase 51's pre-rollout migration hook.
#
# PURPOSE
#   Phase 51 moved database migrations out of application startup and in front of the rollout, so
#   that a failed migration fails the RELEASE instead of being logged into a void. The guarantee is
#   carried entirely by the SHAPE of two chart templates. Every invariant below is one whose loss
#   would silently return us to the previous behaviour -- a green deploy over an out-of-date schema.
#
#   Exit 0 = every invariant holds in both database modes.
#   Exit 1 = an invariant is broken; the offending one is named.
#
# WHY IT ASSERTS A POSITIVE COUNT FIRST
#   A selector that matches nothing does not fail -- it PASSES. Twelve gates were found silently
#   fail-open across phases 48-50 and that was the commonest shape among them. So this script counts
#   the migration Job before it inspects it, and FAILS when the count is zero. Same reason
#   .github/scripts/preview-smoke/lib.sh refuses to finish on zero assertions.
#
# WHY A BARE `helm template` IS NOT USED
#   Both deployments call `required` on an image repository and tag, and values.yaml ships them
#   empty, so a bare render cannot succeed. The placeholder set below is the one recorded in the
#   Phase 48 helm baseline header (assumption A-11).
set -uo pipefail

RED=$'\033[0;31m'; GREEN=$'\033[0;32m'; YELLOW=$'\033[0;33m'; RESET=$'\033[0m'

CHART="./deploy/helm/leap"
PLACEHOLDERS=(
  --set image.repository=example/leap
  --set image.tag=placeholder
  --set images.api.tag=placeholder
  --set images.web.tag=placeholder
)

FAILURES=0
CHECKS=0

fail() { printf '%s[FAIL]%s %s\n' "$RED" "$RESET" "$1" >&2; FAILURES=$((FAILURES + 1)); }
pass() { printf '%s[ok]%s   %s\n' "$GREEN" "$RESET" "$1"; CHECKS=$((CHECKS + 1)); }
info() { printf '%s[info]%s %s\n' "$YELLOW" "$RESET" "$1"; }

# Render one database mode into a file. rds additionally needs a remote secret name (required()).
render() {
  local mode="$1" out="$2"
  local extra=()
  if [ "$mode" = "rds" ]; then
    extra=(--set database.mode=rds --set database.rds.externalSecret.remoteSecretName=placeholder/secret)
  else
    extra=(--set database.mode=pod)
  fi
  helm template leap-app "$CHART" "${PLACEHOLDERS[@]}" "${extra[@]}" > "$out" 2>"$out.err"
}

# Extract the YAML document matching BOTH kind and metadata name, so assertions are scoped to one
# object rather than grepping the whole render (where another object could satisfy them by accident).
#
# Matching on name ALONE is not enough and that was a real defect in the first draft of this script:
# the api Service and the api Deployment are both named `leap-app-api`, the Service sorts first in the
# render, and it has no `image:` -- so the image-drift check silently could not find an image to
# compare and reported a failure it should never have reported. Kind is part of the key.
doc_for() {
  local file="$1" kind="$2" name="$3"
  python3 - "$file" "$kind" "$name" <<'PY'
import sys
path, want_kind, want_name = sys.argv[1], sys.argv[2], sys.argv[3]
for doc in open(path).read().split('\n---\n'):
    lines = doc.splitlines()
    if any(l.strip() == f'kind: {want_kind}' and l.startswith('kind:') for l in lines) and \
       any(l.strip() == f'name: {want_name}' and l.startswith('  name:') for l in lines):
        print(doc)
        sys.exit(0)
sys.exit(1)
PY
}

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

for MODE in pod rds; do
  info "rendering database.mode=${MODE}"
  if ! render "$MODE" "$TMP/$MODE.yaml"; then
    fail "mode=${MODE}: helm template failed -- $(head -3 "$TMP/$MODE.yaml.err" | tr '\n' ' ')"
    continue
  fi

  # ---- 1. POSITIVE COUNT FIRST. Zero is a FAILURE, never a pass. --------------------------------
  JOB_COUNT=$(grep -c '^kind: Job$' "$TMP/$MODE.yaml" || true)
  if [ "$JOB_COUNT" -ne 1 ]; then
    fail "mode=${MODE}: expected exactly 1 migration Job in the render, found ${JOB_COUNT}. A selector that matches nothing must never be read as a pass."
    continue
  fi
  pass "mode=${MODE}: exactly one migration Job is rendered"

  JOB=$(doc_for "$TMP/$MODE.yaml" Job "leap-app-migrate") || {
    fail "mode=${MODE}: no object named leap-app-migrate in the render"
    continue
  }

  # ---- 2. The hook annotation set this mode requires, matched exactly. --------------------------
  if [ "$MODE" = "rds" ]; then EXPECT_HOOK="pre-install,pre-upgrade"; else EXPECT_HOOK="post-install,pre-upgrade"; fi
  if printf '%s' "$JOB" | grep -qF "\"helm.sh/hook\": ${EXPECT_HOOK}"; then
    pass "mode=${MODE}: hook set is exactly ${EXPECT_HOOK}"
  else
    ACTUAL=$(printf '%s' "$JOB" | grep -F '"helm.sh/hook":' | head -1 | sed 's/^ *//')
    fail "mode=${MODE}: hook set must be '${EXPECT_HOOK}' -- found: ${ACTUAL:-<none>}. A pre-install hook in pod mode DEADLOCKS the first install (51-FINDINGS.md §2)."
  fi

  # ---- 3. Hook weight, and a delete policy that RETAINS a failed hook. -------------------------
  printf '%s' "$JOB" | grep -qF '"helm.sh/hook-weight":' \
    && pass "mode=${MODE}: Job carries a hook weight" \
    || fail "mode=${MODE}: Job has no helm.sh/hook-weight annotation"

  DELETE_POLICY=$(printf '%s' "$JOB" | grep -F '"helm.sh/hook-delete-policy":' | head -1)
  if [ -z "$DELETE_POLICY" ]; then
    fail "mode=${MODE}: Job has no helm.sh/hook-delete-policy annotation"
  elif printf '%s' "$DELETE_POLICY" | grep -q 'hook-failed'; then
    fail "mode=${MODE}: delete policy includes hook-failed -- a failed Job's pod is the ONLY readable record of why a deploy failed once --atomic has rolled back. It must be RETAINED."
  elif printf '%s' "$DELETE_POLICY" | grep -q 'hook-succeeded'; then
    # Measured, not theorised: with hook-succeeded, helm deleted the Job about 13 seconds after it
    # completed, so the log answering "what did the last migration actually do" was gone minutes
    # after every successful deploy. before-hook-creation alone supersedes it on the next deploy.
    fail "mode=${MODE}: delete policy includes hook-succeeded -- the Job is then deleted seconds after it finishes and a successful migration's log is unreadable afterwards. Retain it; before-hook-creation supersedes it on the next deploy."
  elif printf '%s' "$DELETE_POLICY" | grep -q 'before-hook-creation'; then
    pass "mode=${MODE}: delete policy retains the Job until the next deploy supersedes it"
  else
    fail "mode=${MODE}: delete policy must include before-hook-creation, or a stale Job blocks the next deploy -- found: ${DELETE_POLICY}"
  fi

  # ---- 4. Retry limit zero and an active deadline. ----------------------------------------------
  printf '%s' "$JOB" | grep -qE '^  backoffLimit: 0$' \
    && pass "mode=${MODE}: backoffLimit is 0" \
    || fail "mode=${MODE}: backoffLimit must be 0 -- retries quietly consume the release timeout and surface as an ambiguous helm timeout"

  printf '%s' "$JOB" | grep -qE '^  activeDeadlineSeconds: [0-9]+$' \
    && pass "mode=${MODE}: an activeDeadlineSeconds is set" \
    || fail "mode=${MODE}: activeDeadlineSeconds must be set, and below the smallest helm timeout"

  # ---- 5. The Job's image is the SAME string as the api Deployment's. --------------------------
  API_DOC=$(doc_for "$TMP/$MODE.yaml" Deployment "leap-app-api") || API_DOC=""
  API_IMAGE=$(printf '%s' "$API_DOC" | grep -E '^ +image: ' | head -1 | sed 's/^ *image: *//')
  JOB_IMAGES=$(printf '%s' "$JOB" | grep -E '^ +image: ' | sed 's/^ *image: *//' | sort -u)
  # `[ -n ... ]`, NOT `wc -l`. The first draft guarded this comparison with
  # `[ "$(printf '%s' "$JOB_IMAGES" | wc -l)" -ne 0 ]`, and `wc -l` counts NEWLINES -- so a single
  # image with no trailing newline counted as ZERO and the whole check was skipped. It only ever ran
  # when the Job carried two DIFFERENT images, i.e. never in the case it exists to catch. Proven
  # fail-open by canary I5, which pointed the Job at `someone-elses/image:stale` and the gate passed.
  if [ -z "$API_IMAGE" ]; then
    fail "mode=${MODE}: could not read the api Deployment's image, so the Job's image cannot be compared"
  elif [ -n "$JOB_IMAGES" ] && [ "$JOB_IMAGES" != "$API_IMAGE" ]; then
    fail "mode=${MODE}: Job image(s) [${JOB_IMAGES//$'\n'/, }] differ from the api Deployment's [${API_IMAGE}] -- the migration must not drift to a different tag than the application being deployed"
  else
    pass "mode=${MODE}: Job runs the same image as the api Deployment (${API_IMAGE})"
  fi

  # ---- 6. Neither new object reads the release's own ConfigMap. --------------------------------
  # On an upgrade a hook sees the PREVIOUS release's ConfigMap, so on the very deploy that changes
  # POSTGRES_DB it would migrate the WRONG database (51-FINDINGS.md §3).
  if printf '%s' "$JOB" | grep -q 'configMapRef'; then
    fail "mode=${MODE}: the Job references the release's own ConfigMap. On an upgrade a hook reads the PREVIOUS release's ConfigMap and would migrate the wrong database. Render host/port/db from .Values instead."
  else
    pass "mode=${MODE}: the Job reads no ConfigMap"
  fi

  # ---- 7. No password value appears literally in the rendered Job. -----------------------------
  # values.yaml ships a known development password; if it is ever rendered INLINE into the Job spec
  # rather than referenced, it is readable by anyone with namespace read access.
  DEV_PASSWORD=$(grep -E '^  POSTGRES_PASSWORD:' deploy/helm/leap/values.yaml | head -1 | sed 's/.*: *//' | tr -d '"')
  if [ -n "$DEV_PASSWORD" ] && printf '%s' "$JOB" | grep -qF "$DEV_PASSWORD"; then
    fail "mode=${MODE}: a password value appears literally in the rendered Job. Credentials must arrive by secretKeyRef only -- never inline, never in command or args."
  else
    pass "mode=${MODE}: no password value is rendered into the Job"
  fi
  printf '%s' "$JOB" | grep -q 'secretKeyRef' \
    && pass "mode=${MODE}: credentials arrive by secretKeyRef" \
    || fail "mode=${MODE}: the Job references no Secret -- it cannot be getting credentials safely"

  # ---- 8. The hook-scoped Secret, per mode. ----------------------------------------------------
  SECRET=$(doc_for "$TMP/$MODE.yaml" Secret "leap-app-migrate-credentials") || SECRET=""
  if [ "$MODE" = "pod" ]; then
    if [ -z "$SECRET" ]; then
      fail "mode=pod: the hook-scoped credentials Secret is absent. In pod mode the release's own Secret does not exist on a first install and is STALE on an upgrade."
    else
      SECRET_WEIGHT=$(printf '%s' "$SECRET" | grep -F '"helm.sh/hook-weight":' | head -1 | sed 's/.*: *//' | tr -d '"')
      JOB_WEIGHT=$(printf '%s' "$JOB" | grep -F '"helm.sh/hook-weight":' | head -1 | sed 's/.*: *//' | tr -d '"')
      if [ -n "$SECRET_WEIGHT" ] && [ -n "$JOB_WEIGHT" ] && [ "$SECRET_WEIGHT" -lt "$JOB_WEIGHT" ]; then
        pass "mode=pod: hook Secret weight ${SECRET_WEIGHT} is lower than the Job's ${JOB_WEIGHT}"
      else
        fail "mode=pod: hook Secret weight (${SECRET_WEIGHT:-<none>}) must be LOWER than the Job's (${JOB_WEIGHT:-<none>}) -- Helm orders hook resources by ascending weight, so otherwise the Job can be created before the Secret it mounts."
      fi
    fi
  else
    if [ -n "$SECRET" ]; then
      fail "mode=rds: the hook-scoped credentials Secret must NOT render -- rds uses the retained ExternalSecret-backed Secret."
    else
      pass "mode=rds: no hook-scoped Secret is rendered"
    fi
    if printf '%s' "$JOB" | grep -qF 'name: leap-app-postgres'; then
      pass "mode=rds: the Job references the external-secret-backed Secret by name"
    else
      fail "mode=rds: the Job does not reference leap-app-postgres, the ExternalSecret-materialised credentials Secret"
    fi
  fi
done

# ---- Repository-wide: NO values file may disable the hook. --------------------------------------
# The switch exists as a one-off emergency `--set`. A values file setting it off would silently
# disable this phase's entire guarantee, and the deploy would stay green while nothing migrated.
# Resolved as a KEY, not grepped. The first draft of this script grepped each values file for any
# `enabled: false` and then for the word `migrations:`, and it flagged values.yaml on the strength of
# an unrelated `dataProtectionKeys.enabled: false` seventeen lines away. A gate that is red on a
# correct tree gets suppressed, which is strictly worse than no gate.
DISABLERS=$(python3 "$(dirname "$0")/check-migrate-hook-disablers.py" 2>/dev/null || true)
if [ -n "$DISABLERS" ]; then
  fail "a values file disables the migration hook: ${DISABLERS//$'\n'/, }"
else
  pass "no values file in the repository disables the migration hook"
fi

# ---- The gate must prove it EXECUTED, not merely that it did not complain. ----------------------
if [ "$CHECKS" -eq 0 ]; then
  printf '%s[FAIL]%s the migrate-hook gate ran ZERO assertions -- it proved nothing.\n' "$RED" "$RESET" >&2
  exit 1
fi

if [ "$FAILURES" -ne 0 ]; then
  printf '\n%s%d invariant(s) broken across %d assertion(s).%s\n' "$RED" "$FAILURES" "$CHECKS" "$RESET" >&2
  exit 1
fi

printf '\n%sMigration hook OK: %d assertions passed across both database modes.%s\n' "$GREEN" "$CHECKS" "$RESET"
exit 0
