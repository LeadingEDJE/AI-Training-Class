# Local mock Google SAML

**Phase 47.** The `google-saml-mock` container is a **mock of our Google Workspace SAML app** —
not a generic identity provider. It impersonates the shape of the Google SAML app a deployed
LEAP would point at (same attribute shape, same `Compass-*-dev` group names asserted in
`docker/google-saml-mock/authsources.php`) so the **real Sustainsys.Saml2 handshake** runs locally
and in CI end-to-end, in a browser.

## What / why

- **Service:** `google-saml-mock` in `compose.yaml` — the maintained
  [`kenchan0130/simplesamlphp`](https://hub.docker.com/r/kenchan0130/simplesamlphp) image pinned
  at **1.19.9**, listening on host port **8090**. Metadata URL:
  `http://localhost:8090/simplesaml/saml2/idp/metadata.php`.
- **Why a mock and not real Google:** it lets every developer exercise the full handshake
  (`metadata load → /auth/login → SSO redirect → login form → signed assertion → /Saml2/Acs →
  saml-external cookie → /auth/login-callback → GoogleAuth:Groups role mapping → session cookie`)
  with **zero per-dev Google registration**, cold-clone, offline. The deployed config still points
  at the real Google SAML app; this only stands in for it locally/CI.
- **Always-on in `make dev-all` (D-02):** the container boots as part of the standard full local
  stack and the API starts SAML-enabled, so the real handshake is the **default local login
  experience** — auth regressions surface immediately rather than hiding behind stub-login.
- **Tool choice (D-01):** SimpleSAMLphp was re-confirmed after an initial PHP concern — the only
  PHP in the repo is one declarative `docker/google-saml-mock/authsources.php` roster mounted
  read-only into a prebuilt container (no PHP toolchain anywhere). The image is dormant (last push
  2024-04) but acceptable for a localhost/CI mock; **Keycloak is the documented runner-up** if this
  ever sours.

## Roster

The roster in `docker/google-saml-mock/authsources.php` was trimmed after the Timesheet and OOTO
modules were deleted — it used to carry one user per Timesheet/OOTO role too; those are gone along
with the UI that needed them. **Every password is the throwaway literal `password`.** Emails reuse
`AbsorbedDirectorySeeder` identities; roles come from the **asserted groups**, not the person row
(so several users deliberately share a person email). Group strings byte-match
`appsettings.Development.json` → `GoogleAuth:Groups[].GroupName`.

| username | email | groups asserted | resolves to (role) | purpose |
|----------|-------|-----------------|--------------------|---------|
| edjer | blair.dev@example.test | _(none)_ | baseline EDJEr | no privileged group |
| compass-superadmin | casey.dev@example.test | `Compass-SuperAdmin-dev` | Compass Super Admin | happy path |
| compass-admin | alex.coach@example.test | `Compass-Admin-dev` | Compass Admin | happy path |
| compass-ops | hank.deliver@example.test | `Compass-Ops-dev` | Compass Ops | happy path |
| compass-sales | casey.dev@example.test | `Compass-Sales-dev` | Compass Sales | happy path (shares casey.dev) |
| compass-ops-prod-plus-dev-superadmin | hank.deliver@example.test | `Compass-Ops`, `Compass-SuperAdmin-dev` | Compass Ops (Prod) / Compass Super Admin (elsewhere) | issue #53 regression: a bare prod group plus a higher-privilege `-dev` group at once — see `GoogleAuthServiceTests.MapGroupsToRoles_ProdAndDevGroupsBothPresentIn*` |
| outsider | outsider@gmail.com | `Compass-SuperAdmin-dev` | **DENIED** — wrong domain | D-08a: fails `GoogleAuth:AllowedDomain=example.test` |
| ghost | ghost@example.test | `Compass-SuperAdmin-dev` | **auto-created** person, then proceeds | D-08b: valid domain, no person row — auto-create path (quick task 260729-sqc), not a denial |
| deactivated | erin.ghost@example.test | `Compass-SuperAdmin-dev` | **DENIED** — "Inactive Person Record" | D-08d: person row exists but is deactivated; deactivation beats any group-derived role |
| unmapped | hank.deliver@example.test | `Some-Unmapped-Group` | baseline EDJEr only | D-08c: groups match no mapping |

## Which local auth path? (decision guide)

Three ways to sign in locally — pick deliberately:

| Path | When to use | How | Real SAML? |
|------|-------------|-----|------------|
| **stub-login** | Fastest inner loop; scripted flows; what the **non-SAML** E2E suite uses | `GET /auth/stub-login?email=<you>@example.test&groups=<Group>` (Development-only) | No — bypasses the handshake |
| **mock Google SAML** | **Default in `make dev-all`**; exercise the real handshake daily; local repro of SAML behavior | Just `make dev-all`, then sign in at the mock login form (roster above) | **Yes** — full Sustainsys handshake against the mock |
| **Option A (real Google)** | Only when verifying against the **actual** Google SAML app (attribute mapping changes, cert rotation) | An HTTPS/mkcert ceremony and the real Google Workspace admin console setup — both out of scope for this trimmed workshop copy | Yes — against real Google |

DevBypass (zero-friction auto-auth) remains available as the deepest escape hatch but is turned
**off** in `dev-all` (`Auth__DevBypass__Enabled=false`) so the handshake is the real default.

## `appsettings.Local.json` interplay (Pitfall 4)

`appsettings.Local.json` is loaded **last** in the config pipeline, so it **OUTRANKS the env vars**
`make dev-all` sets. If you previously configured **Option A** (real Google) you have a
`api/appsettings.Local.json` pointing at `accounts.google.com` — with it
present, `dev-all` silently redirects to **real Google** instead of the mock, and the handshake
never reaches `:8090`. `make dev-all` prints a loud multi-line **WARNING** when the file exists;
**rename or delete it** to use the mock (or keep it deliberately to stay on Option A).

## Security — the mock's metadata is radioactive

**The image's IdP signing key is PUBLIC** (committed in the `kenchan0130/simplesamlphp` repo).
Anything that trusts this metadata trusts a keypair the whole internet has. Therefore:

- The mock's metadata must **NEVER be trusted by any deployed environment.** It is CI/localhost
  only.
- The SAML on-switch is **env-var-only** — delivered exclusively via `make dev-all SAML=1`.
  **Nothing** is added to tracked `appsettings`, Helm, or Terraform. Deployed environments receive
  their IdP metadata from Secrets-delivered `MetadataXml`, never from this mock.
- **Never copy real employee emails into the roster.** Passwords are throwaway and every email must
  stay a synthetic `@example.test` seed identity.

## Commands

```bash
make dev-all SAML=1                                 # boot the full stack with the mock IdP + real handshake wired
docker compose up -d --wait google-saml-mock        # boot just the mock (waits on its metadata healthcheck)
curl -sf http://localhost:8090/simplesaml/saml2/idp/metadata.php   # verify metadata serves
```

Without `SAML=1`, `dev-all` defaults to DevBypass (auto-authenticated, no sign-in step) and never
boots the mock container at all.

## Known limitation — interactive human sign-in over plain http fails at ACS

`SAML=1` boots the mock IdP and exercises the API's real Sustainsys path — the assertion is signed
and role mapping runs for real — but **a human browsing the plain-http stack interactively will hit
an ACS 500** (`UnexpectedInResponseToException` — "No cookie preserving state from the request was
found").

**Why:** the SP-initiated challenge makes Sustainsys mint a correlation cookie
(`Saml2.<relayState>`, holding encrypted request state that ACS matches against the assertion's
`InResponseTo`) marked `SameSite=None`. Over plain http that cookie has no `Secure` flag, and
**every modern browser refuses to store a `SameSite=None` cookie without `Secure`** — so the cookie
is dropped, ACS finds no state, and the handshake 500s.

**Unlike the deleted Timesheet SPA, `web/compass/vite.config.ts` carries no mkcert/HTTPS wiring
today**, so there is no local fix for the browser 500 in this trimmed copy. `SAML=1` is still useful
for confirming the mock IdP serves metadata and that the API resolves it — just not for a full
interactive browser sign-in. Use **stub-login** (`GET /auth/stub-login?email=<you>@example.test`,
Development-only) or the DevBypass default for interactive local sign-in instead.
