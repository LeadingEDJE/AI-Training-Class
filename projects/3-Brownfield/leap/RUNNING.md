# Running LEAP Compass locally

## 1. What this app is

Compass is an internal-style employee and SOW (statement of work) directory: who works
where, on what engagement, reporting to whom, plus an admin area and a handful of
reports. This copy is trimmed from a larger monorepo down to just the Compass module —
a .NET 10 minimal API, a React 19 SPA (Vite), and PostgreSQL 16.

## 2. Prerequisites

- **.NET 10 SDK**
  - macOS: `brew install --cask dotnet-sdk`
  - Windows: `winget install Microsoft.DotNet.SDK.10`
  - Linux (Debian/Ubuntu): `sudo apt-get install -y dotnet-sdk-10.0`
  - Check: `dotnet --list-sdks` must show a `10.0.x` line.
- **Node 22 via nvm** — the repo pins the major version in `.nvmrc`.
  - macOS/Linux: `nvm install` (reads `.nvmrc`), then `nvm use`
  - Windows: use [nvm-windows](https://github.com/coreybutler/nvm-windows), `nvm install 22`, `nvm use 22`
  - Check: `node --version` starts with `v22`.
- **npm 10** — comes with Node 22. `.npmrc` sets `engine-strict=true`, so npm 11 is a
  hard error, not a warning. Check: `npm --version` starts with `10`.
- **Docker** (Postgres, plus the optional mock SAML container).

## 3. Build and test

From the `leap` folder:

```
dotnet build leap.slnx
dotnet test --project tests/unit/LeadingEDJE.Leap.Api.Tests.csproj
npm ci
npm run build -w web/compass
npm run test -w web/compass
```

`dotnet build` should be clean — 0 warnings, 0 errors. `dotnet test` on the unit project
has **2 known failures** out of 2200; see section 6. The integration project
(`tests/integration`) starts its own Postgres through Testcontainers, so it needs Docker
running; it has **4 known failures** out of 650, same bug, also section 6.

## 4. Run

```
cp .env.example .env
make setup
make dev-all
```

The manual equivalent, without `make`, from the `leap` folder. The .NET side does not read
`.env`, so the Postgres password has to be passed on the command line — this is the value
`.env.example` and every `make` target use:

```
docker compose up -d postgres
dotnet tool restore
POSTGRES_PASSWORD=localdevwhocares dotnet ef database update --project api --context LeapDbContext
POSTGRES_PASSWORD=localdevwhocares dotnet run --project api
```

In a second terminal:

```
npm run dev -w web/compass -- --port 5176 --strictPort
```

- API: `http://localhost:5009`
- Compass: `http://localhost:5176/compass/`

You're auto-signed-in by default (DevBypass). Run `SAML=1 make dev-all` instead to
also start a mock Google SAML identity provider at `http://localhost:8090/simplesaml/`
— useful to confirm the mock IdP itself responds, but the browser handshake 500s in
this local setup (no HTTPS, and the identity provider's correlation cookie needs it).

## 5. A tour

1. Open `http://localhost:5176/compass/` — the Team Directory, the app's front door.
2. Click an EDJEr (employee) row to open their detail page, then **Add assignment**
   to see the assignment form.
3. Switch to **Client Directory**, open a client, and again try adding an assignment
   from that side.
4. Open **Sales Dashboard**.
5. Open **Reports** — it defaults to Availability; the tabs switch to Assignment
   Duration, Assignment Start, and SOW Extension.
6. Open **Admin** — lookups, EDJErs, and clients each have their own management screen.

## 6. Known failing tests

All 6 are one real, pre-existing bug in `CompassReportEndpoints.cs`, on the assignment-start
and SOW-extension reports. `from` and `to` are non-nullable `DateOnly` parameters, and a
`from`/`to` the caller got wrong does not fail binding — it arrives as `default(DateOnly)`
(`0001-01-01`). The endpoint then answers **200 with a silently wrong date range** where the
tests expect a 400. It's a reasonable first bug to hunt down.

- `dotnet test --project tests/unit/LeadingEDJE.Leap.Api.Tests.csproj` — **2 failures**, both
  in `CompassReportEndpointsTests`, for an *unparseable* date (`?from=not-a-date`).
- `dotnet test --project tests/integration/LeadingEDJE.Leap.Api.IntegrationTests.csproj` —
  **4 failures**, in its own `CompassReportEndpointsTests`, for a *missing* `from`.

Note what the passing cases tell you: a missing `to` does return 400, but only by accident —
`default(DateOnly)` makes the handler's `from > to` guard fire. Nothing is validating input.

Several comments in `CompassReportEndpoints.cs` and in both test files predict a 500 here,
on the theory that this is a binding failure subject to `RouteHandlerOptions.ThrowOnBadRequest`.
It isn't: that flag is pinned `false` at `api/Program.cs:102`, and the observed answer is 200.
Those comments are part of the puzzle.

## 7. Common problems

- **`npm ci` fails with EBADENGINE**: you're on npm 11. `.npmrc` rejects it on purpose
  — install Node 22 (which ships npm 10) and re-run.
- **Wrong Node version generally**: run `nvm use` (reads `.nvmrc`), then re-run
  `npm ci` — switching Node alone doesn't refresh already-installed packages.
- **Port already in use** (`5009` or `5176`): a previous run is still listening.
  Find and stop it, or run `make dev-down`.
- **API can't reach Postgres / migrations fail**: Postgres wasn't ready yet. Re-run
  `docker compose up -d postgres` and wait a few seconds before `dotnet ef database update`.
- **`dotnet ef: command not found`**: the local tool wasn't restored. Run
  `dotnet tool restore` from the `leap` folder.
- **`SAML=1 make dev-all` and sign-in 500s**: expected here — see section 4. Drop
  `SAML=1` and use the DevBypass default to actually sign in.
