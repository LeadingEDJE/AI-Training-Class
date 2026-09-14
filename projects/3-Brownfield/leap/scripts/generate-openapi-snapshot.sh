#!/usr/bin/env bash
# Regenerate the committed compass-v1 OpenAPI snapshot from the code (#141).
#
# Run this whenever you change the /api/compass/v1 surface (a new route, a DTO field, a status code).
# It builds the API with build-time OpenAPI emission ON, then writes the compass-v1 document to the
# committed snapshot the diff gate checks. Commit the result in the SAME change as the code.
#
# The snapshot is the REAL generated surface, not a hand-authored contract — scripts/check-openapi-
# contract.sh fails if the two drift, so this file can never quietly diverge from the code.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

EMITTED="api/obj/openapi/LeadingEDJE.Leap.Api_compass-v1.json"
SNAPSHOT="api/Modules/Compass/Contracts/compass-v1.openapi.json"

echo "[openapi-snapshot] building api with OpenAPI emission on..."
dotnet build api/LeadingEDJE.Leap.Api.csproj -p:OpenApiGenerateDocumentsOnBuild=true --nologo -v quiet

if [[ ! -f "$EMITTED" ]]; then
  echo "[openapi-snapshot] ERROR: expected emitted document at $EMITTED but it is missing." >&2
  echo "[openapi-snapshot] Is AddOpenApi(\"compass-v1\", ...) still registered in api/Program.cs?" >&2
  exit 1
fi

mkdir -p "$(dirname "$SNAPSHOT")"
# Normalize into the committed form: LF newlines AND embedded-CR stripped from string values, so the
# snapshot is byte-identical whether generated on Windows or the Linux CI runner. See the header of
# scripts/lib/normalize-openapi.py for why both are needed.
python3 "$REPO_ROOT/scripts/lib/normalize-openapi.py" "$EMITTED" "$SNAPSHOT"

echo "[openapi-snapshot] wrote $SNAPSHOT"
