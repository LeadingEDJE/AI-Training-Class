.PHONY: build test test-integration test-e2e-compass lint lint-backend lint-frontend format format-backend format-frontend clean dev down reset-db db-ensure migrate migration-add migration-check verify verify-full generate-api coverage setup dev-up dev-all e2e-stack-up e2e-stack-down

# The local database name, read from .env with the chart default as the fallback. ONE definition,
# used by every target below.
#
# It used to be the literal `timesheet_dev` in nine places. That is a trap once the name is
# configurable: `make reset-db` would happily DROP a database this repository no longer owns while
# the stack came up against a different one. Deriving it means every target manages the database it
# is actually configured for -- including a developer who overrides POSTGRES_DB for their own reasons.
#
# The Postgres username (`timesheet`) is NOT derived and stays hard-coded, for reasons tied to the
# hosted deployment this workshop copy does not include -- leave it alone rather than "fixing" the
# apparent inconsistency with the derived database name above.
POSTGRES_DB := $(strip $(shell sed -n 's/^POSTGRES_DB=//p' .env 2>/dev/null | head -1))
ifeq ($(POSTGRES_DB),)
POSTGRES_DB := leap_dev
endif

# The host every local target reaches Postgres on. `?=` so an existing environment value still wins,
# which is how CI keeps its own `localhost` and how a developer points at a remote database.
#
# 127.0.0.1 rather than `localhost`, DELIBERATELY. On Windows `localhost` resolves to ::1 first, and
# Docker Desktop's WSL2 backend serves that leg through `wslrelay.exe`. When WSL's network stack is
# wedged -- which it silently can be; `ss` inside the distro fails with WSAENOBUFS -- that relay
# ACCEPTS the TCP connection and then aborts it. Every layer above reports the wrong thing: `docker ps`
# says healthy, a plain TCP connect test succeeds, and only a real Postgres handshake shows the abort.
# The API then retries 60 times and reports "Database server localhost:5432 was not reachable",
# pointing at Docker, which was fine all along.
#
# 127.0.0.1 skips name resolution and the broken leg with it. It is equally correct on Linux and
# macOS, where the published port binds 0.0.0.0 -- so this is not a Windows-only workaround bolted
# onto a shared default.
POSTGRES_HOST ?= 127.0.0.1
# EXPORTED, because `dev-all` and `e2e-stack-up` launch the API with a bare `dotnet run` that does not
# name this variable. A make variable is not in a recipe child's environment unless it is exported or
# came from the environment, so without this line the migrations would reach 127.0.0.1 and the API
# would still fall back to its own default -- fixed in one place and broken in the other.
export POSTGRES_HOST

