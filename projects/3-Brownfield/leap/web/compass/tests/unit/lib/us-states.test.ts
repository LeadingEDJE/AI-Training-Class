import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { US_STATES } from '../../../src/lib/us-states';

/**
 * The residence vocabulary the EDJEr form offers, and the one the server accepts.
 *
 * **The two must agree exactly.** `api/Modules/Compass/UsStateCodes.cs` is the authority — it generates
 * the database `CHECK` on `compass.employee.state_of_residence` and the service validates against it
 * (FR-014). This list is the presentation half: the form shows names because "Ohio" is what a human
 * reads, and submits codes because `char(2)` is what the column holds.
 *
 * A drift between the two is a 400 on a value the form offered, which is the worst kind — the
 * administrator did nothing wrong and has no way to tell. So the count and the codes are asserted here,
 * and the acceptance test for the pairing is the endpoint refusing anything outside the set.
 */
describe('US_STATES', () => {
  it('offers exactly fifty-one entries — the 50 states plus DC', () => {
    // Matches UsStateCodes.All.Length, asserted at 51 by UsStateCodesTests. A count that drifts means a
    // state was lost from the form or a territory crept in.
    expect(US_STATES).toHaveLength(51);
  });

  it('includes DC, because an EDJEr can reside there', () => {
    expect(US_STATES.map((state) => state.code)).toContain('DC');
  });

  it.each(['PR', 'GU', 'VI', 'AS', 'MP'])(
    'excludes %s — a US territory, deliberately outside the vocabulary (AC-NFR-6)',
    (code) => {
      // The server's CHECK constraint rejects these, so offering one in the form would produce a 400 on a
      // value the form itself suggested.
      expect(US_STATES.map((state) => state.code)).not.toContain(code);
    },
  );

  it('uses two-letter upper-case codes, which is what the char(2) column holds', () => {
    for (const state of US_STATES) {
      expect(state.code).toMatch(/^[A-Z]{2}$/);
    }
  });

  it('names every entry, so the form reads as prose rather than as codes', () => {
    for (const state of US_STATES) {
      expect(state.name.length).toBeGreaterThan(2);
    }
  });

  it('has no duplicate code', () => {
    // A duplicate would satisfy the count check while masking a missing state, and would render two
    // identical options.
    const codes = US_STATES.map((state) => state.code);
    expect(new Set(codes).size).toBe(codes.length);
  });

  it('has no duplicate name', () => {
    const names = US_STATES.map((state) => state.name);
    expect(new Set(names).size).toBe(names.length);
  });

  it('is ordered by name, which is how a person scans a select', () => {
    // Not by code: a list running AL, AK, AZ… is alphabetical by an abbreviation the reader is not
    // looking at. Alabama, Alaska, Arizona is.
    const names = US_STATES.map((state) => state.name);
    expect(names).toEqual([...names].sort((left, right) => left.localeCompare(right)));
  });
});

/**
 * The two halves of the vocabulary, compared directly.
 *
 * Counting to 51 on both sides is not the same as agreeing: two lists can each hold 51 codes and hold a
 * different 51. This reads the C# authority and diffs the sets, which is the only assertion that actually
 * rules out the drift — a 400 on a value the form offered, where the administrator did nothing wrong.
 *
 * It reaches across into `api/` from a frontend test, which is unusual and deliberate: the coupling is
 * real and one-directional, and asserting it from the side that can be wrong is where it belongs. The
 * same technique guards `index.css` against `brand-tokens.ts` in `brand-token-parity.test.ts`.
 *
 * It fails CLOSED. If the layout moves, `readFileSync` throws and the test fails rather than quietly
 * comparing nothing — the fail-open shape this repository has been bitten by repeatedly.
 */
describe('US_STATES agrees with the server vocabulary it must match', () => {
  const AUTHORITY = join(__dirname, '../../../../../api/Modules/Compass/UsStateCodes.cs');

  /** The codes declared in `UsStateCodes.All`, read from the C# source. */
  function serverCodes(): string[] {
    const source = readFileSync(AUTHORITY, 'utf8');

    // Scoped to the `All` array's own initializer rather than the whole file, so the codes quoted in its
    // doc comments and in `IsValid`'s remarks cannot pad the set.
    const block = /public static readonly string\[\] All =\s*\[([\s\S]*?)\];/.exec(source);
    if (block === null) {
      // Thrown rather than asserted, so a caller cannot carry on with an empty set: an assertion here
      // would fail this case and still return [], and the sibling cases would then compare nothing.
      throw new Error(`could not find UsStateCodes.All in ${AUTHORITY}`);
    }

    return [...block[1].matchAll(/"([A-Z]{2})"/g)].map((match) => match[1]);
  }

  it('found the authority array at all', () => {
    // Non-vacuity. An empty parse would make every comparison below trivially true.
    expect(serverCodes().length).toBeGreaterThanOrEqual(51);
  });

  it('offers exactly the codes the server accepts, and no others', () => {
    const server = [...serverCodes()].sort();
    const offered = US_STATES.map((state) => state.code).sort();

    expect(offered).toEqual(server);
  });

  it('detects a difference rather than reporting agreement whatever it reads', () => {
    // Proves the comparison can fail: a code the server does not carry must not be in the offered set.
    expect(serverCodes()).not.toContain('PR');
  });
});
