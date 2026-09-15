import { describe, expect, it } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';

/**
 * Every control whose label begins with "Add" carries exactly one leading `+` (owner request
 * 2026-08-18).
 *
 * **Why a source scan rather than an assertion per screen.** Three Add controls exist today, on three
 * different screens, and they disagreed: the lookup sections had `+ Add {singular}`, the EDJEr list had
 * `+ Add New EDJEr`, and the client list and the billable-category form had none. A per-screen
 * assertion fixes the three that exist and says nothing about the fourth, which is how the
 * inconsistency arose in the first place. This fails on the next one instead.
 *
 * **What it deliberately does not do:** it does not require a `+` on a control that merely creates
 * something. "New Assignment" and "Save New Employee Type" are not "Add" buttons and are left alone —
 * the rule is about the word, not the intent, because the word is what a reader is matching on.
 *
 * The scan is line-based and matches JSX text content, which is enough for this codebase: every Add
 * label is a literal in a `<Button>` or `<Link>` body. Braces are ALLOWED in the label so
 * `+ Add {singular}` is covered; `=`, `(`, `)`, `;` and angle brackets are not, which is what excludes
 * `aria-label` values, identifiers such as `addMutation`, and comments. A label assembled entirely from
 * a variable would still slip past, which is why the non-vacuity assertion below pins the number of
 * controls found — a regex that silently stops matching is the fail-open shape this repository has been
 * bitten by repeatedly.
 */
const SRC_DIR = join(__dirname, '../../src');

/**
 * How many Add controls the scan must find. Bump it when a screen gains one.
 *
 * 4 → 5 (feature 006 US3/#66, 2026-08-19): the SOWs/Contracts card on `AssignmentDetailPage` added
 * "+ Add SOW / Contract" — already carrying the single leading `+` this suite enforces.
 *
 * 5 → 6 (technical-skills feature): `SkillSection`'s "+ Add Skill" control, on the new Skills admin
 * screen.
 */
const EXPECTED_ADD_CONTROLS = 6;

function listSourceFiles(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      return listSourceFiles(full);
    }
    return /\.tsx$/.test(entry) ? [full] : [];
  });
}

/** Every line that is JSX text starting with an optional `+` and then the word "Add". */
function addControlLines(): { file: string; text: string }[] {
  const found: { file: string; text: string }[] = [];

  for (const file of listSourceFiles(SRC_DIR)) {
    const relative = file.slice(SRC_DIR.length + 1).replace(/\\/g, '/');

    for (const line of readFileSync(file, 'utf8').split('\n')) {
      const text = line.trim();
      // JSX text only: a line that is the whole label and nothing else. This is what excludes
      // `aria-label` values, comments and identifiers such as `addMutation`.
      if (/^\+?\s*Add\b[^<>=();]*$/.test(text)) {
        found.push({ file: relative, text });
      }
    }
  }

  return found;
}

describe('every Add control is prefixed with a single +', () => {
  it('finds the Add controls at all', () => {
    // Non-vacuity. Every assertion below passes for free against an empty list.
    expect(addControlLines()).toHaveLength(EXPECTED_ADD_CONTROLS);
  });

  it.each(addControlLines())('$file — "$text" starts with a + ', ({ text }) => {
    expect(text.startsWith('+ ')).toBe(true);
  });

  it('never doubles the +', () => {
    // "+ + Add" is the shape a second well-meaning fix produces.
    for (const { file, text } of addControlLines()) {
      expect(text, file).not.toMatch(/^\+\s*\+/);
    }
  });
});
