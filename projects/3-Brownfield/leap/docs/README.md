# `docs/` — reading order

This is a trimmed, Compass-only workshop copy of a larger internal monorepo (the Timesheet and
OOTO modules, the deployment/CI machinery, and the original repo's operational history were cut).
What remains here is what helps someone **run or understand the Compass module locally**. Docs
about deleted modules, production deployment, or the original repo's cutover history were removed
rather than bannered — a dangling reference is worse than no reference, so if a surviving doc still
points at something that's gone, treat that as a bug in the doc, not a hidden file to go find.

---

## Read in this order

**1. Get it running** — the top-level [`README.md`](../README.md) at the repository root.
Prerequisites, `make setup` / `make dev-all`, and how sign-in works locally (DevBypass by default,
or the real SAML handshake via `SAML=1`).

**2. Who is allowed to do what** — [`auth-roles-and-policies.md`](auth-roles-and-policies.md).
The closed role vocabulary, the Compass authorization policies, and the Google group → role
mapping — including the one direction that fails open.

**3. Sign in with a real SAML handshake** — [`local-mock-google-saml.md`](local-mock-google-saml.md).
The mock Google Workspace IdP this repo runs locally against, its user roster, and the known
limitation around interactive browser sign-in over plain http.

**4. The product requirements** — [`02_prd/finalized/PRD-compass.md`](02_prd/finalized/PRD-compass.md).
The authoritative Compass PRD (acceptance criteria, business rules). It cites a few ADRs and a
target-architecture doc that did not survive the trim to this copy; treat each cited reconciliation
(e.g. "PostgreSQL, not MySQL") as still true even though the ADR file isn't here to open.

**5. Non-functional targets** — [`nfr/NFR-catalog.md`](nfr/NFR-catalog.md).
The testable reliability/security/performance/accessibility targets and how each is verified,
cross-referenced to the PRD's `AC-NFR-*` items.

**6. How the test suite is organized** — [`TEST-STRATEGY.md`](TEST-STRATEGY.md).
Which layer (unit / integration / E2E) a given test belongs in, and why — written against the
original, larger monorepo, but the rules apply unchanged to Compass's own suite.

**7. Add a module, or understand how Compass is put together** —
[`platform/adding-a-module.md`](platform/adding-a-module.md).
An ordered recipe written from what building Compass actually did — the one-context data model,
the module-boundary convention, roles, and the front-end/routing checklists — with the real error
text for every trap. This copy has no `.github/`, `.husky/` or `.claude` directory, so the CI/hook
rows in its gate checklist are historical rather than something to go looking for here.

**8. Design reference** — [`design/edje-compass-mockups.html`](design/edje-compass-mockups.html).
Indicative screen mockups cited in the PRD's grounding. Where a mockup and an acceptance criterion
disagree, the acceptance criterion wins.

---

## What is not here, and why

- **Deleted-module docs** (Timesheet, OOTO, the TPS/MySQL absorption, ETL import tooling) — those
  modules do not exist in this trimmed copy.
- **Deployment and CI/CD docs** (AWS, Helm, GitHub Actions, semantic-release, cutover runbooks,
  backup/restore, migration rehearsal) — this workshop copy is a local-only target; nothing here
  gets deployed, and none of that documentation is present in this checkout.
- **`ARCHITECTURE.md` / `REPOSITORIES.md` / the platform architecture overview** — described the
  whole original platform (a modular monolith with Timesheet + OOTO + Compass as peer modules).
  Genuinely Compass-relevant facts from them (the module layout, the one-context data model, the
  Google-SAML-plus-cookie-session auth topology) live in `platform/adding-a-module.md` and
  `auth-roles-and-policies.md` instead.
- **`docs/legacy/`** — the original pre-rewrite Timesheet requirements set. Not relevant to Compass.
- **`SECURITY-DISMISSALS.md` / `slack-smoke-test-guide.md`** — a GitHub Security-tab dismissal
  mirror and a Slack-notification smoke test guide, both almost entirely about the deleted
  Timesheet module and citing files that no longer exist in this module set.
