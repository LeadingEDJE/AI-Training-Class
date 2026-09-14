# Test Strategy

**Which layer a test belongs in, and how to tell.** Written 2026-08-27 after a measurement of the
suite found four responsibilities had drifted upward into Playwright. This document is the contract
those consolidations cite; a removal that cannot name a rule below is either missing a rule or is
the wrong removal.

> This document was written against a larger monorepo that also contained a Timesheet module and
> its own Playwright tree; this trimmed workshop copy carries only Compass and the counts below
> are no longer current for it. The **rules themselves are unchanged and still apply** — a rule
> that names a Timesheet-only file is illustrative history, not something to go looking for here.

---

## The shape, measured

Counted 2026-08-27 against the original, untrimmed monorepo (`main` @ `cbd8b8e`):

| Layer | Cases | What runs it |
|---|---:|---|
| Unit | 5,487 | xUnit (2,823) + timesheet Vitest (1,549) + Compass Vitest (1,115) |
| Integration | 722 | xUnit + Testcontainers against real PostgreSQL |
| End-to-end | 391 | Playwright, 77 spec files, timesheet (290) + Compass (101) |

**14 : 2 : 1 is a healthy pyramid and is not the problem.** Do not treat the E2E count as a budget
to defend. The rule below is about *what a test proves*, not how many there are — a suite can hold
the right ratio and still ask the wrong layer to do the work, which is exactly what the 2026-08-27
measurement found.

---

## The disqualifying question

Before writing or keeping a test at any layer, ask:

> **Can the layer below prove this?**

If yes, it belongs there. A test at layer N is justified only by something layer N−1 structurally
cannot observe. "It's more realistic up here" is not a justification — every test is more realistic
one layer up, and that argument terminates with everything in a browser.

