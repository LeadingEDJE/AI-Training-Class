import { describe, expect, it } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { ACCENT_SURFACE, buttonClassName } from '../../src/components/ui-classes';

/**
 * A button's appearance is defined once (issue #336, reworded 2026-08-21).
 *
 * The issue's own words: "Link and button styling should be applied in a single place so they can be
 * changed in one place instead of multiple." `table-link-consistency.test.ts` is the link half; this
 * is the button half.
 *
 * **What is banned, and why these two signatures.** A button's generic utilities (`rounded`, `border`,
 * `px-3`) are used by every control in the app, so they cannot be banned. Its TONE is unique to it:
 *
 * - `hover:bg-brand-green/10` — only a secondary button hovers that way;
 * - {@link ACCENT_SURFACE}'s pair, `bg-brand-green` + `border-brand-green-700` in one class string —
 *   only a primary surface carries the fill AND the §1.4.11 edge together.
 *
 * The pair matters rather than either half: `CompassNav`'s avatar and `Card`'s accent bar both use
 * `bg-brand-green` alone and are not buttons, so banning the fill on its own would need an allowlist.
 *
 * **No allowlist, and that is a property the code had to earn.** Two sites failed this when it was
 * written — `NotFoundPage`'s hand-rolled "Back to Compass" (a secondary button that had already
 * drifted to `font-medium` and `py-2` after the 2026-08-18 weight change) and `Toggle`'s track, which
 * repeated the accent pair literally. The Toggle now consumes {@link ACCENT_SURFACE}, which is what
 * lets this scan carry no exemptions.
 */
const SRC_DIR = join(__dirname, '../../src');

/** The one file allowed to define a button's tone. */
const DEFINITION_FILE = 'components/ui-classes.ts';

/** The secondary button's hover fill. `/5` is a table-row hover and deliberately not matched. */
const SECONDARY_HOVER = 'hover:bg-brand-green/10';

function listSourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      return listSourceFiles(full);
    }
    return /\.(ts|tsx|css)$/.test(entry) ? [full] : [];
  });
}

/**
 * Every `className="…"` / `className={…}` value in a source file, whitespace-normalised.
 *
 * Brace-DEPTH tracking rather than a regex. A regex that allows one level of nesting cannot read
 * `` className={`mt-6 ${buttonClassName({ variant: 'primary' })}`} `` — the `${` and the `({` are two
 * levels — and that is the shape of every composed call site in this codebase. So the naive version
 * was blind to precisely the sites this rule governs, while still reporting a pass. Its own
 * self-test below is what caught that.
 */
function classNameValues(content: string): string[] {
  const values: string[] = [];

  for (const match of content.matchAll(/className=/g)) {
    const i = match.index + match[0].length;

    if (content[i] === '"') {
      const end = content.indexOf('"', i + 1);
      if (end > 0) values.push(content.slice(i + 1, end).replace(/\s+/g, ' '));
      continue;
    }

    if (content[i] !== '{') continue;

    let depth = 0;
    for (let j = i; j < content.length; j += 1) {
      if ('{(['.includes(content[j])) depth += 1;
      else if ('})]'.includes(content[j])) {
        depth -= 1;
        if (depth === 0) {
          values.push(
            content
              .slice(i + 1, j)
              .replace(/\s+/g, ' ')
              .trim(),
          );
          break;
        }
      }
    }
  }

  return values.filter((value) => value !== '');
}

/**
 * Whether a class string paints a primary surface: the accent fill AND its §1.4.11 edge.
 *
 * NOT `\bbg-brand-green\b` for the fill. `-` and `/` are non-word characters, so a trailing `\b` is
 * satisfied by every step of the green scale — `bg-brand-green-tint`, `-accent`, `-700` and
 * `bg-brand-green/5` all matched, and any of them sitting beside a `border-brand-green-700` would be
 * reported as a hand-rolled button. The lookarounds below exclude the suffix forms, leaving only the
 * bare fill {@link ACCENT_SURFACE} actually carries.
 */
function carriesAccentPair(value: string): boolean {
  return (
    /(?<![\w-])bg-brand-green(?![\w/-])/.test(value) &&
    /(?<![\w-])border-brand-green-700(?![\w-])/.test(value)
  );
}

/**
 * Every className in `content` painting a button tone that only `ui-classes.ts` may define.
 *
 * Named rather than inlined into the scan below so a test can call it: with the filter inline,
 * combining the two predicates wrongly left every test in this file green (see the fixture test).
 */
