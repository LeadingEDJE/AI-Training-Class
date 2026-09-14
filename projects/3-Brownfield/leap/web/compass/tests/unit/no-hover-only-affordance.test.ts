import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * Feature 011, T050 (issue #83) — nothing in Compass is reachable only by hovering.
 *
 * **Why this is a guard and not a note in a commit message.** T050 asked for any hover-dependent
 * assertion to be re-expressed for the emulated device projects, because `isMobile` maps mouse input to
 * touch and a touch user never hovers. The answer turned out to be that there were none — but "I looked
 * and there weren't any" decays the moment somebody adds one, and the failure would be invisible: the
 * affordance renders, the tests pass, and it is simply unreachable on every phone.
 *
 * So the finding is expressed as an invariant instead. Every `hover:` utility in `web/compass/src` is
 * decorative — colour, underline, shadow, background — sitting on an element that is already a visible
 * link or button. What is forbidden is a `hover:` utility that changes whether content EXISTS on screen,
 * because that is the shape that strands a touch user.
 *
 * `group-hover:` is checked too: it is the other idiom for "reveal on hover", and reaches an element the
 * pointer is not even over.
 *
 * Scope note: this is deliberately a source scan, not a rendered-DOM assertion. A rendered check would
 * need every screen in every state to be visited to be meaningful, which is what the e2e device projects
 * do for behaviour; this catches the pattern at the point it is written, on every file, for free.
 */

/** Utilities that make content appear or disappear, rather than merely restyling it. */
const REVEALING = [
  'block',
  'flex',
  'inline',
  'inline-block',
  'inline-flex',
  'grid',
  'table',
  'visible',
  'opacity-100',
  'h-auto',
  'w-auto',
  'max-h-none',
  'not-sr-only',
];

const REVEAL_ON_HOVER = new RegExp(`\\bhover:(${REVEALING.join('|')})\\b`, 'g');
const GROUP_HOVER = /\bgroup-hover:/g;

function sourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      return sourceFiles(full);
    }
    return /\.(ts|tsx|css)$/.test(entry) ? [full] : [];
  });
}

describe('no hover-only affordance (AC-NFR-7, #83)', () => {
  // `__dirname`, matching `wcag-sweep-coverage.test.ts`'s idiom for reading source from a test.
  const files = sourceFiles(join(__dirname, '../../src'));

  it('finds source files at all, so the scan cannot pass by matching nothing', () => {
    // The non-vacuity assertion this gate needs — a moved `src/` would otherwise empty the list and
    // report success over zero files.
    expect(files.length).toBeGreaterThanOrEqual(20);
  });

  it('never reveals content on hover, because a touch user never hovers', () => {
    const offenders: string[] = [];

    for (const file of files) {
      const source = readFileSync(file, 'utf8');
      for (const match of source.matchAll(REVEAL_ON_HOVER)) {
        offenders.push(`${file.split('/src/')[1]}: ${match[0]}`);
      }
    }

    expect(
      offenders,
      'A `hover:` utility that changes whether content is on screen makes that content unreachable on ' +
        'every phone — `isMobile` maps mouse to touch and there is no hover state to enter. Give the ' +
        'content a focusable trigger (a button that toggles it) rather than a hover state.',
    ).toEqual([]);
  });

  it('never uses group-hover, the other reveal-on-hover idiom', () => {
    const offenders: string[] = [];

    for (const file of files) {
      const source = readFileSync(file, 'utf8');
      if (GROUP_HOVER.test(source)) {
        offenders.push(file.split('/src/')[1]);
      }
      GROUP_HOVER.lastIndex = 0;
    }

    expect(
      offenders,
      '`group-hover:` reveals an element the pointer is not even over, so it is strictly harder to ' +
        'reach by touch than a direct hover. If a group needs to disclose something, make the ' +
        'disclosure a control.',
    ).toEqual([]);
  });
});
