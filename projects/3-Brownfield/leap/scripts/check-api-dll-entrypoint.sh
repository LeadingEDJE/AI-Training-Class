#!/usr/bin/env bash
# check-api-dll-entrypoint.sh — the SC-2 local instrument for Phase 48 (LEAP rename).
#
# WHAT IT ASSERTS
#   The published assembly filename INSIDE the built deployed container image is exactly
#   the filename the copied entrypoint script executes.
#
# WHY THIS SCRIPT EXISTS
#   Nothing else in this repository can observe that. No `dotnet build`, no `dotnet test`,
#   no `npm run lint`, no `npm run build`, no Vitest run, no Playwright run, no coverage
#   run, and no agent hook ever EXECUTES THE PUBLISHED ENTRYPOINT. So the two strings can
#   diverge while every single local gate stays green.
#
#   The divergence is easy to create and expensive to discover:
#     * There is no <AssemblyName> override in the API csproj, so the DLL name tracks the
#       PROJECT FILENAME. Rename the project and the DLL name changes with it.
#     * deploy/docker/api-entrypoint.sh hardcodes the DLL filename on its exec line.
#     * docker/api.Dockerfile (the local Compose variant) has its own ENTRYPOINT naming the
#       assembly, plus a watch-mode ENTRYPOINT that names the project DIRECTORY instead.
#   Miss any one of them and the container BUILDS FINE, starts, dies immediately, and the
#   only signal is a deploy that rolls back and erases its own logs.
#
#   Phase 48 additionally adds an explicit <AssemblyName>, which permanently removes this
#   trap by decoupling the DLL name from the directory name. Until then, and as a
#   regression guard afterwards, this script is the local proof.
#
# EXIT CODES — a SKIP is deliberately distinguishable from a PASS
#   0 = PASS   the image's assembly filename and the entrypoint string agree
#   1 = FAIL   they diverge (the container would start, die, and pass every local gate)
#   2 = SKIP   Docker is unavailable. NOT a pass. Never treat this as a pass.
#
# USAGE
#   bash scripts/check-api-dll-entrypoint.sh
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

DOCKERFILE="deploy/docker/api.Dockerfile"
ENTRYPOINT_SRC="deploy/docker/api-entrypoint.sh"
IMAGE_TAG="leap-api-dll-entrypoint-check:throwaway"
PRODUCT_PREFIX="LeadingEDJE."

cleanup() {
  docker image rm -f "$IMAGE_TAG" >/dev/null 2>&1 || true
}
# Remove the throwaway tag on EVERY exit path, success or failure.
trap cleanup EXIT

# --- Guard: Docker must be reachable. A missing daemon is a SKIP, not a PASS.
if ! command -v docker >/dev/null 2>&1; then
  echo -e "${YELLOW}SKIP: docker is not installed -- cannot inspect the published image.${NC}"
  echo "This is a SKIP, not a PASS. The assembly/entrypoint agreement is UNVERIFIED."
  exit 2
fi
if ! docker info >/dev/null 2>&1; then
  echo -e "${YELLOW}SKIP: the docker daemon is unreachable -- cannot inspect the published image.${NC}"
  echo "This is a SKIP, not a PASS. The assembly/entrypoint agreement is UNVERIFIED."
  exit 2
fi

echo "=== SC-2: published assembly filename vs. entrypoint string ==="
echo ""

# --- Build COLD. --no-cache is mandatory, not defensive: a warm layer cache can satisfy a
#     COPY path that no longer exists in the tree, so a cached build can "prove" agreement
#     for a layout that is already broken.
echo "Building $DOCKERFILE cold (--no-cache)..."
if ! docker build --no-cache -f "$DOCKERFILE" -t "$IMAGE_TAG" . >/tmp/leap-dll-check-build.log 2>&1; then
  echo -e "${RED}FAIL: image build failed. Last 30 lines:${NC}"
  tail -30 /tmp/leap-dll-check-build.log
  exit 1
fi
echo "Build OK."
echo ""

# --- The published assembly, as it exists in the image's app directory.
IMAGE_DLL="$(
  docker run --rm --entrypoint sh "$IMAGE_TAG" -c \
    "ls -1 /app/${PRODUCT_PREFIX}*.dll 2>/dev/null | head -1" || true
)"
IMAGE_DLL="$(basename "${IMAGE_DLL:-}" 2>/dev/null || true)"

if [[ -z "$IMAGE_DLL" ]]; then
  echo -e "${RED}FAIL: no /app/${PRODUCT_PREFIX}*.dll found in the built image.${NC}"
  echo "Published assemblies present:"
  docker run --rm --entrypoint sh "$IMAGE_TAG" -c 'ls -1 /app/*.dll 2>/dev/null | head -20' || true
  exit 1
fi

# --- The assembly filename the COPIED entrypoint actually executes, read out of the image
#     (not out of the source tree) so a missed COPY cannot hide.
ENTRYPOINT_LINE="$(
  docker run --rm --entrypoint sh "$IMAGE_TAG" -c 'cat /app/entrypoint.sh' 2>/dev/null || true
)"
ENTRYPOINT_DLL="$(printf '%s\n' "$ENTRYPOINT_LINE" | grep -oE '[A-Za-z0-9._-]+\.dll' | head -1 || true)"

if [[ -z "$ENTRYPOINT_DLL" ]]; then
  echo -e "${RED}FAIL: could not read a .dll filename out of /app/entrypoint.sh in the image.${NC}"
  echo "Entrypoint contents:"
  printf '%s\n' "$ENTRYPOINT_LINE"
  exit 1
fi

echo "  image assembly    : $IMAGE_DLL"
echo "  entrypoint invokes: $ENTRYPOINT_DLL"
echo ""

if [[ "$IMAGE_DLL" == "$ENTRYPOINT_DLL" ]]; then
  echo -e "${GREEN}OK: entrypoint references ${ENTRYPOINT_DLL}${NC}"
  exit 0
fi

echo -e "${RED}FAIL: image has ${IMAGE_DLL}, entrypoint wants ${ENTRYPOINT_DLL}${NC}"
echo ""
echo "The container will start, fail to find the assembly, and exit -- while every local"
echo "build, test, lint and Playwright gate stays GREEN. Update both sides together:"
echo "  * ${ENTRYPOINT_SRC} (the deployed entrypoint exec line)"
echo "  * docker/api.Dockerfile (the local Compose ENTRYPOINT, and the watch-mode one)"
exit 1