This is Fowler's
[practical test pyramid](https://martinfowler.com/articles/practical-test-pyramid.html) applied
literally: each layer earns its place by covering what the one beneath it cannot.

---

## What each layer can uniquely prove

**Unit — xUnit and Vitest.** Behaviour of a function, service, hook or component in isolation.
Every branch, every edge case, every error path. This is where volume belongs: it is the only layer
where exhaustive is affordable.

*Cannot prove:* that a query translates to SQL, that DI is wired, that two modules agree.

**Integration — xUnit + Testcontainers, real PostgreSQL.** The HTTP contract, end to end through
the real stack minus the browser: routing, authorization, model binding, EF Core translation,
persistence, transaction boundaries.

*Uniquely proves query translation.* The EF Core InMemory provider translates nothing — it
evaluates the expression tree in .NET, so a repository read that Npgsql cannot translate passes
every unit test and returns HTTP 500 in every real environment. This has shipped. Every
repository read needs at least one real-PostgreSQL test whose job is simply *"it translates."*

*Cannot prove:* that anything rendered, that a form is wired to the endpoint it claims.

**End-to-end — Playwright.** That the assembled application works in a real browser: the SPA
reaches the API across the real origin, the session cookie travels, a form submission round-trips
to the grid, and the rendered result is accessible.

*Cannot prove anything the layers below prove more cheaply.* That is the whole constraint.

---

## The five rulings

These resolve the specific drift found in the 2026-08-27 measurement. Cite them by number.

### Rule 1 — Authorization status codes are integration tests, never E2E

A test asserting the **server** returns 401/403 belongs in `tests/integration/`.
`CompassAuthorizationTests.cs` is the worked example: it discovers the whole endpoint graph,
substitutes every route parameter, and exercises every write. That is strictly stronger than any
per-screen browser assertion — it covers routes no screen exposes and cannot go stale when someone
adds an endpoint.

**What stays in E2E:** that the UI *hides* or *disables* the control for that role. That is a
rendering fact, and only a browser can see it.

Denial assertions are also the suite's most reliable flake source: sign-in runs
`UserRoleService.SyncFromProfileAsync`, which deletes `user_roles` rows absent
from the incoming profile. A denial outcome is the only kind that depends on role state at the
instant of the request, so it is the only kind that breaks under parallel workers. Moving these down
removes redundancy and instability together.

**Corollary — never write a denial assertion as "not 200".** Assert the exact status. Under a stale
DevBypass API a loose assertion passes and the gate silently disappears.

### Rule 2 — An accessibility sweep lives in the owning module's E2E tree, once

One sweep per module, in that module's own Playwright project, against the origin that module
actually serves from. Not two sweeps in two trees, and not per-screen scans scattered across
feature specs.

Compass's is `web/compass/tests/e2e/wcag-sweep.critical.spec.ts` — a parameterised screens table
whose per-screen time budget is *derived from* `screens.length`, so adding a screen grows the
allowance instead of silently pushing the test past its timeout. Add screens as rows in that table.

**The exception, and it is narrow:** a scan of an **interaction state** the sweep cannot reach —
an opened disclosure panel, a modal, a populated error state — belongs with the spec that produces
that state. `nav-disclosure.critical.spec.ts` is the legitimate case.

**A per-screen spec may still own its own scan, but it must CALL the shared helper, never transcribe
it.** Four Compass specs — `sales-dashboard`, `reports-availability`, `reports-assignment-duration`
and `reports-assignment-start` — each hand-rolled `axe.run` with `exclude: [['.overflow-x-auto']]`
inline. Feature 011 deleted that exclusion from `helpers/axe.ts` because it was suppressing a
`serious` `scrollable-region-focusable` finding, and `axe-no-exclusions.test.ts` was written to keep
it deleted — but that gate imports the shared constant, so it could not see a copy that never
imported it. **The exclusion survived in four specs, including on `reports-availability`, the screen
the helper's own docstring names as carrying three `serious` nodes without it.** The gate read green
throughout.

This is the same failure as the 28-file comment header, in executable form: *a second copy decays
independently, and a gate that checks the original cannot see the copy.* `axe-no-exclusions.test.ts`
now scans the spec sources as well as the constant.

**The general rule it yields:** when a policy gate reads a constant, it is guarding the constant —
not the policy. Ask what an inline copy would look like, and whether anything would catch one.

### Rule 3 — Render assertions are component tests

"The heading is visible", "the table has these columns", "the empty state shows this text" — Vitest,
with Testing Library. These cost milliseconds there and seconds in a browser, and a browser adds
nothing: no API, no cross-origin behaviour, no real layout dependency is being exercised.

A render assertion earns a browser only when it depends on something jsdom has no engine for —
computed colour contrast, real layout overflow, actual scroll geometry. Those cases are real
(see `helpers/scroll-region.ts`) and they are rare.

### Rule 4 — A browser test proves wiring, not behaviour

Per screen, the E2E-unique fact is: the form submits to the API, and the response updates what is on
screen. **One journey.** Create-and-see-it is enough to prove the wiring exists; edit, deactivate,
validation messages and error paths are covered by the integration test that owns the endpoint and
the component test that owns the form.

A second and third CRUD round-trip in the same spec re-proves the same wiring against a different
verb. That is not additional coverage.

### Rule 5 — A claim about CI belongs in CI, not in the source it gates

A comment or a test title cannot observe the workflow that runs it, so it cannot stay correct when
that workflow changes. It has no mechanism keeping it honest, and it decays silently while reading
as authoritative.

The measured case: thirty-three test files stated, in a header comment or in a `test.describe` title
that printed into every CI log, that they *"run in all 3 browsers (Chromium, Firefox, WebKit)"*.
Since issue #416 that is false on a pull request — `enginesFor()` returns chromium alone for
`pull_request`. Nobody updated the copies because nothing could.

**So:** state the engine set, the shard layout and the cadence in `.github/workflows/ci.yml` and
`detect-modules.cjs`, which are the only places that can be true. The one thing worth writing down
about an engine is a *requirement* — "this test exists for a WebKit-only defect and must not be
skipped there", which is why `web/compass/tests/e2e/nav-keyboard.critical.spec.ts` exists.
**Not** in the spec: a rule file is maintained, and a spec header is not.

Enforced by [`scripts/check-stale-engine-claims.sh`](../scripts/check-stale-engine-claims.sh), wired
into the unconditional `Repo-wide Gates` job. Its fail-open mode is the **inverse** of the coverage
checks in "Before deleting a test" below: an absence check goes vacuous when it searches the wrong
corpus, not when it loops over an empty list. So it asserts the number of files it scanned against a
floor before running a single pattern.

**What the guard does not cover, and no longer has to.** Twenty-one spec headers opened with a line
like `// CRITICAL: runs under the compass-chromium-critical project.` — a restatement of `testMatch`
and `playwright.config.ts` that the filename already carries. They were *true*, so they were never the
misinformation this guard exists to stop, but they had the same weakness: nothing kept them correct if
a project were renamed. **The judged comment pass removed all twenty-one**, so the guard's patterns
being worded not to fire on them is now belt and braces rather than a deferral. Do not reintroduce
one: the project a spec runs under is decided by `testMatch` and readable from the filename.

(This paragraph previously said twenty-two. The measured count was twenty-one at the commit that wrote
it and twenty-one when they were removed — a reminder that a number in prose is a claim like any
other.)

---

## Before deleting a test

Three checks, in order. The second one is the one people skip.

1. **Name the replacement.** State the specific test at the lower layer that already covers what is
   being removed. If it does not exist, write it first — that is a separate, prior commit.

2. **Prove the replacement actually runs, and assert the count.** Print how many items the coverage
   check found and assert it is non-zero *before* iterating.

   > **A verification loop over zero items PASSES.** A `for f in $(extract ...)` that extracts
   > nothing prints "all OK" and exits 0. A path regex matching no files reports success. This repo
   > has lost **eight** gates to exactly this shape, including one
   > inside a fix for this bug class. Write `[ "$n" -gt 0 ]` before the loop, not after.

3. **Run the engines a pull request does not.** Firefox and WebKit gate nothing on a PR —
   `enginesFor()` returns chromium alone for `pull_request`, and the other two run only post-merge
   or on the Tuesday schedule. A consolidation that breaks WebKit merges clean and surfaces a week
   later, looking exactly like the known concurrency flakiness. Dispatch CI on the
   branch with the full engine set.

---

## What is deliberately exempt

Not everything expensive is misplaced. These stay where they are:

| Suite | Why it is legitimately E2E |
|---|---|
| Scroll/overflow geometry (`web/compass/tests/e2e/scroll-region-helper.critical.spec.ts`) | jsdom has no layout engine; the measurement is the point. |

> The original monorepo also exempted a SAML-handshake suite and a shell suite (each in its own
> `tests/e2e/` tree). Neither the Timesheet SAML tests nor the shell's own test tree survived the
> trim to this Compass-only copy, so they are omitted here rather than left as dangling paths.

---

## Why this document exists

Every boundary above had eroded by 2026-08-27, and none of it was anybody's mistake: nothing was
written down to erode *against*. `docs/` held no test-strategy document, so each individual decision
to add one more browser assertion was locally reasonable and the aggregate was not.

The measurement that produced these rules found eight browser tests re-proving an authorization
sweep that already covered more than they did, two WCAG sweeps over the same screens in different
trees, fifteen admin specs duplicating their integration counterparts one-for-one, and a comment
copied into 28 files that had become false. Keeping this document current is cheaper than
re-discovering that.
