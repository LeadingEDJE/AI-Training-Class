import { describe, expect, it } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Enforces issue #234 — "all dates should be displayed in [mm/dd/yyyy] throughout" — mechanically.
 *
 * Every Compass `DateOnly` arrives on the wire as `yyyy-MM-dd`, so rendering one straight into JSX
 * shows a user the exact format #234 was filed to remove. `lib/date.ts` exports `formatDate`,
 * `formatOptionalDate` and `formatDateTime` for this; the rule is that a date-bearing value reaching
 * the screen goes through a formatter.
 *
 * This gate exists because the manual version did not hold. Feature 007's T099 asks someone to
 * "confirm every date the four screens render goes through formatDate" — scoped to four NAMED
 * screens, so a fifth was covered by nothing, and `SowList.tsx` shipped rendering `2024-04-01`
 * (issue #306). The repo already guards `fetch`, the slate palette and the brand tokens this way;
 * dates had no equivalent.
 *
 * <h3>Detection is per INTERPOLATION, not per line</h3>
 * The first version of this gate decided line-by-line and had two false negatives, both found in
 * review of PR #307:
 *
 * - `<div>Expires {row.endDate}</div>` — the `{` is preceded by label text rather than by `>`, so a
 *   "must directly follow a tag" rule missed the most ordinary way a date appears next to a word.
 * - `<td>{formatDate(a)} {row.sowEndDate}</td>` — one formatter call anywhere on the line
 *   short-circuited the whole line, so a second, unformatted date beside it escaped entirely.
 *
 * Both are fixed by evaluating each `{...}` on its own: its position is resolved by scanning
 * backwards to the first character that settles it, and its formatting is judged from its own text.
 */
const SRC_DIR = join(__dirname, '../../src');

/** A single `{...}` interpolation with no nested braces. */
const INTERPOLATION = /\{[^{}]*\}/g;

/** An identifier whose last word is `Date` — `row.sowStartDate`, `endDate`. */
const DATE_BEARING = /\b\w*Date\b/;

/**
 * Any date formatter, rather than a fixed list of names.
 *
 * `formatOptionalDate` already exists and delegates to `formatDate`. Naming formatters individually
 * would mean this gate goes stale at the next legitimate wrapper, so the rule is "goes through
 * something that formats a date" — which is what it actually means.
 */
const FORMATTER_CALL = /\bformat\w*Date\w*\(/;

/**
 * Only `.tsx`. The rule is about RENDERING, and JSX cannot appear in a `.ts` file — scanning those
 * matches object literals such as `{ key: 'hireDate', label: 'Hire Date' }`, which are column
 * configuration rather than output.
 *
 * `lib/date.ts` therefore needs no exemption: it is a `.ts` file and is never collected. An earlier
 * revision carried one, which review correctly called dead code.
 */
function listSourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      return listSourceFiles(full);
    }
    return /\.tsx$/.test(entry) ? [full] : [];
  });
}

/**
 * Whether this interpolation is JSX CHILD text — something a user reads — by scanning backwards to
 * the first character that settles the question.
 *
 * `>` means we walked out of an opening tag and everything between was child content, whether that
 * was nothing (`<td>{date}`) or a label (`<div>Expires {date}`). `=` is an attribute
 * (`value={date}`, `key={date}`): an `<input type="date">` value must stay ISO by HTML spec and a
 * key is an identity, not output. `(` and `,` are call arguments, `<` means we reached a tag
 * boundary without passing content. `$` settles nothing on its own — it says we are in a
 * template-literal slot, so the scan restarts from the literal's own position.
 *
 * Anything unresolved defaults to NOT a child, so the gate stays quiet rather than guessing.
 */