# AUTH MODE for `dev-all`. Default DevBypass; real Google-SAML handshake opt-in via `SAML=1`.
#
# `make dev-all` is what docs/TEAM-HANDOFF.md tells a new developer to run, so it MUST produce an
# app they can actually use. It used to force the real Sustainsys handshake
# (Auth__DevBypass__Enabled=false + the three Saml2__* vars), which left a fresh clone unable to
# sign in at all -- and BOTH documented ways in were dead ends:
#
#   1. The handshake itself 500s over plain HTTP. Sustainsys sets its correlation cookie
#      `Saml2.<relayState>` with `samesite=none` and NO `Secure` attribute, and browsers have
#      rejected SameSite=None-without-Secure since Chrome 80, so the cookie is never stored. The
#      IdP's POST back to /Saml2/Acs then finds no state and the handler throws
#      UnexpectedInResponseToException -> HTTP 500. The IdP's own login page works, which is what
#      makes this read as an app bug rather than a cookie-policy one.
#   2. `/auth/stub-login`, named as the escape hatch, 302s to
#      /auth/access-denied?msg=Unauthorized%20domain, because this target also set
#      GoogleAuth__AllowedDomain=example.test and the stub identity is not in that domain.
#
# So the real handshake needs an HTTPS origin, where SameSite=None is legal. Unlike the deleted
# timesheet SPA, Compass's vite.config.ts carries no mkcert/httpsCerts wiring, so that HTTPS leg
# does not exist locally today -- SAML=1 boots the mock IdP and the API's real Sustainsys path, but
# the browser handshake will still hit the plain-http SameSite=None-without-Secure 500 described
# above until Compass grows the same certs option timesheet had. Tracked as a known gap, not fixed
# in this pass; SAML=1 remains useful for confirming the IdP metadata and API-side wiring.
# AUTH_BANNER / SAML_IDP_BANNER exist so the SUCCESS BANNER cannot contradict the mode actually
# booted. The banner used to print "Auth: mock Google SAML ... form at http://localhost:8090" and a
# "SAML IdP:" line UNCONDITIONALLY, because those two echoes sat outside this conditional -- so the
# DevBypass default advertised a sign-in form and an IdP that were not running (nothing binds 8090
# unless SAML=1 brings up the google-saml-mock container). A new developer's first instruction was
# therefore a dead link, and it reads as a broken stack rather than a stale string. Deriving both
# lines from SAML here means the banner is wrong only if this block is wrong.
SAML ?= 0
# The SP origin for the mock-IdP handshake -- Compass's own dev server, plain http (see the HTTPS
# note above). Exported to `docker compose` for SAML_SP_ORIGIN in compose.yaml and used for
# Saml2__EntityId below.
WEB_ORIGIN := http://localhost:5176
SAML_SP_ORIGIN := $(WEB_ORIGIN)
ifeq ($(SAML),1)
AUTH_MODE_LABEL := SAML-on, real Sustainsys handshake against the mock Google IdP
AUTH_BANNER := mock Google SAML -- real Sustainsys handshake; sign-in form at http://localhost:8090 (escape hatch: /auth/stub-login)
SAML_IDP_BANNER := http://localhost:8090/simplesaml/ (google-saml-mock)
AUTH_ENV := Saml2__EntityId=$(SAML_SP_ORIGIN)/Saml2 \
	 Saml2__IdentityProvider__EntityId=http://localhost:8090/simplesaml/saml2/idp/metadata.php \
	 Saml2__IdentityProvider__MetadataLocation=http://localhost:8090/simplesaml/saml2/idp/metadata.php \
	 GoogleAuth__AllowedDomain=example.test \
	 Auth__DevBypass__Enabled=false
else
AUTH_MODE_LABEL := DevBypass, auto-authenticated -- no sign-in needed (use SAML=1 for the real handshake)
AUTH_BANNER := DevBypass -- already authenticated, there is NO sign-in step (use SAML=1 for the real handshake)
SAML_IDP_BANNER :=
AUTH_ENV :=
endif

