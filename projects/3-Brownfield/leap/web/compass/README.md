# Compass SPA (`web/compass`)

The EDJE Compass front end, mounted at `/compass/`. It began as a walking skeleton proving one round
trip through the module boundary; since feature 004 it carries real configuration screens, and since
feature 005 the read surfaces below. **That skeleton page is retired** (issue #248, then feature 005's
T103): `/compass/` renders the Team Directory, and `src/App.tsx` and `src/lib/employee-id.ts` — which
read an `employeeId` out of the query string — are deleted. The endpoint it exercised,
`/api/compass/v1/employees/{id}`, is untouched and still serves its real audience (ADR-008).

## What is here now

| Screen                                                          | Path                                                          | Story         |
| --------------------------------------------------------------- | ------------------------------------------------------------- | ------------- |
| Team Directory                                                  | `/compass/`, `/compass/team-directory`                        | 005 US1 (#70) |
| Employee detail (read-only)                                     | `/compass/team-directory/$employeeId`                         | 005 US2 (#71) |
| Client Directory                                                | `/compass/client-directory`                                   | 005 US4 (#73) |
| Client view                                                     | `/compass/client-directory/$clientId`                         | 005 US5 (#74) |
| Administration shell                                            | `/compass/admin`                                              | 004 US1       |
| Lookup administration — employee types, invoice frequency types | `/compass/admin/lookups`                                      | 004 US1 (#58) |
| EDJEr list                                                      | `/compass/admin/edjers`                                       | 004 US2 (#59) |
| EDJEr add / edit                                                | `/compass/admin/edjers/new`, `/compass/admin/edjers/$edjerId` | 004 US2 (#59) |

Client configuration is feature **004** US3 and not built yet (not to be confused with feature 005's
US3, which is the cross-cutting inactive-EDJEr visibility rule rather than a screen). The `compass`
database schema holds **7 tables** since
PR #181 (2026-08-06) — `employee`, `client`, `client_assignment`, `sow`, `employee_type`,
`invoice_frequency_type`, `billable_time_category`.

**This file used to say "there is no Compass domain model here, no routing, no feature" and to treat
every file as replaceable.** That was true of the skeleton and is not true now: `specs/004-compass-reference-data`
is the requirements source, and the screens above are implemented against acceptance criteria with
tests. The sentence is recorded rather than deleted because a reader who saw the old one needs to know
it was superseded, not that they misremembered.

## Design sources, and the order they rank in

Three documents inform these screens and they disagree, so the precedence is fixed:

1. **`docs/design/edje-compass-mockups.html`** — layout, labels, field order, section grouping, hint
   text. Its own banner says it is "indicative input, not specification" and that where a mockup and an
   acceptance criterion disagree, **the criterion wins**. It does, in two recorded places on the EDJEr
   form (the coach is optional, and annotation chips are scaffolding). A third used to be listed —
   "there is no assignment-history card" — and is gone: the card shipped with `#223`, and `#448` added
   the mockup's own `+ Assign to Client` action beside its `View SOWs` column.
2. **`docs/design/leading-edje-style-guide.html`** — colour roles, the tint/shade ramp, typography.
   Note it is a _marketing-website_ restyle kit; take its families, weights and roles, not its
   website-scale type sizes, and expect it to specify nothing for error states, required markers,
   toggles or tables.
3. **Measured contrast overrides both** — constitution Principle IX, codified in
   `src/styles/brand-tokens.ts` and enforced by `tests/unit/brand-contrast.test.ts`. Two mockup
   pairings and one style-guide token fail AA and are rejected by measurement. **Do not copy a pairing
   from either document without measuring it.**

`src/components/ui.tsx` is where that reconciliation lives, one primitive per element, so the next
screen does not re-decide it. Its class strings are in `ui-classes.ts` — a separate file because
`react-refresh/only-export-components` requires a component module to export components only.

### Recorded departure: the mobile nav is a disclosure, and the mockup does not say so

Below `md` (768px) `CompassNav` collapses its links behind a **Menu** button. The design source is
**silent** on mobile navigation — `edje-compass-mockups.html`'s only media query is
`@media(max-width:900px)` and it touches `.tiles`, `.apps`, `.formgrid` and `.two-col`, never `.topbar`
or `.nav`. Its banner grants "design liberty ... on layout and interaction", so this is a permitted
choice that has to be **written down** rather than a correction of something specified (Principle X
rule 3). What it replaces: `flex-wrap` at 390px put five links on three lines and pushed page content
down by roughly a third of the viewport.

**768, not the mockup's 900.** 768 is the project's one layout breakpoint (`FormGrid`'s docstring
records the reasoning); 900 is not a token and adopting it would give Compass a second breakpoint. The
switch goes through `useIsNarrowViewport`, so exactly ONE branch is in the DOM — which is why all 25
pre-existing `CompassNav` tests are untouched (and why the 1280x800 screenshot baselines were too,
before issue #574 deleted them on 2026-09-08), and why jsdom
(no `matchMedia`) still sees the desktop bar by default.

Three decisions in `CompassNav`'s docstring that are settled, not open: the wordmark and userchip stay
in the bar; **zero permitted links renders no button** (a control opening an empty panel advertises
navigation that does not exist); and the panel is not a focus trap, but Escape closes it and returns
focus to the button.

**Writing an e2e spec that navigates via the nav?** Open the menu conditionally —
`compass-team-directory.critical.spec.ts`'s "reaches the directory from the navigation shell" is the
shape to copy. A spec written at desktop passes `compass-chromium-critical` on every PR and fails only
under the 390px and 360px device projects, which run on merge-to-main and the Tuesday schedule.

### The card set below `md`, and why `EmployeeDetailPage` is in it

Seven screens stack their wide tables to label-value cards below 768px, via `StackedRows`. Six are there
because they **stranded** — content off the horizontal axis with no keyboard path to it (issue #83).

**`EmployeeDetailPage`'s assignment history is the seventh, and it never stranded.** It is in the set by
owner decision (2026-08-25), for consistency with its deliberate twin, the client record's `CC-3` history
(PR #223; spec 010 records the two as parallel). One twin as cards and the other as a table is a visible
inconsistency, and that was judged worse than including a screen no measurement condemned.

Worth knowing if you touch it: its first cell is **rich** — a client link, a second server-gated
"View assignment" link, an optional note, and a nested SOW list. `cellClassNames` carries `align-top` on
all three cells for that reason; without it the two dates centre against a tall neighbour instead of
lining up with the client name.

`tests/e2e/responsive-stranding.critical.spec.ts`'s `CARD_STACK_SCREENS` is the list. It deliberately
excluded `employee-detail` while the decision was open, so that folding it in could not happen quietly —
adding it there is part of making the choice, not bookkeeping after it.

### A link in a table is `tableLinkClass`, and nothing else

`ui-classes.ts` holds the one definition of a link's appearance. Use it for every link in a table cell,
and for a `<button>` that acts rather than navigates but reads as a link (the lookup screen's and the
SOW list's `Edit`). Compose only what belongs to the **cell** (`whitespace-nowrap`, `text-sm`)
alongside it — the shared value says nothing about weight, wrapping or size.

Eight screens had each written their own run before this existed, so
`tests/unit/table-link-consistency.test.ts` fails on **any** bare `underline` utility under `src/`
outside `ui-classes.ts`, with no allowlist.

## It IS a root workspace member — unlike `web/ooto`

`web/compass` is listed in the root `package.json` `workspaces` array, so a plain `npm ci` at the
repository root installs it and the root binaries (`eslint`, `prettier`, `vitest`) resolve normally.

`web/ooto` is **deliberately not** a workspace member: it is on React 18, which cannot coexist with
React 19 under npm hoisting, so it needs its own isolated nested install (`cd web/ooto && npm ci`).
Compass is React 19, so it joins the workspace normally. **Do not copy ooto's isolated-install shape
here** — that shape is a workaround for a version conflict Compass does not have.

### The query-library single-instance rule

`@tanstack/react-query` must resolve to exactly ONE hoisted copy across the workspace. Two copies mean
the shared `@leap/api-client` hooks cannot see this app's `QueryClientProvider`, and every page using
them crashes with "No QueryClient set".

**Unit tests still pass in that broken state — only the end-to-end suite catches it.** After any
react-query version bump anywhere in the workspace:

```bash
npm dedupe
npm ls @tanstack/react-query   # must report ONE version, everything else "deduped"
```

Keep this app's react-query range identical to `web/timesheet`'s. A divergent range is exactly how a
second copy appears.

## Toolchain

React 19 + TypeScript (strict) + Vite + Tailwind v4 via the Vite plugin — **no PostCSS and no
`tailwind.config.js`**, matching the timesheet SPA.

Compass adopts the timesheet SPA's Tailwind type tokens **by convention**, not through a shared UI
package. Extracting an `@leap/ui` package would need the rule-of-three threshold met; with two
consumers it is not, and inventing one now would be speculative design.

`base: '/compass/'` — the SPA is a path-mounted LEAP module. This value must stay in step with the
nginx mount path and the Playwright proxy; a mismatch produces a blank page with 404s on every asset.

There **is** a router — TanStack Router with **code-based** routes, in `src/routes/router.ts`.

The code-based form is the point. File-based routing emits a generated `routeTree.gen.ts`, which would
need a coverage exclusion, and this workspace holds the 98% tier with zero of them (see below). Every
route below is an ordinary TypeScript module that counts toward the tier like any other file.
**Do not introduce `@tanstack/router-plugin`.**

Until feature 004 this section said there was no router, _because_ of that generated file. Code-based
routes resolved the objection rather than overriding it, so the reason survived and the conclusion
changed.

`basepath` must stay in step with the Vite `base`, the nginx `location /compass/` mount and the
Playwright proxy — a mismatch produces a blank page with 404s on every asset.

One split to keep straight: `CompassNav` uses plain `<a href>` with `/compass/`-prefixed paths (it
predates the router and does full page loads), while `AdminLayout` uses `<Link to>` with **unprefixed**
paths, because the router's `basepath` supplies the prefix. Mixing them yields
`/compass/compass/admin/…`.

## Coverage: 98% tier, ZERO exclusions

The vitest config sets all four thresholds (lines, functions, branches, statements) to **98** from day
one, and its `exclude` list contains only the generic entries (`node_modules/`, type declarations,
`tests/`, config files). There is no project-specific exclusion and there must not be one.

If a file cannot reach the tier, **write the test**. Do not add an exclusion and do not lower a
threshold — the repository's exclusion guard blocks growth of the exclusion set anyway, and the
standing rule is that a guardrail causing friction gets fixed, never removed.

The `json-summary` reporter is **required**. Its absence is what silently reported a frontend line
rate of zero for the timesheet SPA on every PR until it was found by audit. A coverage gate must prove
it executed.

## Commands

```bash
npm run dev            # dev server on :5176, proxying /api to the local API on :5009
npm run build          # tsc -b && vite build
npm run lint           # eslint + prettier --check
npm run test           # vitest run
npm run test:coverage  # vitest run --coverage (enforces the 98% thresholds)
```

The LEAP shell (`web/shell/`) is a static, build-free launcher with no dev server of its own; it
is only exercised through the nginx production image, not via `npm run dev` here or elsewhere.

## API calls

**Never use a bare `fetch()` for an API call.** Use `apiFetch` + `apiUrl` from `src/lib/api-url.ts`,
which sends the server-managed HttpOnly session cookie via `credentials: 'include'` and surfaces a 401
as a `session-expired` event. A bare fetch drops the cookie in any cross-origin deployed environment
and skips re-login handling.

For endpoints in the OpenAPI schema, `$api` from `@leap/api-client` is also available — it is a single
whole-API client, so Compass routes appear in the same generated `schema.d.ts` as everything else.
There is no separate Compass client.

## Accessibility and responsive baseline (US7 / #57)

Established as a Phase 0 foundation, before the first real screen (#54) — see plan.md's Phase 7
sequencing rationale: both are substantially more expensive to retrofit once nine screens exist.
The baseline is documented conventions **plus automated checks that provably fail on a
violation**, enforced by `npm run test:coverage` (the same command CI already runs, so no
separate pipeline wiring was needed):

- `tests/unit/brand-contrast.test.ts` — pure WCAG contrast-ratio math against the five brand
  hex values, no rendering involved.
- `tests/unit/accessibility.test.tsx` (backed by `src/lib/accessibility-check.ts`) — axe-core's
  structural ruleset (roles, labels, landmarks) plus a dedicated inline-colour contrast walk,
  run against every screen that exists (`App` + `CompassNav`).
- `tests/unit/responsive.test.tsx` (backed by `src/lib/responsive-check.ts`) — no element wider
  than the viewport at 390×844, 767×1024, 768×1024, 1280×800 and 1920×1080.

**Real-browser confirmation, closing the jsdom gap above (2026-08-10).** The unit checks above
disable axe-core's own `color-contrast` rule because jsdom has no layout/paint engine and cannot
reliably evaluate it — and the hand-rolled inline-style walk that stands in for it can't see
contrast driven by compiled Tailwind classes, which is how every real colour in this app is
actually applied. `web/timesheet/tests/e2e/compass-accessibility.critical.spec.ts` closes that gap
permanently: it runs the real axe-core engine (`color-contrast` enabled) via `@axe-core/playwright`
against the live, fully-styled page at `localhost:5173/compass/`, scoped strictly to
`wcag2a`/`wcag2aa`/`wcag21a`/`wcag21aa` tags, at both desktop and the 390px mobile width — **zero
violations**, confirmed by two independent passes (a manual `pwcli` spot-check across all 5 role
states, then this permanent CI-enforced spec). The same file also drives real `Tab` key presses
through the nav and asserts each link receives focus in the correct order with a visible focus
indicator — genuine keyboard-only verification, not a `tabIndex` proxy.

**Still not done, and explicitly out of scope here:** manual assistive-technology testing (a real
screen reader — NVDA/JAWS/VoiceOver — actually announcing the page) and full nine-screen coverage.
Both remain Stream 6's job; this baseline only establishes and enforces the posture for what exists
today, and wires the same checks so a new screen's own test file inherits them automatically
(import `checkAccessibility` / `checkResponsiveOverflow` the same way `accessibility.test.tsx` /
`responsive.test.tsx` do).

### The five brand values and their roles

Defined once in `src/styles/brand-tokens.ts` (the source the tests import) and mirrored as
Tailwind v4 `@theme` custom properties in `src/index.css` (`--color-brand-*`, generating
`bg-brand-*` / `border-brand-*` / `fill-brand-*` utilities):

| Value | Hex       | Contrast on white  | Role                                                 |
| ----- | --------- | ------------------ | ---------------------------------------------------- |
| Green | `#98C93D` | 1.95:1 — fails AA  | fill / border / accent only                          |
| Cyan  | `#49C6E5` | 2.0:1 — fails AA   | fill / border / accent only                          |
| Blue  | `#5C95FF` | 2.92:1 — fails AA  | fill / border / accent only                          |
| Taupe | `#C4B1AE` | fails AA           | fill / border / accent only                          |
| Gray  | `#4C4D4F` | 8.46:1 — passes AA | **default body text** (set on `body` in `index.css`) |

**Never use green, cyan, blue or taupe as body text on a light surface.** Following the brand
palette literally for text produces a WCAG failure that looks correct at a glance — this is why
`brand-contrast.test.ts` asserts the rejection rather than only documenting it. Large text (≥24px,
or ≥19px bold) only needs 3:1, which none of the four accents clear either, so the same rule
applies there. Before using a colour pairing anywhere, verify the **actual rendered** pairing
against `contrastRatio` in `src/lib/contrast.ts` rather than copying a value out of
`docs/design/edje-compass-mockups.html` — the mockups' pale-green pill (`#5c7c1e` on `#eef6dc`)
measures 4.33:1, under the normal-text threshold, and passes only as large text.

### The viewport set

390×844, 767×1024, 768×1024, 1280×800, 1920×1080 — the same five breakpoints used across every
LEAP module's Playwright/visual verification. No horizontal page-body overflow at any of them.

### The `apiFetch`/`apiUrl` requirement, restated

Covered in full under [API calls](#api-calls) above, and enforced automatically by
`tests/unit/no-bare-fetch.test.ts` — it scans `src/` for a bare `fetch(` outside `lib/api-url.ts`
itself (where `apiFetch` is defined) and fails the suite if it finds one.

## Cross-browser notes that are NOT defects (issue #83)

Two engine differences that look like bugs, verified as not being any. Both were found while adding
the Firefox, WebKit, iPhone and Android Playwright projects for AC-NFR-7, and both are written down
here because the natural next step on meeting either is to open a bug.

### WebKit paints today's date into an empty `<input type="date">`

On Safari and WebKit an untouched date input renders its placeholder segments filled with the current
date — `08/20/2026` where Chromium and Firefox show `mm/dd/yyyy`. It reads exactly like a field that
has been pre-populated.

**The DOM disagrees, and the DOM is what matters.** Measured on an untouched `Add EDJEr` form in
WebKit: `input.value === ''`, `input.validity.valid === true`. Nothing is submitted, no default is
stored, and a `required` date field still blocks submission. It is a rendering convention for the
placeholder, not a value.

So: no fix, no workaround, and do not add a test asserting the placeholder text — it would assert a
browser's rendering choice and fail the next time WebKit changes it.

### The five NFR viewports do not include 360px

`docs/nfr/NFR-catalog.md` P4 names 390/767/768/1280/1920. Playwright's `Galaxy S24` descriptor is
**360** wide, and `compass-android-critical` therefore measures a width the standard never reaches —
which is how the page-overflow finding on the client configuration screen was caught. If you add a
responsive assertion, remember the device projects are a second axis and not a re-run of the first.

## Roles

Compass has its own four roles (`Compass Super Admin`, `Compass Admin`, `Compass Ops`,
`Compass Sales`) and **inherits nothing** — a timesheet SuperAdmin gets a 403 here. The page renders
an explicit "not authorised" state for that case rather than hiding it.
