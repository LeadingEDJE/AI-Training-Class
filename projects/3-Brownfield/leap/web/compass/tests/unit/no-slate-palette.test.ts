import { describe, expect, it } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Enforces the #57 brand-token retrofit across EVERY Compass screen, not just the navigation.
 *
 * `CompassNav.test.tsx` already asserts the nav carries `border-brand-taupe/40` / `text-brand-gray`
 * and no `slate-` class. That assertion is scoped to one component, so the rule it encodes was
 * silently optional everywhere else — and feature 004's screens duly re-introduced 21 `slate-`
 * classes while passing every gate. Widened here, with no allowlist: an exemption is how a
 * convention becomes decorative.
 *
 * The scan is deliberately BLUNT — it matches `slate-<digits>` anywhere in a file, comments included,
 * rather than trying to parse `className` context. A context-aware scan is a scan with holes. The cost
 * is that prose under `src/` must spell the palette without the hyphen-digit form ("Tailwind's slate
 * 400 step"); `CompassBoundaryTests` chose its own token the same way, so a comment mentioning the word
 * cannot trip it.
 *
 * The rule is about token discipline, not contrast. Contrast is measured separately
 * (`brand-contrast.test.ts`, plus real axe-core in
 * `web/timesheet/tests/e2e/compass-accessibility.critical.spec.ts`) — a `slate-` class can be
 * perfectly readable and still be off-palette, which is what this catches.
 */
const SRC_DIR = join(__dirname, '../../src');

function listSourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      return listSourceFiles(full);
    }
    return /\.(ts|tsx|css)$/.test(entry) ? [full] : [];
  });
}

describe('the brand palette is the only palette', () => {
  it('finds no Tailwind slate- utility anywhere under src/', () => {
    const offenders: { file: string; tokens: string[] }[] = [];

    for (const file of listSourceFiles(SRC_DIR)) {
      const content = readFileSync(file, 'utf8');
      const tokens = [...content.matchAll(/[\w:/[\]-]*\bslate-\d+\b/g)].map((match) => match[0]);
      if (tokens.length > 0) {
        offenders.push({
          file: file.slice(SRC_DIR.length + 1).replace(/\\/g, '/'),
          tokens: [...new Set(tokens)],
        });
      }
    }

    expect(offenders).toEqual([]);
  });

  it('inspects a non-trivial number of real source files', () => {
    // Non-vacuity. A path that resolves to nothing passes this kind of scan for free, which is the
    // shape this repository has been bitten by repeatedly — so the scan proves it looked at something
    // before anything is concluded from an empty offender list.
    const files = listSourceFiles(SRC_DIR);

    expect(files.length).toBeGreaterThan(10);
    expect(files.some((file) => file.endsWith('.tsx'))).toBe(true);
    expect(files.some((file) => file.endsWith('index.css'))).toBe(true);
  });

  it('would flag a slate class if one existed — the detector is not inert', () => {
    // Proves the regex actually matches the thing it is looking for, rather than the offender list
    // being empty because the pattern never fires.
    const sample = 'className="border-slate-400 hover:bg-slate-100 text-brand-gray"';

    const tokens = [...sample.matchAll(/[\w:/[\]-]*\bslate-\d+\b/g)].map((match) => match[0]);

    expect(tokens).toContain('border-slate-400');
    expect(tokens).toContain('hover:bg-slate-100');
    expect(tokens).not.toContain('text-brand-gray');
  });
});
