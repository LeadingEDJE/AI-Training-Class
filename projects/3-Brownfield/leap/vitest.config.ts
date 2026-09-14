import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    // `cjs` added in Phase 50 (plan 50-02). The scripts under .github/scripts/ are
    // `.cjs` by convention (so `require()` inside actions/github-script resolves as
    // CommonJS regardless of the root package.json "type"), which makes `.test.cjs`
    // an easy and WRONG guess for the matching test file -- and a test file this glob
    // does not match is not reported as anything. It simply never runs. That is the
    // ghost-test trap: the same class as `npm run test:scripts` itself being wired to
    // nothing for its entire life (closed in plan 50-01). Matching `cjs` means such a
    // file is collected and fails loudly if it is malformed, instead of being silently
    // ignored. House style remains `.test.js` with ESM imports.
    include: ['.github/scripts/**/*.test.{js,cjs,mjs,ts}'],
    environment: 'node',
    globals: false,
    // A zero-test run is a FAILURE, not a pass. If the include pattern ever stops
    // matching, the CI step must go red rather than report success over nothing.
    passWithNoTests: false,
  },
});
