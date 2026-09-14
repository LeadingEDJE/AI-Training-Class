import { defineConfig, devices } from '@playwright/test';

// Compass's own dedicated Playwright config (T006). Until this existed, Compass's critical E2E
// specs lived under web/timesheet/tests/e2e/compass*.critical.spec.ts and ran through the
// timesheet SPA's Vite proxy on :5173 -- see T010's CI wiring, which still targets that tree.
// This config is the first step off that borrowed setup: it boots the API directly against
// Compass's OWN dev server on :5176 (which already proxies /api -> the API per
// web/compass/vite.config.ts), so no timesheet or ooto dev server is needed here.
//
// Session cookies set by a direct stub-login call to API_BASE (localhost:5009) are still sent on
// requests to localhost:5176: cookie matching in the browser ignores port and matches on host
// alone, so the existing web/timesheet/tests/e2e/helpers/auth-session.ts helpers work unchanged
// against this origin if a future spec here needs an authenticated session.
const baseURL = process.env.BASE_URL ?? 'http://localhost:5176';

export default defineConfig({
  testDir: './tests/e2e',
  outputDir: './tests/e2e/test-results',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: 'html',

  use: {
    baseURL,
    trace: 'on',
    screenshot: 'only-on-failure',
  },

  projects: [
    {
      name: 'compass-chromium-critical',
      use: { ...devices['Desktop Chrome'] },
      testMatch: /.*\.critical\.spec\.ts/,
    },

    /*
     * Cross-engine coverage for AC-NFR-7 (issue #83, T044).
     *
     * AC-NFR-7 names Chrome, Edge, Firefox and Safari. Until this existed, Compass's OWN e2e tree ran
     * on Chromium and nothing else — not on a pull request, not on a merge, not on the Tuesday
     * schedule. That is a narrower gap than it looks and worth stating precisely: the timesheet tree
     * DOES have `firefox-critical` and `webkit-critical` projects, deliberately demoted to
     * merge-and-schedule after measurement (601s and 554s, `.github/workflows/ci.yml:8-21`, "a
     * DEMOTION, NOT A DELETION"). This config simply had no such projects to demote.
     *
     * **Edge needs no project.** Edge is Chromium-based and `compass-chromium-critical` discharges it
     * (owner decision Q1, 2026-08-21; recorded in `docs/nfr/NFR-catalog.md` P4). A `channel: 'msedge'`
     * project would need Edge installed on the runner and would re-prove the same engine.
     *
     * **These run on the same cadence as the timesheet tree's, not on every pull request** — see the
     * `e2e-compass-dedicated` job, which selects projects from `enginesFor(eventName)` so there is one
     * cadence policy rather than two.
     */
    {
      name: 'compass-firefox-critical',
      use: { ...devices['Desktop Firefox'] },
      testMatch: /.*\.critical\.spec\.ts/,
    },
    {
      name: 'compass-webkit-critical',
      use: { ...devices['Desktop Safari'] },
      testMatch: /.*\.critical\.spec\.ts/,
    },

    /*
     * Emulated mobile devices for AC-NFR-7's "iPhone and Android" clause (issue #83, T045).
     *
     * **What these add that a resized desktop engine does not.** Until now every "mobile" assertion in
     * this repository was a desktop browser narrowed to 390px — no mobile user-agent, no touch, no
     * `isMobile`. These descriptors carry all three, so a hover-only affordance or a UA-gated code path
     * is finally observable. `isMobile` maps mouse input to touch, which is exactly why it can surface
     * failures a width change cannot.
     *
     * **Two axes, and neither substitutes for the other.** The five NFR-catalog viewports are the
     * responsive standard and the stranding sweep drives them explicitly, overriding whatever viewport
     * a project starts with. These devices contribute the UA/touch axis across those widths, PLUS one
     * width the standard does not contain at all: the Galaxy S24 is **360px**, narrower than the
     * catalog's 390px floor and a very common real Android width. `no Compass screen strands content at
     * the project's own viewport` is the test that exercises it.
     *
     * **iPhone 14 is WebKit and Galaxy S24 is Chromium** — the engines those devices actually ship, so
     * this is not merely two more Chromium runs wearing different user-agent strings. Note Firefox does
     * not support `isMobile`, which is why there is no third device here.
     *
     * Emulation is not a real device, and this feature does not pretend otherwise: Q2 pairs these
     * standing projects with a one-time manual pass on real hardware (T052).
     */
    {
      name: 'compass-iphone-critical',
      use: { ...devices['iPhone 14'] },
      testMatch: /.*\.critical\.spec\.ts/,
    },
    {
      name: 'compass-android-critical',
      use: { ...devices['Galaxy S24'] },
      testMatch: /.*\.critical\.spec\.ts/,
    },
  ],

  webServer: [
    {
      // Start dependencies (Postgres) with a clean DB, then launch the API. cwd is TWO levels up
      // from web/compass/ to the repo root, mirroring web/timesheet/playwright.config.ts.
      //
      // Auth__DevBypass__Enabled=false and GoogleAuth__AllowedDomain=example.test match the
      // timesheet config's E2E stack: DevBypass off so unauthenticated requests are observable,
      // and the synthetic @example.test identities DevelopmentSeeder ships pass domain validation.
      //
      // These are set via `env`, not inlined in `command` -- an inline `KEY=value` prefix only
      // parses under a POSIX shell; cmd.exe (Windows) treats it as an unknown command and the
      // server never starts.
      command: [
        'make e2e-stack-up',
        '&&',
        'dotnet run --project api --urls http://localhost:5009',
      ].join(' '),
      cwd: '../..',
      url: 'http://localhost:5009/health',
      reuseExistingServer: true,
      timeout: 300_000, // Full stack startup (docker + migrations + seed) can be slow
      env: {
        POSTGRES_HOST: process.env.POSTGRES_HOST ?? 'localhost',
        POSTGRES_PASSWORD: 'localdevwhocares',
        ASPNETCORE_ENVIRONMENT: 'Development',
        Auth__DevBypass__Enabled: 'false',
        GoogleAuth__AllowedDomain: 'example.test',
      },
    },
    {
      // Compass's own vite dev server. Unlike ooto, Compass is a root npm workspace member, so
      // --workspace=@leap/compass resolves directly -- no nested install to cd into.
      // --strictPort makes a port clash fail loudly instead of silently binding elsewhere.
      command: 'npm run dev --workspace=@leap/compass -- --port 5176 --strictPort',
      cwd: '../..',
      url: 'http://localhost:5176/compass/',
      reuseExistingServer: true,
      // 30s measured flaky under load on a resource-constrained sandbox: a cold npm/vite start
      // racing the API server's own startup for CPU/disk intermittently missed it, failing the
      // whole run on a false timeout rather than a real startup problem.
      timeout: 60_000,
    },
  ],
});
