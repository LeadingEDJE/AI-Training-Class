# LEAP Compass

Employee and SOW (statement of work) directory app: who works where, on what engagement,
reporting to whom. Trimmed to just the Compass module as a brownfield lab target.
Stack: .NET 10 minimal API, a React 19 SPA (Vite), and PostgreSQL 16.

## Prerequisites

- .NET 10 SDK
- Node 22 and npm 10 (`.npmrc` is engine-strict; npm 11 is rejected on purpose)
- Docker (for Postgres, and the optional mock SAML container)

## Run it

```
cp .env.example .env
make setup
make dev-all
```

- API: `http://localhost:5009`
- Compass: `http://localhost:5176/compass/`

## Signing in

`make dev-all` defaults to **DevBypass** — already signed in, no login step. `SAML=1
make dev-all` instead starts a mock IdP at `http://localhost:8090/simplesaml/`, but its
browser handshake 500s (no local HTTPS) — use DevBypass for a real sign-in; SAML=1 is
only useful to confirm the mock IdP itself responds.

Full setup, build/test commands, and a tour of the app: [`RUNNING.md`](RUNNING.md).
