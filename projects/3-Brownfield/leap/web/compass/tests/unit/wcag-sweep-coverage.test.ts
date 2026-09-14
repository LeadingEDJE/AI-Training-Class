import { readFileSync } from 'node:fs';
import { join } from 'node:path';

/**
 * AC-NFR-5 says "**every** screen". This asserts the WCAG sweep actually covers the router, rather
 * than covering whatever list someone last remembered to update (issue #84).
 */
/**
 * **Why a source scan rather than trusting the sweep.** A list-driven sweep is exactly the shape that
 * passes by covering nothing: delete a screen from the array and every assertion still goes green, and
 * the report reads as a clean bill of health. This repository has found eight gates silently failing
 * open that way, so the sweep gets a gate of its own.
 *
 * The check compares every LITERAL SEGMENT of each route declared in `router.ts` against the paths
 * named in the spec, anchored at the end of the path so a longer route cannot stand in for a shorter
 * one. It cannot tell whether a screen was scanned meaningfully; it can only tell whether the sweep
 * knows the screen exists, which is the failure mode that matters here.
 */
// `__dirname`, matching `brand-token-parity.test.ts`'s idiom for reading source from a test.
const routerSource = readFileSync(join(__dirname, '../../src/routes/router.ts'), 'utf8');
const sweepSource = readFileSync(join(__dirname, '../e2e/wcag-sweep.critical.spec.ts'), 'utf8');

/**
 * Routes the sweep does NOT visit, each with the reason it is covered elsewhere.
 *
 * Every entry must name where the coverage actually lives. "Not worth scanning" is not a reason — if a
 * screen is reachable, AC-NFR-5 applies to it.
 */
const COVERED_ELSEWHERE: Record<string, string> = {
  '/sales-dashboard': 'sales-dashboard.critical.spec.ts runs its own five-viewport sweep',
  '/availability': 'reports-availability.critical.spec.ts',
  '/assignment-duration': 'reports-assignment-duration.critical.spec.ts',
  '/assignment-start': 'reports-assignment-start.critical.spec.ts',
  '/sow-extension': 'reports-sow-extension.critical.spec.ts',
};

/**
 * The STRANDING sweep's inventory (feature 011, T009) — a second consumer of this same gate.
 *
 * **Why extend this gate rather than write a second one.** The stranding assertion (Gate A) has exactly
 * the failure mode this file was built to prevent: a hand-written screen list that silently stops
 * covering a screen someone added. That is not a new problem needing a new mechanism, it is this
 * problem again — so `pathPattern` and the waiver discipline are reused verbatim and only the source
 * file differs. A parallel gate beside this one would be the duplication the Rule of Three forbids, and
 * the weaker of the two would rot first.
 *
 * The stranding sweep waives NOTHING: unlike the axe sweep, it does not delegate any screen to a
 * per-screen spec, so every declared route must appear in `compass-routes.ts` directly.
 */
const strandingInventorySource = readFileSync(
  join(__dirname, '../e2e/helpers/compass-routes.ts'),
  'utf8',
);

const STRANDING_COVERED_ELSEWHERE: Record<string, string> = {};

/** Every `path: '...'` declared in the code-based route tree. */
function declaredRoutePaths(): string[] {
  return (
    [...routerSource.matchAll(/path:\s*'([^']+)'/g)]
      .map((match) => match[1])
      // The bare index routes ('/') are the parents' own landing pages; the sweep visits those parents
      // by their real URLs, so matching on '/' would be noise rather than coverage.
      .filter((path) => path !== '/')
  );
}

