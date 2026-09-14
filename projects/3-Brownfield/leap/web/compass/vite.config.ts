import { existsSync, readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import type { Plugin } from 'vite';
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

/**
 * Serves the LEAP shell's `config.js` at the Compass base in DEV ONLY (feature 011, T062, issue #83).
 *
 * **The bug this fixes, and why it is dev-only.** `index.html` references `/config.js` root-absolutely,
 * which is correct: deployed environments serve it from nginx's exact-match `location = /config.js`,
 * synthesized from the Helm `web.env` vars. But Vite's DEV SERVER rewrites a root-absolute script `src`
 * to the app's `base`, so the browser asks for `/compass/config.js` — which nothing serves. Every
 * Compass screen therefore logged a 404 locally.
 *
 * That was never a production defect: `npm run build` leaves the reference unprefixed (verified), so the
 * deployed page still resolves it through nginx. It was noise — and noise on every screen is what hides
 * the next real console error, which is exactly what the audit behind this feature had to rule out
 * across 22 screens before it could trust any console signal at all.
 *
 * **Serving the shell's file rather than adding a second one.** `web/shell/config.js` already exists and
 * already carries the local-dev defaults; a copy under `web/compass/public/` would be a second source of
 * the same values, free to drift, and would also land a never-referenced file in `dist/`.
 *
 * `apply: 'serve'` keeps this out of the build entirely, so the production output is untouched.
 */
function serveShellConfigInDev(): Plugin {
  return {
    name: 'leap-serve-shell-config-at-compass-base',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use('/compass/config.js', (_req, res, next) => {
        const shellConfig = fileURLToPath(new URL('../shell/config.js', import.meta.url));
        if (!existsSync(shellConfig)) {
          // Fall through rather than inventing values: a missing shell config is a real problem and a
          // 404 naming it is more useful than a silently empty script.
          next();
          return;
        }
        res.setHeader('Content-Type', 'application/javascript; charset=utf-8');
        // Never cached: it is runtime config, and a stale copy pins the module flags.
        res.setHeader('Cache-Control', 'no-store');
        res.end(readFileSync(shellConfig, 'utf8'));
      });
    },
  };
}

// https://vite.dev/config/
export default defineConfig({
  // Compass is a path-mounted LEAP module served under /compass/, mirroring the timesheet SPA's
  // '/timesheet/' and ooto's '/ooto/'. Vite prefixes every emitted asset URL and the dev server
  // serves the app at /compass/. This value must stay in step with the nginx mount path and the
  // Playwright proxy — a mismatch produces a blank page with 404s on every asset.
  base: '/compass/',
  plugins: [react(), tailwindcss(), serveShellConfigInDev()],
  server: {
    host: '0.0.0.0',
    // 5173 is the timesheet SPA, 5174 is ooto, 5175 is the static shell server.
    port: 5176,
    watch: {
      usePolling: true,
    },
    proxy: {
      // So `npm run dev` alone is usable: the page's API calls reach the local API.
      '/api': {
        target: process.env.VITE_API_TARGET ?? 'http://localhost:5009',
        changeOrigin: true,
      },
      '/health': {
        target: process.env.VITE_API_TARGET ?? 'http://localhost:5009',
        changeOrigin: true,
      },
      // Same-origin auth surface (issue #217, mirroring web/timesheet/vite.config.ts): /auth/*
      // (login, login-callback, logout, stub-login) is served by the API, not the Compass SPA.
      // `installSessionExpiredRedirect` (src/lib/session-redirect.ts) does a full-page navigation
      // to `/auth/login?returnUrl=...` on any 401 -- without this proxy entry, that request falls
      // through to Vite's own SPA fallback and never reaches the API in Compass's standalone dev
      // topology (deployed nginx already routes /auth to the API; this keeps local dev consistent).
      // changeOrigin stays FALSE, matching timesheet's entry: Sustainsys computes the SAML ACS URL
      // from Request.Host, so the browser-facing host must reach the API with Host preserved.
      '/auth': {
        target: process.env.VITE_API_TARGET ?? 'http://localhost:5009',
        changeOrigin: false,
        xfwd: true,
      },
      '/Saml2': {
        target: process.env.VITE_API_TARGET ?? 'http://localhost:5009',
        changeOrigin: false,
        xfwd: true,
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './tests/unit/setup.ts',
    include: ['tests/unit/**/*.test.{ts,tsx}'],
    coverage: {
      provider: 'v8',
      // 'json-summary' produces coverage/coverage-summary.json. Its ABSENCE is what silently
      // reported a frontend line rate of 0 for the timesheet SPA on every PR until Phase 48's gate
      // audit found it (G-15). A coverage gate must prove it EXECUTED — do not remove this reporter.
      reporter: ['text', 'json', 'json-summary', 'lcov'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,tsx}'],
      // Generic entries ONLY. Do NOT add a project-specific exclusion: Compass is at the platform's
      // 98% tier from day one with ZERO exemptions, and the exclusion guard blocks set growth anyway.
      // If a file cannot reach the tier, write the test.
      exclude: ['node_modules/', 'src/**/*.d.ts', 'tests/', '*.config.*'],
      thresholds: {
        lines: 98,
        functions: 98,
        branches: 98,
        statements: 98,
      },
    },
  },
});
