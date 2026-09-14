import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

import { describe, expect, it } from 'vitest';

import { SHARED_CHROME_EXCLUSIONS } from '../e2e/helpers/axe';

/**
 * Feature 011, T017 — the axe sweep carries NO exclusions, and re-adding one is visible.
 *
 * **Why this needs its own assertion rather than trusting the constant.** Between issue #84 and issue
 * #83 the sweep excluded `.overflow-x-auto` — the exact selector carrying a `serious` WCAG violation —
 * with a stated reason and no tracking issue. The result was a suite that walked 23 screens at five
 * viewports and reported `NONE` on every one, including a screen with three `serious` nodes. Full
 * coverage, zero detection.
 *
 * The exclusion was honest about itself and still hid a live defect for weeks, so "write down why" is
 * demonstrably not enough on its own. This test makes the next one a deliberate, reviewable act: adding a
 * selector fails a named test whose message says what the alternative is.
 *
 * Not a substitute for judgement — a reviewer can still approve a change here. It just cannot happen
 * quietly.
 */
describe('axe sweep exclusions (AC-NFR-5, #83/#84)', () => {
  it('excludes nothing', () => {
    expect(
      SHARED_CHROME_EXCLUSIONS,
      'The axe sweep must scan the whole page. An exclusion here silences the sweep across EVERY ' +
        'screen it walks — that is how a serious `scrollable-region-focusable` violation shipped ' +
        'while the gate reported green. If axe is reporting something, fix the finding; if the ' +
        'finding is genuinely not ours to fix, file an issue and reference it here rather than ' +
        'leaving an unowned exclusion behind.',
    ).toEqual([]);
  });

  /**
   * The same policy, enforced against the SPEC FILES rather than the constant.
   *
   * **This gate was blind to the thing it was written for, and that is why it now reads source.**
   * `SHARED_CHROME_EXCLUSIONS` was emptied by T017, and the assertion above went green — while four
   * specs (`sales-dashboard`, `reports-availability`, `reports-assignment-duration`,
   * `reports-assignment-start`) each carried their own hand-transcribed `axe.run` with
   * `exclude: [['.overflow-x-auto']]` still hard-coded inline. Importing one constant cannot see a
   * copy that never imported it, so the exclusion this file exists to forbid survived in four places
   * for as long as the copies did — including on `reports-availability`, the screen `helpers/axe.ts`
   * names as carrying three `serious` nodes without it.
   *
   * `docs/TEST-STRATEGY.md` calls that shape out directly: *"a second copy decays independently."*
   * The copies are gone — every spec now calls `expectNoWcagViolations` — and this keeps a fifth from
   * appearing.
   */
  it('no spec hand-rolls its own axe run with an inline exclusion', () => {
    const e2eDir = join(__dirname, '../e2e');
    const specs = readdirSync(e2eDir).filter((name) => name.endsWith('.spec.ts'));

    // A directory listing that matched nothing would iterate zero files and PASS, reporting a clean
    // sweep over an empty set — the fail-open shape that has cost this repo eight gates before.
    // Asserted BEFORE the loop, not after.
    expect(
      specs.length,
      'found no spec files to scan — this gate would then pass over nothing',
    ).toBeGreaterThan(0);

    const offenders = specs.filter((name) => {
      const source = readFileSync(join(e2eDir, name), 'utf8');
      // `axe.run` is the hand-rolled call; `exclude:` is the option only a hand-rolled call can set,
      // because `expectNoWcagViolations` takes no exclusion argument at all.
      return source.includes('axe.run(') && /\bexclude:/.test(source);
    });

    expect(
      offenders,
      `${offenders.join(', ')} hand-roll an axe run with an inline exclusion. Call ` +
        '`expectNoWcagViolations` / `expectNoWcagViolationsAt` from `helpers/axe.ts` instead — one ' +
        'exclusion policy, in one place, under the assertion above. A spec-local copy is invisible ' +
        'to that assertion and decays on its own.',
    ).toEqual([]);
  });
});