/**
 * Turns a router path into a pattern that matches how the sweep spells the SAME screen.
 *
 * **Every literal segment has to match, and the match has to end where the path ends.** The first
 * version of this compared only the prefix ahead of the first `$param`, which was fail-open in exactly
 * the way this gate claims to prevent: `/team-directory/$employeeId` reduced to `/team-directory`, and
 * the Team Directory LIST entry kept that substring alive, so deleting "Employee detail" from the sweep
 * still passed. Same for `/edjers/$edjerId` against the EDJEr list, `/clients/$clientId` against the
 * client list, and both assignment routes. Caught in review on PR #323 — CI had gone green over it.
 *
 * - A `$param` segment matches a template interpolation (`${employeeId}`), whatever it is named.
 * - The pattern is anchored on the closing quote or backtick, so a longer path that merely STARTS with
 *   this one is not accepted as covering it. That anchor is what distinguishes
 *   `/team-directory/$employeeId` from `/team-directory/$employeeId/assignments/new`.
 * - The leading `[^'`]*` absorbs the `/compass` prefix and any admin parent nesting, so this does not
 *   have to reimplement the router's parent/child composition to know that `/edjers` is served at
 *   `/compass/admin/edjers`.
 */
function pathPattern(routePath: string): RegExp {
  const body = routePath
    .split('/')
    .filter((segment) => segment !== '')
    .map((segment) =>
      segment.startsWith('$') ? '\\$\\{[^}]+\\}' : segment.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'),
    )
    .join('/');

  return new RegExp(`[^'\`]*${body}(?=['\`])`);
}

describe('WCAG sweep coverage (AC-NFR-5, #84)', () => {
  it('names every declared route, or waives it with a reason', () => {
    const uncovered = declaredRoutePaths().filter((path) => {
      if (path in COVERED_ELSEWHERE) {
        return false;
      }

      return !pathPattern(path).test(sweepSource);
    });

    expect(
      uncovered,
      'every route must be swept or waived in COVERED_ELSEWHERE with the spec that covers it',
    ).toEqual([]);
  });

  it('finds routes at all, so the check cannot pass by matching nothing', () => {
    // The non-vacuity assertion the gate itself needs. Without it, a rename of `path:` in router.ts
    // would empty the list and this suite would report success over zero screens.
    expect(declaredRoutePaths().length).toBeGreaterThanOrEqual(15);
  });

  it('waives nothing without naming where the coverage lives', () => {
    for (const [path, reason] of Object.entries(COVERED_ELSEWHERE)) {
      expect(reason, `${path} is waived without a stated reason`).toMatch(/spec\.ts/);
    }
  });
});

describe('Stranding sweep coverage (AC-NFR-7, #83)', () => {
  it('names every declared route in the stranding inventory, or waives it with a reason', () => {
    const uncovered = declaredRoutePaths().filter((path) => {
      if (path in STRANDING_COVERED_ELSEWHERE) {
        return false;
      }

      return !pathPattern(path).test(strandingInventorySource);
    });

    expect(
      uncovered,
      'every route must appear in tests/e2e/helpers/compass-routes.ts, or be waived with the spec ' +
        'that covers it. Gate A is only as good as this list — which is the failure feature 011 ' +
        'exists to correct, so do not add a waiver to make this pass.',
    ).toEqual([]);
  });

  it('carries the NotFound screen, which no route list can derive', () => {
    // Registered as `notFoundComponent` / `defaultNotFoundComponent`, not as a route — so
    // `declaredRoutePaths()` cannot see it and the check above would never notice its absence.
    // #84's axe sweep carries it for the same reason; this asserts the stranding sweep does too.
    expect(
      strandingInventorySource,
      'NotFoundPage is a real destination and needs an explicit entry (T007)',
    ).toMatch(/name: 'not-found'/);
  });

  it('does not silently shrink, so a dropped screen cannot pass as coverage', () => {
    // The non-vacuity assertion, matching the axe gate's. A screen deleted from `compassScreens`
    // would otherwise reduce the sweep and report success over fewer screens.
    const declaredScreens = [...strandingInventorySource.matchAll(/name: '[^']+'/g)].length;
    expect(
      declaredScreens,
      'the inventory declares 22 leaf routes + NotFound; a drop below 20 means screens were removed',
    ).toBeGreaterThanOrEqual(20);
  });
});