# Portable "free these TCP ports" helper. ONE definition, used by every teardown target below.
#
# It used to be `fuser -k <port>/tcp` inline, which is GNU-only and DOES NOT WORK ON macOS. macOS
# ships a BSD /usr/bin/fuser that takes FILENAMES and has no -k flag, so `fuser -k 5009/tcp` is a
# usage error ("Unknown option: k", exit 1), not a port kill. Every call site swallowed that with
# `|| true`, so `make dev-down` killed NOTHING while still printing "All services stopped." -- it
# stopped the containers, which made it look like it had worked. The orphaned API and Vite
# processes then held 5009/5173/5174/5176 and blocked the next `make dev-all`. This is the whole
# reason a macOS developer's second boot of the day fails for no visible reason.
#
# lsof is present on macOS and on the CI runners and reports the same thing on both, so it is the
# portable answer; fuser stays as the fallback for a Linux image without lsof.
#
# Git Bash on Windows has NEITHER lsof nor a working fuser -- `command -v` on both fails, so this
# used to fall into the fuser branch, run a command that doesn't exist, swallow that with
# `|| true`, and report success while nothing was killed (issue #319). `powershell.exe` is on
# PATH under Git Bash and Get-NetTCPConnection/Stop-Process do the job.
#
# The powershell.exe branch is gated on BOTH the tool AND `uname -s` matching MINGW/MSYS/CYGWIN --
# tool presence alone is not enough. WSL2 exposes the Windows `powershell.exe` on PATH via
# interop too (confirmed live: a stock Ubuntu WSL box has it), but Get-NetTCPConnection queries
# Windows' OWN TCP stack, which cannot see a socket bound inside the WSL2 Linux kernel -- even
# with this machine's mirrored networking mode enabled. Without the uname guard, a WSL box
# missing lsof would silently pick the powershell.exe branch, query the wrong kernel, find
# nothing, and `continue` with no "freed" and no "WARNING" -- the exact silent-false-success shape
# of #319, just relocated. `uname -s` reliably tells the two apart: MINGW*/MSYS*/CYGWIN* on Git
# Bash (a POSIX layer hosted directly on the Windows kernel, so Get-NetTCPConnection sees exactly
# what it binds), `Linux` inside WSL (a separate kernel from the one `powershell.exe` inspects).
#
# Teardown must not fail open, so this VERIFIES rather than assuming: TERM, wait for the port to
# actually go quiet, then escalate to KILL and say so. A port that never frees is reported as a
# failure instead of being reported as success. Every branch below -- lsof, powershell.exe, AND
# fuser -- follows this same verify-then-report discipline; a branch that can fail silently
# defeats the INCOMPLETE/hard-fail checks in dev-down/e2e-stack-up that key off the WARNING text.
# The true "no tool at all" case also prints a loud WARNING instead of silently swallowing the
# failure the way the old bare `fuser` fallback did.
define kill_ports
if command -v lsof >/dev/null 2>&1; then \
	for p in $(1); do \
		pids=$$(lsof -nP -tiTCP:$$p -sTCP:LISTEN 2>/dev/null || true); \
		[ -n "$$pids" ] || continue; \
		kill $$pids 2>/dev/null || true; \
		for i in $$(seq 1 20); do \
			lsof -nP -tiTCP:$$p -sTCP:LISTEN >/dev/null 2>&1 || break; \
			sleep 0.5; \
		done; \
		if lsof -nP -tiTCP:$$p -sTCP:LISTEN >/dev/null 2>&1; then \
			echo "  port $$p ignored TERM -- sending KILL"; \
			kill -9 $$(lsof -nP -tiTCP:$$p -sTCP:LISTEN 2>/dev/null) 2>/dev/null || true; \
			sleep 1; \
		fi; \
		if lsof -nP -tiTCP:$$p -sTCP:LISTEN >/dev/null 2>&1; then \
			echo "  WARNING: port $$p is STILL held by $$(lsof -nP -tiTCP:$$p -sTCP:LISTEN 2>/dev/null | tr '\n' ' ')-- the next boot will fail to bind"; \
		else \
			echo "  freed port $$p"; \
		fi; \
	done; \
elif command -v powershell.exe >/dev/null 2>&1 && uname -s 2>/dev/null | grep -qE '^(MINGW|MSYS|CYGWIN)'; then \
	for p in $(1); do \
		pids=$$(powershell.exe -NoProfile -NonInteractive -Command "(Get-NetTCPConnection -State Listen -LocalPort $$p -ErrorAction SilentlyContinue).OwningProcess" 2>/dev/null | tr -d '\r' | sort -u); \
		[ -n "$$pids" ] || continue; \
		for pid in $$pids; do \
			powershell.exe -NoProfile -NonInteractive -Command "Stop-Process -Id $$pid -Force -ErrorAction SilentlyContinue" >/dev/null 2>&1; \
		done; \
		for i in $$(seq 1 20); do \
			still=$$(powershell.exe -NoProfile -NonInteractive -Command "(Get-NetTCPConnection -State Listen -LocalPort $$p -ErrorAction SilentlyContinue).OwningProcess" 2>/dev/null | tr -d '\r'); \
			[ -n "$$still" ] || break; \
			sleep 0.5; \
		done; \
		still=$$(powershell.exe -NoProfile -NonInteractive -Command "(Get-NetTCPConnection -State Listen -LocalPort $$p -ErrorAction SilentlyContinue).OwningProcess" 2>/dev/null | tr -d '\r'); \
		if [ -n "$$still" ]; then \
			echo "  WARNING: port $$p is STILL held by PID(s) $$(echo $$still | tr '\n' ' ')-- the next boot will fail to bind"; \
		else \
			echo "  freed port $$p"; \
		fi; \
	done; \