function isJsxChild(line: string, braceIndex: number): boolean {
  let sawOnlyWhitespace = true;

  for (let i = braceIndex - 1; i >= 0; i--) {
    const character = line[i];

    if (character === '>') {
      // `=>` is an ARROW, not a closing tag. Without this, any date interpolated inside an arrow
      // function passed as a prop — `rowKey={(row) => `${row.startDate}`}` — scans back to the arrow's
      // `>` and reads as child text. That is a false positive: an arrow body inside an attribute is
      // still attribute context, and a React key is an identity, not output. Found by feature 011's
      // StackedRows migration, where the same key that passed as `<tr key={...}>` started failing once
      // it moved into a `rowKey` prop. Narrowing a false positive, not relaxing the rule: a genuine
      // `<div>{date}` still hits a bare `>` and is still caught.
      if (i > 0 && line[i - 1] === '=') {
        i--;
        sawOnlyWhitespace = false;
        continue;
      }
      return true;
    }

    // A COMPLETED interpolation to our left tells us nothing about our own position, so step over it
    // and keep looking. Without this, `<td>{formatDate(a)} {row.endDate}</td>` resolves the second
    // date against the first one's closing paren and reads as a call argument.
    if (character === '}') {
      const opening = line.lastIndexOf('{', i);
      if (opening === -1) {
        return false;
      }
      i = opening;
      sawOnlyWhitespace = false;
      continue;
    }

    // A template-literal slot. Whether it is output depends entirely on where the LITERAL sits, so
    // hop to its opening backtick and judge that instead of assuming an attribute. `title={`due
    // ${date}`}` is a prop; `<div>{`Expires ${date}`}</div>` is child text a user reads, and
    // answering false for both is how the second one escaped this gate (PR #310 review finding).
    if (character === '$') {
      const backtick = line.lastIndexOf('`', i);
      if (backtick === -1) {
        return false;
      }

      // The literal is normally wrapped in a JSX expression container, and it is that container's
      // position which settles the question — so step over it rather than reading its `{` as the
      // start of an object literal.
      let outer = backtick - 1;
      while (outer >= 0 && /\s/.test(line[outer])) {
        outer--;
      }

      return isJsxChild(line, outer >= 0 && line[outer] === '{' ? outer : backtick);
    }

    if (['=', '(', ',', '<', '{', ':'].includes(character)) {
      return false;
    }

    if (!/\s/.test(character)) {
      sawOnlyWhitespace = false;
    }
  }

  // Nothing to our left on this line: a `{` opening a line in a .tsx file is multi-line JSX. A
  // multi-line OBJECT literal cannot reach here — its properties carry no braces of their own, so no
  // interpolation matches on those lines at all.
  return sawOnlyWhitespace;
}

function findOffendingLines(content: string): string[] {
  return content
    .split('\n')
    .flatMap((line, index) =>
      [...line.matchAll(INTERPOLATION)]
        .filter(
          (match) =>
            DATE_BEARING.test(match[0]) &&
            !FORMATTER_CALL.test(match[0]) &&
            isJsxChild(line, match.index),
        )
        .map(() => `line ${index + 1}: ${line.trim()}`),
    );
}