function offendingClassNames(content: string): string[] {
  return classNameValues(content).filter(
    (value) => value.includes(SECONDARY_HOVER) || carriesAccentPair(value),
  );
}

function relative(file: string): string {
  return file.slice(SRC_DIR.length + 1).replace(/\\/g, '/');
}

describe('a button tone is defined once', () => {
  it('finds no hand-rolled button tone in any className outside ui-classes.ts', () => {
    const offenders: { file: string; classNames: string[] }[] = [];

    for (const file of listSourceFiles(SRC_DIR)) {
      if (relative(file) === DEFINITION_FILE) {
        continue;
      }

      const hits = offendingClassNames(readFileSync(file, 'utf8'));

      if (hits.length > 0) {
        offenders.push({ file: relative(file), classNames: [...new Set(hits)] });
      }
    }

    expect(offenders).toEqual([]);
  });

  it('bans exactly the tones the helper defines', () => {
    // Ties the ban to the definition: the signatures forbidden above are the ones `buttonClassName`
    // actually produces, so the rule redirects a caller to the helper rather than forbidding
    // something unrelated and passing for free.
    expect(buttonClassName({ variant: 'secondary' })).toContain(SECONDARY_HOVER);
    expect(carriesAccentPair(buttonClassName({ variant: 'primary' }))).toBe(true);
    expect(carriesAccentPair(ACCENT_SURFACE)).toBe(true);
  });

  it('inspects a non-trivial number of real source files', () => {
    // Non-vacuity: a path resolving to nothing passes this kind of scan for free.
    const files = listSourceFiles(SRC_DIR);

    expect(files.length).toBeGreaterThan(10);
    expect(files.filter((file) => file.endsWith('.tsx')).length).toBeGreaterThan(10);
  });

  it('would flag a hand-rolled button if one existed — the detector is not inert', () => {
    const secondary = 'rounded border border-brand-gray/70 px-3 py-2 hover:bg-brand-green/10';
    const primary = 'rounded border border-brand-green-700 bg-brand-green text-brand-ink';

    expect(secondary.includes(SECONDARY_HOVER)).toBe(true);
    expect(carriesAccentPair(primary)).toBe(true);
  });

  it('leaves the accent FILL alone where it is not a button', () => {
    // `CompassNav`'s avatar circle and `Card`'s accent bar carry `bg-brand-green` with no control
    // edge. Banning the fill on its own would need an allowlist, which is what this avoids.
    expect(carriesAccentPair('rounded-full bg-brand-green text-brand-ink')).toBe(false);
    expect(carriesAccentPair('h-4 w-1 rounded-sm bg-brand-green')).toBe(false);
    // And a table row's own hover tint is a different, weaker fill than the button's.
    expect('hover:bg-brand-green/5'.includes(SECONDARY_HOVER)).toBe(false);

    // No step of the green SCALE is the accent fill, even beside the accent border. Under a trailing
    // `\b` every one of these matched, because `-` and `/` end a word — so a green-tinted pill or a
    // hover-tinted row that gained a green border would have been called a hand-rolled button.
    expect(carriesAccentPair('bg-brand-green-tint border-brand-green-700')).toBe(false);
    expect(carriesAccentPair('bg-brand-green-accent border-brand-green-700')).toBe(false);
    expect(carriesAccentPair('hover:bg-brand-green/5 border-brand-green-700')).toBe(false);
    expect(carriesAccentPair('bg-brand-green-700 border-brand-green-700')).toBe(false);
  });

  it('reports each tone through the scan itself, not only through its predicates', () => {
    // The combining step the file scan runs. Without this, flipping `||` to `&&` in
    // `offendingClassNames` hides a secondary button carrying ONLY the hover fill — the NotFoundPage
    // case this gate was written for — while every other test here stays green.
    const secondaryOnly = '<a className="rounded border px-3 py-2 hover:bg-brand-green/10">';
    const primaryOnly = '<button className="border-brand-green-700 bg-brand-green text-brand-ink">';

    expect(offendingClassNames(secondaryOnly)).toHaveLength(1);
    expect(offendingClassNames(primaryOnly)).toHaveLength(1);
    expect(offendingClassNames('<div className="rounded border px-3 py-2">')).toEqual([]);
  });

  it('reads a className built from a template literal', () => {
    // Every composed call site is `` `${...} ...` ``, so a quotes-only scan would miss them. The
    // backticks come back with the value; harmless, since every check here is a substring match.
    const literals = classNameValues('<a className={`mt-6 ${buttonClassName({ x: 1 })}`}>');

    expect(literals).toEqual(['`mt-6 ${buttonClassName({ x: 1 })}`']);
  });
});