elif command -v fuser >/dev/null 2>&1; then \
	for p in $(1); do \
		pids=$$(fuser $$p/tcp 2>/dev/null); \
		[ -n "$$pids" ] || continue; \
		fuser -k -TERM $$p/tcp >/dev/null 2>&1 || true; \
		for i in $$(seq 1 20); do \
			fuser $$p/tcp >/dev/null 2>&1 || break; \
			sleep 0.5; \
		done; \
		if fuser $$p/tcp >/dev/null 2>&1; then \
			echo "  port $$p ignored TERM -- sending KILL"; \
			fuser -k -KILL $$p/tcp >/dev/null 2>&1 || true; \
			sleep 1; \
		fi; \
		if fuser $$p/tcp >/dev/null 2>&1; then \
			echo "  WARNING: port $$p is STILL held by PID(s) $$(echo $$(fuser $$p/tcp 2>/dev/null) | tr '\n' ' ')-- the next boot will fail to bind"; \
		else \
			echo "  freed port $$p"; \
		fi; \
	done; \
else \
	echo "  WARNING: no port-freeing tool available (need lsof, fuser, or PowerShell) -- ports $(1) were NOT verified or freed; stop them manually"; \
fi
endef

build:
	dotnet build leap.slnx

test:
	dotnet test --project tests/unit

test-integration:
	dotnet test --project tests/integration

# Compass's own dedicated E2E (T006). Runs web/compass/playwright.config.ts's
# compass-chromium-critical project against Compass's own dev server -- see that config's
# own comment for why it needs neither the (deleted) timesheet nor ooto dev server.
test-e2e-compass:
	cd web/compass && npx playwright test

lint: lint-backend lint-frontend

lint-backend:
	dotnet format leap.slnx --verify-no-changes

lint-frontend:
	npm run lint --workspace=@leap/compass

format: format-backend format-frontend

format-backend:
	dotnet format leap.slnx

format-frontend:
	npm run format --workspace=@leap/compass