describe('no raw dates rendered to the screen (issue #234)', () => {
  it('finds no JSX rendering a date-bearing value without a date formatter', () => {
    const offenders: string[] = [];

    for (const file of listSourceFiles(SRC_DIR)) {
      const relative = file.slice(SRC_DIR.length + 1).replace(/\\/g, '/');

      for (const offendingLine of findOffendingLines(readFileSync(file, 'utf8'))) {
        offenders.push(`${relative} ${offendingLine}`);
      }
    }

    expect(offenders).toEqual([]);
  });

  // ---------------------------------------------------------------- it fires when it should

  it('fires on a raw date interpolation, so the check can fail at all', () => {
    // A gate that has never failed has never run. This is the shape that shipped in #306.
    expect(findOffendingLines('<td>{row.sowStartDate}</td>')).toHaveLength(1);
  });

  it('fires on a raw date opening its own line in multi-line JSX', () => {
    expect(findOffendingLines('<td>\n  {row.sowEndDate}\n</td>')).toHaveLength(1);
  });

  it('fires on a raw date sitting next to label text', () => {
    // PR #307 review finding 1. The `{` follows a word, not a tag — the most ordinary way a date
    // appears beside a label, and the first version of this gate missed it.
    expect(findOffendingLines('<div>Expires {row.endDate}</div>')).toHaveLength(1);
  });

  it('fires on an unformatted date sharing a line with a formatted one', () => {
    // PR #307 review finding 2. A line-level formatter check short-circuited the whole line, so the
    // second date escaped. Each interpolation is now judged on its own text.
    expect(findOffendingLines('<td>{formatDate(a.startDate)} {row.sowEndDate}</td>')).toHaveLength(
      1,
    );
  });

  it('fires on a raw date inside a template literal that IS the JSX child text', () => {
    // PR #310 review finding. `$` in the backwards scan means "template-literal slot", which is only
    // a reason to stay quiet when the literal is a prop value. A literal that is itself the child of
    // a tag is read by a user, so its slots are output like any other.
    expect(findOffendingLines('<div>{`Expires ${row.endDate}`}</div>')).toHaveLength(1);
  });

  it('fires on each raw slot of a child template literal, not just the first', () => {
    // Same rule as the per-interpolation change above: a range rendered this way hides two dates.
    expect(findOffendingLines('<td>{`${row.startDate} – ${row.endDate}`}</td>')).toHaveLength(2);
  });

  it('accepts a child template literal whose slots go through a formatter', () => {
    expect(findOffendingLines('<div>{`Expires ${formatDate(row.endDate)}`}</div>')).toEqual([]);
  });

  // ---------------------------------------------------------------- it stays quiet when it should

  it('accepts the same value once it goes through formatDate', () => {
    expect(findOffendingLines('<td>{formatDate(row.sowStartDate)}</td>')).toEqual([]);
  });

  it('accepts a local wrapper such as formatOptionalDate, not only formatDate by name', () => {
    expect(findOffendingLines('<Cell>{formatOptionalDate(row.sowEndDate)}</Cell>')).toEqual([]);
  });

  it('accepts two formatted dates on one line', () => {
    expect(
      findOffendingLines('<td>{formatDate(a.startDate)} – {formatDate(a.endDate)}</td>'),
    ).toEqual([]);
  });

  it('accepts a date bound to an input value, which must stay ISO by HTML spec', () => {
    expect(
      findOffendingLines('<input type="date" value={field.state.value.sowEndDate} />'),
    ).toEqual([]);
  });

  it('accepts a date inside a React key, which is an identity rather than output', () => {
    expect(findOffendingLines('<li key={`${sow.startDate}-${sow.endDate}`}>')).toEqual([]);
    // The same identity, moved into a prop that takes an arrow function. `=>` must not read as a
    // closing tag (feature 011 / StackedRows).
    expect(
      findOffendingLines(
        'rowKey={(row, index) => `${row.employeeName}-${row.startDate}-${index}`}',
      ),
    ).toEqual([]);
    // ...and the arrow allowance must not swallow a genuine child interpolation.
    expect(findOffendingLines('<td>{row.startDate}</td>')).not.toEqual([]);
  });

  it('accepts a date in a template literal used as a prop value', () => {
    // The reason the `$` rule exists: a literal in an attribute position is not child text, and the
    // fix for the child case must not swallow this one.
    expect(findOffendingLines('<Cell title={`due ${row.endDate}`} />')).toEqual([]);
  });

  it('accepts a date passed as a function argument rather than rendered', () => {
    expect(findOffendingLines('await props.onSubmit({ startDate, endDate, note });')).toEqual([]);
  });

  it('accepts a date in an object literal property', () => {
    expect(
      findOffendingLines("  const columns = { key: 'hireDate', label: 'Hire Date' };"),
    ).toEqual([]);
  });

  it('does not fire on an identifier that merely starts with a date word', () => {
    // `endDateError` is a validation message, not a date. Matching it would push authors toward
    // suppressing the rule, which is how a gate stops being trusted.
    expect(findOffendingLines('<p>{endDateError}</p>')).toEqual([]);
  });

  // ---------------------------------------------------------------- non-vacuity

  it('inspects real files rather than resolving nothing', () => {
    // A path-based check that matches zero files PASSES. This repository has produced that
    // fail-open shape repeatedly, so the file count is asserted before anything is concluded.
    expect(listSourceFiles(SRC_DIR).length).toBeGreaterThan(20);
  });
});
