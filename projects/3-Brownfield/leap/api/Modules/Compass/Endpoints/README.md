# Compass endpoints

The Compass module's HTTP surface. Routes are **versioned from the start** — `/api/compass/v1/...`.

The OOTO module's routes are unversioned because they inherited a legacy system's shapes and are now
contract-locked by an absorbed SPA. That was a concession, not a convention. Do not copy it, and do
not "harmonise" the OOTO routes with this one — they are frozen.

Endpoint groups follow the house pattern: a static class with a `Map<Resource>Endpoints` extension on
the web application, a `MapGroup` for the route prefix, private static async handlers taking their
services by DI parameter injection, and `IResult` returns.

Handlers reach data through the module boundary (`IDirectory`), never through the database context
directly.

Every endpoint group needs a matching test in one of the two sanctioned tiers
(`tests/integration/Endpoints/**` or `tests/unit/Endpoints/**`) — a commit-time gate enforces this and
exits 2 without one.

## The eight published routes

This directory publishes the Compass Directory boundary's HTTP transport — the versioned,
out-of-monolith-facing surface (`specs/009-compass-integration-boundary/contracts/directory-boundary.md`).
Eight routes across the seven data-kind families, in four files:

| File | Routes |
|---|---|
| `CompassEmployeeEndpoints.cs` | `GET /api/compass/v1/employees/{id:int}`, `GET /api/compass/v1/employees/{id:int}/assignments` |
| `CompassClientEndpoints.cs` | `GET /api/compass/v1/clients/{id:int}`, `GET /api/compass/v1/clients/{id:int}/billable-categories`, `GET /api/compass/v1/clients/{id:int}/assignments` |
| `CompassAssignmentEndpoints.cs` | `GET /api/compass/v1/assignments/{id:int}`, `GET /api/compass/v1/assignments/{id:int}/sows` |
| `CompassInvoiceFrequencyEndpoints.cs` | `GET /api/compass/v1/invoice-frequencies` |

Not published here: `Endpoints/Read/` and `Endpoints/Write/` back the **application** surface the
Compass SPA calls (ADR-008) — a different audience with different change rules. Do not conflate the two;
see each subfolder's own header remarks for the distinction, and `CompassAssignmentEndpoints.cs` under
`Endpoints/Write/` in particular, which shares a class name with this folder's file in a different
namespace.

## Authorization: per-group, not boundary-wide

**Every route group above carries its OWN `.RequireAuthorization(RolePolicy.CompassAdmin)` call.** There
is no shared, boundary-wide gate applied once for all four files — each `Map<Resource>Endpoints` method
declares its own `MapGroup(...).RequireAuthorization(...)`. This is deliberate (FR-017): a future
per-data-kind scope (`#143`) attaches to a group that already exists per family, rather than needing one
carved out of a single monolithic gate later. See
`specs/009-compass-integration-boundary/contracts/boundary-authorization.md` § 2 for the full policy
table and the status-code contract (401 unauthenticated, 403 authenticated-without-role, 404 for a
record that exists but the caller may not see).

## No out-of-monolith consumer until `#143` lands

**Do not onboard an out-of-monolith consumer against these routes.** `#143` — OAuth2 client-credentials,
a consumer registry, scoped/revocable/rotatable tokens, per-consumer rate limits — is deferred whole.
Until it lands, the only sanctioned callers of this surface are same-origin, and no external caller's
access can be revoked or throttled independently of every other caller's.