generate-api:
	@OPENAPI_URL=$${OPENAPI_URL:-http://localhost:5009/openapi/v1.json}; \
	echo "Generating API client from $$OPENAPI_URL"; \
	npx openapi-typescript "$$OPENAPI_URL" -o packages/api-client/generated/schema.d.ts

coverage:
	dotnet test --project tests/unit -- \
		--coverage --coverage-output-format cobertura \
		--coverage-output coverage.cobertura.xml
	@echo "Coverage report generated. Install reportgenerator for HTML: dotnet tool install -g dotnet-reportgenerator-globaltool"

clean:
	dotnet clean

# The compose stack, with an EXPLICIT migrate step.
#
# The api container applies no schema when it starts (Phase 51 -- schema arrives from the pre-rollout
# hook in the cluster and from a step like this one locally). Until then this target only ran
# `migration-check`, which WARNS about pending migrations and exits zero; the schema a fresh volume
# ended up with was built by the api container for itself. With that gone, an un-migrated `make dev`
# is a wall of missing-relation errors on a developer's first command.
#
# Warning replaced by action, which is strictly stronger. Same shape as dev-up / e2e-stack-up:
# Postgres up first, wait for it, apply, then start everything else against a migrated database.
dev:
	@test -f .env || (echo "Error: .env file not found. Run: cp .env.example .env" && exit 1)
	docker compose up -d postgres
	@echo "Waiting for Postgres..."
	@for i in $$(seq 1 30); do docker compose exec postgres pg_isready -U timesheet -d $(POSTGRES_DB) >/dev/null 2>&1 && break || sleep 2; done
	@$(MAKE) --no-print-directory db-ensure
	@echo "Applying database migrations..."
	@POSTGRES_PASSWORD=localdevwhocares POSTGRES_HOST=$(POSTGRES_HOST) dotnet ef database update --project api --context LeapDbContext
	docker compose up --build

down:
	docker compose down

reset-db:
	docker compose down -v
	docker compose up --build -d postgres
	@echo "Waiting for Postgres..."
	@for i in $$(seq 1 30); do docker compose exec postgres pg_isready -U timesheet -d $(POSTGRES_DB) >/dev/null 2>&1 && break || sleep 2; done
	@echo "Recreating database schema on Postgres..."
	@docker compose exec postgres psql -U timesheet -d postgres -c "DROP DATABASE IF EXISTS $(POSTGRES_DB) WITH (FORCE);" || true
	@docker compose exec postgres psql -U timesheet -d postgres -c "CREATE DATABASE $(POSTGRES_DB);" || true
	@$(MAKE) --no-print-directory db-ensure
	@echo "Applying migrations..."
	@POSTGRES_PASSWORD=localdevwhocares POSTGRES_HOST=$(POSTGRES_HOST) dotnet ef database update --project api --context LeapDbContext
	@echo "Postgres volume removed and recreated; fresh schema applied. Run 'make dev-up' to start the stack."

# Creates the configured database if it is absent, using THE SHIPPED BOOTSTRAP -- the same
# `--ensure-database` mode the cluster's migration hook runs as its init container, not a hand-written
# psql call that could drift from it.
#
# This is what makes the additive Phase 51 rename painless for a developer whose Postgres volume
# predates it: the volume already ran initdb for the OLD database, and initdb never runs again, so the
# new one does not exist. Without this the first command after pulling the change is a wall of
# missing-relation errors. Idempotent, so running it when the database already exists is a no-op, and
# it degrades with a warning rather than crashing when the role cannot create databases.
db-ensure:
	@POSTGRES_PASSWORD=localdevwhocares POSTGRES_HOST=$(POSTGRES_HOST) POSTGRES_DB=$(POSTGRES_DB) \
		dotnet run --project api --no-launch-profile -- --ensure-database

migrate: db-ensure
	POSTGRES_PASSWORD=localdevwhocares POSTGRES_HOST=$(POSTGRES_HOST) dotnet ef database update --project api --context LeapDbContext

# --output-dir is relative to the PROJECT directory. Plan 48-12 moved the migrations
# under Platform/ and re-pointed this in the SAME commit as the move. A stale value
# silently creates a SECOND migrations folder -- EF discovers migrations by the
# [Migration] attribute, not by folder, so the build stays green and the split is
# invisible until a human looks.
# Keep `read` joined to the command that uses $$name: separate recipe lines are separate
# shells, and a `@#` line inside the join is not a comment -- make hands it to sh.
migration-add:
	@read -p "Migration name: " name; \
	if [ -z "$$name" ]; then echo "Migration name is required."; exit 1; fi; \
	dotnet ef migrations add "$$name" --project api --output-dir Platform/Data/Migrations --context LeapDbContext

migration-check:
	@dotnet ef migrations list --project api --no-connect --context LeapDbContext 2>/dev/null | grep -q "(Pending)" && \
		echo "WARNING: Pending migrations detected. Run 'make migrate' to apply." || true

# Apply pending migrations to a POPULATED throwaway database and report timings + row preservation.
# Closes the gap that EVERY other gate in this repo migrates an EMPTY database while a deployed
# upgrade migrates a populated one -- see docs/ops/populated-migration-rehearsal.md.
# BASELINE is the migration the target environment is currently on; `make rehearse-migration-list`
# prints the ordered chain.
rehearse-migration:
	@if [ -z "$(BASELINE)" ]; then \
		echo "BASELINE is required, e.g. make rehearse-migration BASELINE=20260810121031_AddAuditLogEffectiveRoles"; \
		echo "Run 'make rehearse-migration-list' to see the ordered migration chain."; \
		exit 2; \
	fi
	bash scripts/rehearse-migration.sh --baseline $(BASELINE) $(if $(ROWS),--rows $(ROWS),)

rehearse-migration-list:
	@bash scripts/rehearse-migration.sh --list

verify: build test
	@echo "=== Build + Tests passed ==="
	@echo "Stopping Docker API (if running) to free port 5009..."
	@docker compose stop api 2>/dev/null || true
	@echo "Starting Postgres..."
	@docker compose up -d postgres
	@echo "Waiting for Postgres healthy..."
	@for i in $$(seq 1 30); do \
		docker compose exec postgres pg_isready -U timesheet -d $(POSTGRES_DB) >/dev/null 2>&1 && break || sleep 2; \
	done
	@$(MAKE) --no-print-directory db-ensure
	@echo "Applying migrations..."
	@POSTGRES_PASSWORD=localdevwhocares POSTGRES_HOST=$(POSTGRES_HOST) dotnet ef database update --project api --context LeapDbContext
	@echo "Starting API..."
	@POSTGRES_PASSWORD=localdevwhocares dotnet run --project api &
	@API_PID=$$!; \
	echo "Waiting for API..."; \
	for i in $$(seq 1 20); do \
		curl -sf http://localhost:5009/health >/dev/null 2>&1 && break || sleep 2; \
	done; \
	echo "=== Verifying endpoints ==="; \
	echo "GET /health:"; \
	curl -sf http://localhost:5009/health | python3 -m json.tool; \
	echo ""; \
	echo "GET /api/time-categories:"; \
	curl -sf http://localhost:5009/api/time-categories | python3 -m json.tool; \
	echo ""; \
	echo "=== Verification complete ==="; \
	kill $$API_PID 2>/dev/null; \
	docker compose stop postgres

# This target is the full regression check, so it must be able to FAIL.
#
# The Playwright line below used to end with:
#     || echo "Playwright critical tests not configured or failed -- review output above"
# which is the identical swallow construct that was removed from a pre-push git hook on evidence:
# there it converted a real "2 failed / 189 passed" run into "all checks passed" and hid a
# genuine HTTP 500 for two waves. A gate that turns red into green is worse than no gate, because
# it manufactures confidence instead of merely lacking it. `make` already aborts a target on a
# non-zero recipe line, so removing the fallback is the entire fix.
#
# Do NOT reintroduce a '||' fallback here. If Playwright cannot start, fail loudly on the real
# cause -- copy the pre-push hook's explicit missing-node_modules guard instead of swallowing.
verify-full: verify
	@echo "=== Frontend build ==="
	npm run build --workspace=@leap/compass
	@echo "=== Playwright critical tests ==="
	cd web/compass && npx playwright test --project='compass-chromium-critical'

setup:
	@echo "Installing root workspaces (compass + packages)..."
	@# This single `npm ci` covers web/compass and packages/* — the only root workspace members
	@# left since web/timesheet and web/ooto were removed. Compass needs NO separate install step,
	@# which is the whole point of it being a workspace member.
	npm ci
	@# dotnet-ef is required by dev-up/dev-all/migrate (dotnet ef database update). Restored from
	@# the local manifest (.config/dotnet-tools.json) rather than installed globally, so the
	@# version every `dotnet ef` invocation resolves is pinned per-repo, not whatever a developer
	@# happens to have installed globally.
	@echo "Restoring local dotnet tools (dotnet-ef)..."
	dotnet tool restore
	@echo "Setup complete. Run 'make dev-all' to boot the full monolith."

# Minimal target: database only. It starts NOTHING else -- it prints the API and frontend commands
# for you to run yourself -- exactly as the mock SAML container is left out. `dev-all` owns the
# full monolith.
dev-up:
	@echo "Starting development stack..."
	docker compose up -d postgres
	@echo "Waiting for Postgres..."
	@for i in $$(seq 1 20); do docker compose exec postgres pg_isready -U timesheet -d $(POSTGRES_DB) >/dev/null 2>&1 && break || sleep 3; done
	@$(MAKE) --no-print-directory db-ensure
	@echo "Applying database migrations..."
	@POSTGRES_PASSWORD=localdevwhocares POSTGRES_HOST=$(POSTGRES_HOST) dotnet ef database update --project api --context LeapDbContext
	@echo "Stack ready. Start API: POSTGRES_PASSWORD=localdevwhocares dotnet run --project api"
	@echo "Start frontend: npm run dev --workspace=@leap/compass -- --port 5176 --strictPort"

dev-all: dev-up
	@# Fail-fast guard: dependencies must be installed before booting anything (D-01 cold clone).
	@# Compass IS a root workspace member, so the root npm ci already installed it — there is
	@# deliberately no separate install step for it. Its node_modules is the hoisted root one, so
	@# the guard checks that the workspace link resolved.
	@test -d node_modules/@leap/compass || (echo "node_modules/@leap/compass missing — run 'make setup' first" && exit 1)
	@# The mock Google IdP is only needed for the real handshake, so it boots ONLY under SAML=1 —
	@# the default path does not pay for a container it never talks to. Sustainsys fetches IdP
	@# metadata lazily at the FIRST /auth/login challenge (not at API boot), so the wait loop makes
	@# ordering deterministic: the container must serve metadata before the first login.
	@if [ "$(SAML)" = "1" ]; then \
	  SAML_SP_ORIGIN=$(SAML_SP_ORIGIN) docker compose up -d google-saml-mock; \
	  echo "Waiting for mock Google SAML metadata..."; \
	  for i in $$(seq 1 20); do curl -sf http://localhost:8090/simplesaml/saml2/idp/metadata.php -o /dev/null 2>/dev/null && break || sleep 2; done; \
	  echo ""; \
	  echo "!!! SAML=1 WILL FAIL the browser handshake at /Saml2/Acs with HTTP 500."; \
	  echo "!!! Sustainsys sets its correlation cookie SameSite=None with no Secure attribute, and"; \
	  echo "!!! browsers reject that over plain HTTP, so the callback finds no state to correlate."; \
	  echo "!!! Unlike the deleted timesheet SPA, web/compass/vite.config.ts has no mkcert/https"; \
	  echo "!!! wiring yet, so there is no local fix for this today -- SAML=1 is still useful to"; \
	  echo "!!! confirm the mock IdP serves metadata and that the API resolves it, just not for a"; \
	  echo "!!! full browser sign-in. Drop SAML=1 and use the DevBypass default for that."; \
	  echo ""; \
	  if [ -f api/appsettings.Local.json ]; then \
	    echo ""; \
	    echo "!!! WARNING: api/appsettings.Local.json exists and OUTRANKS these env vars."; \
	    echo "!!! dev-all may target real Google instead of the mock. Rename or delete it."; \
	    echo ""; \
	  fi; \
	fi
	@echo "Starting API ($(AUTH_MODE_LABEL))..."
	@# AUTH_ENV is empty by default (DevBypass, which defaults ON in Development) and carries the
	@# SAML on-switch under SAML=1 — the three Saml2__* vars are the all-or-nothing gate, env
	@# presence alone flips SAML on, and they live only here, never in tracked appsettings.
	@# GoogleAuth__AllowedDomain=example.test goes with them because every seeded person email is
	@# @example.test. See the AUTH MODE block at the top of this file for why the default flipped.
	@POSTGRES_PASSWORD=localdevwhocares \
	 $(AUTH_ENV) \
	 dotnet run --project api &
	@echo "Waiting for API to be ready..."
	@for i in $$(seq 1 20); do curl -sf http://localhost:5009/health -o /dev/null 2>/dev/null && break || sleep 2; done
	@# Compass SPA: a root workspace member, so it starts through the normal workspace tooling.
	@# There is no shell/proxy origin fronting it locally (the timesheet SPA used to fill that role
	@# on :5173) -- Compass's own vite dev server proxies /api, /auth and /Saml2 straight to the API
	@# (see web/compass/vite.config.ts), so it is reachable and usable on its own port. The LEAP
	@# shell (web/shell/) is only exercised through the nginx production image, not this target.
	@# --strictPort makes a port clash fail loudly instead of binding elsewhere, where a developer
	@# would open the tab and find nothing listening.
	@echo "Starting compass SPA..."
	@npm run dev --workspace=@leap/compass -- --port 5176 --strictPort &
	@echo "Waiting for compass SPA..."
	@for i in $$(seq 1 15); do curl -sf http://localhost:5176/compass/ -o /dev/null 2>/dev/null && break || sleep 2; done
	@# ASSERT, don't assume. Every wait loop above is `curl ... && break || sleep` with NO failure
	@# path: when the retries run out the loop simply ends, so the recipe walked straight into the
	@# "=== Full stack running ===" banner and exited 0 even when the API never bound and Compass was
	@# dead. That is how a failed boot came to look exactly like a successful one -- the banner and
	@# the exit code were both unconditional, and the only real signal was buried in the log.
	@#
	@# Note this asserts REACHABILITY, which is what the banner claims -- it cannot tell our fresh
	@# process from an orphan of an earlier run answering on the same port. Confirm that with:
	@#   macOS/Linux:  ps -o pid,lstart,command -p $$(lsof -nP -tiTCP:5009 -sTCP:LISTEN)
	@#   Windows:      Get-Process -Id (Get-NetTCPConnection -LocalPort 5009 -State Listen).OwningProcess
	@failed=""; \
	curl -sf http://localhost:5009/health -o /dev/null 2>/dev/null || failed="$$failed\n  API        http://localhost:5009/health"; \
	curl -sf http://localhost:5176/compass/ -o /dev/null 2>/dev/null || failed="$$failed\n  Compass    http://localhost:5176/compass/"; \
	if [ -n "$$failed" ]; then \
		echo ""; \
		echo "=== STACK FAILED TO START ==="; \
		printf 'These services never became reachable:%b\n' "$$failed"; \
		echo ""; \
		echo "Most likely causes, in the order worth checking:"; \
		echo "  1. A port was already held by an orphan from an earlier run. Check with:"; \
		echo "       macOS/Linux:  for p in 5009 5176; do lsof -nP -iTCP:\$$p -sTCP:LISTEN; done"; \
		echo "       Windows:      Get-NetTCPConnection -State Listen -LocalPort 5009,5176 | Select LocalPort,OwningProcess"; \
		echo "     Then tear down with 'make dev-down' and retry."; \
		echo "  2. The API failed to bind or threw at startup -- scroll up for AddressInUseException"; \
		echo "     or an unhandled exception."; \
		echo "  3. Dependencies are installed under the wrong Node version. Run 'nvm use' (see"; \
		echo "     .nvmrc), then re-run 'make setup' -- switching Node alone is not enough."; \
		echo ""; \
		exit 1; \
	fi
	@echo ""
	@echo "=== Full stack running ==="
	@echo "  Postgres: localhost:5432 ($(POSTGRES_DB) schema)"
	@echo "  Auth:     $(AUTH_BANNER)"
	@if [ -n "$(SAML_IDP_BANNER)" ]; then echo "  SAML IdP: $(SAML_IDP_BANNER)"; fi
	@echo "  API:      http://localhost:5009"
	@echo "  Compass:  $(WEB_ORIGIN)/compass/ (add ?employeeId=<uuid> to read a record)"
	@echo ""
	@echo "Stop with: make dev-down"

dev-down:
	@echo "Stopping API and SPA processes..."
	@tmp="$$(mktemp)"; \
	{ $(call kill_ports,5009 5176) ; } 2>&1 | tee "$$tmp"; \
	docker compose stop postgres google-saml-mock 2>/dev/null || true; \
	if grep -q WARNING "$$tmp"; then \
		echo "Teardown INCOMPLETE -- see WARNING(s) above; a port is still held and the next 'make dev-all' may fail to bind."; \
	else \
		echo "All services stopped."; \
	fi; \
	rm -f "$$tmp"

e2e-stack-up:
	@echo "=== Resetting E2E environment ==="
	@# Free any lingering processes on app ports (portable -- see the kill_ports define above).
	@# This one refuses to continue on a WARNING rather than just reporting it: an E2E run against
	@# a stale API on :5009 auto-authenticates via DevBypass and makes every role-denial assertion
	@# pass regardless of whether authorization actually works.
	@tmp="$$(mktemp)"; \
	{ $(call kill_ports,5009 5176) ; } 2>&1 | tee "$$tmp"; \
	if grep -q WARNING "$$tmp"; then \
		rm -f "$$tmp"; \
		echo "ERROR: could not verify ports 5009/5176 are free -- refusing to start the E2E stack against a possibly-stale API."; \
		exit 1; \
	fi; \
	rm -f "$$tmp"
	@# Start Postgres fresh if not running
	@docker compose up -d postgres
	@echo "Waiting for Postgres..."
	@for i in $$(seq 1 30); do docker compose exec postgres pg_isready -U timesheet -d $(POSTGRES_DB) >/dev/null 2>&1 && break || sleep 2; done
	@# Reset the DB on Postgres: drop + recreate, then re-migrate
	@docker compose exec postgres psql -U timesheet -d postgres -c "DROP DATABASE IF EXISTS $(POSTGRES_DB) WITH (FORCE);" || true
	@docker compose exec postgres psql -U timesheet -d postgres -c "CREATE DATABASE $(POSTGRES_DB);" || true
	@$(MAKE) --no-print-directory db-ensure
	@echo "Applying migrations..."
	@POSTGRES_PASSWORD=localdevwhocares POSTGRES_HOST=$(POSTGRES_HOST) dotnet ef database update --project api --context LeapDbContext
	@echo "=== E2E stack ready ==="

e2e-stack-down:
	@tmp="$$(mktemp)"; \
	{ $(call kill_ports,5009 5176) ; } 2>&1 | tee "$$tmp"; \
	docker compose stop postgres 2>/dev/null || true; \
	if grep -q WARNING "$$tmp"; then \
		echo "E2E stack teardown INCOMPLETE -- see WARNING(s) above."; \
	else \
		echo "E2E stack stopped"; \
	fi; \
	rm -f "$$tmp"
