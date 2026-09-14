# ==============================================================================
# React SPA Dockerfile -- Multi-stage (build / prod / dev)
# Build context must be the repo root (context: .) for npm workspace resolution.
# ==============================================================================

# --- BUILD stage (CI / release builds) ---------------------------------------
FROM node:26-slim AS build

WORKDIR /app

ENV HUSKY=0

COPY package.json package-lock.json ./
# Compass SPA is a root npm workspace member, so it installs through the same
# `npm ci --workspace=...` invocation below. Its manifest is copied here for layer caching.
COPY web/compass/package.json ./web/compass/
COPY packages/api-client/package.json ./packages/api-client/

# Two-step install: ci first for speed, then fix rollup native binary.
# npm ci with --omit=optional skips the lockfile's platform-specific optional deps, which also
# skips the rollup native binary the build actually needs. A targeted npm install adds it back.
#
# The binary name is derived from the BUILD ARCH rather than hardcoded to x64 (49-08). It used to
# read `@rollup/rollup-linux-x64-gnu` literally, which makes this Dockerfile unbuildable on an
# arm64 host: npm refuses with `EBADPLATFORM ... wanted {"cpu":"x64"} (current: {"cpu":"arm64"})`.
# CI builds on amd64 so it never surfaced there, but it meant nobody on an arm64 machine could
# build this image at all — including to assert nginx routing against a REAL image, which is the
# only way the /compass/ deep-link class of defect is visible. On amd64 the expression resolves to
# the identical `@rollup/rollup-linux-x64-gnu`, so CI behaviour is unchanged.
RUN npm ci --ignore-scripts --omit=optional --workspace=@leap/compass --workspace=@leap/api-client && \
    npm install --ignore-scripts "@rollup/rollup-linux-$(node -p "process.arch === 'arm64' ? 'arm64' : 'x64'")-gnu"

COPY web/compass/ ./web/compass/
COPY packages/ ./packages/

# Build the Compass SPA into web/compass/dist (vite base '/compass/').
#
# Deliberately NO build args. Compass resolves its API base at RUNTIME from the same
# window.__API_BASE_URL__ global the shell uses; that value is empty (same-origin relative), so it
# works unchanged under the /compass/ path mount.
WORKDIR /app/web/compass
RUN npm run build

# --- PROD stage (static file serving via nginx) ------------------------------
FROM nginx:1.31-alpine@sha256:72ba65eb42c10344912a84ff42408db7d34f2feb642204570ab8fc5ffd29f1d3 AS prod

# Clears any CVEs patched upstream between base-image publish and this build.
# Privileged nginx runs as root by default; no USER wrapping needed.
RUN apk upgrade --no-cache

# The hand-rolled static LEAP shell (no build stage — framework-free files copied straight from
# the build context) owns the html root and is served by nginx at / (its top-level PWA surface
# manifest.webmanifest + sw.js ride this root scope). Compass is the only remaining dist tree: the
# destination directory name, the Vite `base` in web/compass/vite.config.ts, and the
# `location /compass/` prefix in nginx.conf must all read `/compass/` — a mismatch in any one of
# the three serves a blank page.
COPY --from=build /app/web/compass/dist /usr/share/nginx/html/compass
COPY web/shell/ /usr/share/nginx/html/
COPY docker/nginx.conf /etc/nginx/templates/default.conf.template

# Phase 41: nginx proxies /api,/auth,/Saml2 to the API on this same origin. Restrict envsubst to
# the named config vars so nginx's own runtime variables ($host, $uri, $proxy_add_x_forwarded_for,
# $http_x_forwarded_proto) survive template rendering and are not blanked out.
# Every substituted name in docker/nginx.conf's /config.js MUST appear in the filter AND have an
# ENV default here in lockstep, or the literal placeholder survives envsubst (Pitfall 3).
ENV NGINX_ENVSUBST_FILTER="API_URL|IS_EPHEMERAL_PREVIEW|EPHEMERAL_LABEL"
ENV API_URL=http://localhost:5000
ENV IS_EPHEMERAL_PREVIEW=""
ENV EPHEMERAL_LABEL=""

EXPOSE 80

# --- DEV stage (used by Docker Compose for local development) -----------------
FROM node:26-slim AS dev

WORKDIR /app

# Skip husky git hooks in container builds
ENV HUSKY=0

# Copy workspace root files first (layer caching)
COPY package.json package-lock.json ./

# Copy workspace member package.json files
COPY web/compass/package.json ./web/compass/
COPY packages/api-client/package.json ./packages/api-client/

RUN npm ci --ignore-scripts --workspace=@leap/compass --workspace=@leap/api-client

# Copy source
COPY web/compass/ ./web/compass/
COPY packages/ ./packages/

WORKDIR /app/web/compass

EXPOSE 5176

CMD ["npx", "vite", "--host", "0.0.0.0", "--port", "5176", "--strictPort"]
