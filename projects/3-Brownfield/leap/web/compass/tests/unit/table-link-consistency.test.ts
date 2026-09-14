import { describe, expect, it } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { tableLinkClass } from '../../src/components/ui-classes';

/**
 * One link style, defined once (owner request 2026-08-21).
 *
 * Eight screens had each written their own class run for a link in a table cell. {@link
 * tableLinkClass} is the fix; this keeps it the only definition. Blunt and with **no allowlist**, in
 * the shape `no-slate-palette.test.ts` established — an exemption is how a convention becomes
 * decorative. Banned outside `components/ui-classes.ts`: the bare `underline` utility, and
 * `decoration-brand-green-700`.
 *
 * `hover:underline` and `underline-offset-*` are NOT matched — `Table`'s sortable header and the tab
 * strip use them, and neither is a link.
 */
const SRC_DIR = join(__dirname, '../../src');

/** The one file allowed to define the link's appearance. */
const DEFINITION_FILE = 'components/ui-classes.ts';

/**
 * A bare `underline` in a class string — not `hover:underline`, not `underline-offset-2`.
 *
 * No `g` flag: `test` on a global regex advances `lastIndex`, so a shared instance would answer
 * `false` for every second caller and skip half its offenders while still passing.
 */
const BARE_UNDERLINE = /(?<![\w:-])underline(?![\w-])/;

function listSourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      return listSourceFiles(full);
    }
    return /\.(ts|tsx|css)$/.test(entry) ? [full] : [];
  });
}

/** Every `className="…"` / `className={`…`}` string literal in a source file. */
function classNameLiterals(content: string): string[] {
  return [...content.matchAll(/className=(?:"([^"]*)"|\{`([^`]*)`\})/g)].map(
    (match) => match[1] ?? match[2],
  );
}

function relative(file: string): string {
  return file.slice(SRC_DIR.length + 1).replace(/\\/g, '/');
}

describe('one link style, defined once', () => {
  it('finds no hand-rolled underline in any className outside ui-classes.ts', () => {
    const offenders: { file: string; classNames: string[] }[] = [];

    for (const file of listSourceFiles(SRC_DIR)) {
      if (relative(file) === DEFINITION_FILE) {
        continue;
      }

      const hits = classNameLiterals(readFileSync(file, 'utf8')).filter((value) =>
        BARE_UNDERLINE.test(value),
      );

      if (hits.length > 0) {
        offenders.push({ file: relative(file), classNames: [...new Set(hits)] });
      }
    }

    expect(offenders).toEqual([]);
  });

  it('finds the green underline decoration only in ui-classes.ts', () => {
    const offenders = listSourceFiles(SRC_DIR)
      .filter((file) => relative(file) !== DEFINITION_FILE)
      .filter((file) => readFileSync(file, 'utf8').includes('decoration-brand-green-700'))
      .map(relative);

    expect(offenders).toEqual([]);
  });

  it('bans exactly the utility the shared helper carries', () => {
    // Proves the banned utility is the one the helper supplies, so the ban redirects callers to it
    // rather than forbidding something unrelated and passing for free.
    expect(BARE_UNDERLINE.test(tableLinkClass)).toBe(true);
    expect(tableLinkClass).toContain('decoration-brand-green-700');
  });

  it('inspects a non-trivial number of real source files', () => {
    // Non-vacuity: a path resolving to nothing passes this kind of scan for free.
    const files = listSourceFiles(SRC_DIR);

    expect(files.length).toBeGreaterThan(10);
    expect(files.filter((file) => file.endsWith('.tsx')).length).toBeGreaterThan(10);
  });

  it('would flag a hand-rolled link if one existed — the detector is not inert', () => {
    // The regex matches what it looks for and skips a hover-underlined header.
    const offender = classNameLiterals(
      '<a className="text-brand-text underline underline-offset-2">',
    );
    const sortHeader = classNameLiterals(
      '<button className="uppercase underline-offset-2 hover:underline">',
    );

    expect(offender).toHaveLength(1);
    expect(BARE_UNDERLINE.test(offender[0])).toBe(true);
    expect(sortHeader).toHaveLength(1);
    expect(BARE_UNDERLINE.test(sortHeader[0])).toBe(false);
  });

  it('reads a template-literal className, not only a quoted one', () => {
    // The composed form is a template literal, so a quotes-only scan would miss the very sites this
    // rule governs.
    const literals = classNameLiterals('<a className={`${tableLinkClass} whitespace-nowrap`}>');

    expect(literals).toEqual(['${tableLinkClass} whitespace-nowrap']);
  });
});
