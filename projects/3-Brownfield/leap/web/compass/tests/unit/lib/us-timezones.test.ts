import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { US_TIMEZONES } from '../../../src/lib/us-timezones';

/**
 * The timezone vocabulary the EDJEr form offers, and the one the server accepts.
 *
 * **The two must agree exactly.** `api/Modules/Compass/UsTimeZones.cs` is the authority — the service
 * validates a submitted zone against it (FR-8.1) and `compass.employee.timezone` stores the identifier
 * verbatim. This list is the presentation half: labels because "Eastern" is what a human reads, IANA ids
 * because that is what the column holds and what OOTO does date arithmetic with.
 *
 * A drift is a 400 on a value the form itself offered, where the administrator did nothing wrong — the
 * same failure `us-states.test.ts` guards against, and this file borrows its technique.
 */
describe('US_TIMEZONES', () => {
  it('offers exactly six options — no worldwide IANA list (FR-8.1b)', () => {
    // The acceptance criterion is a COUNT as much as a content check: "opening the dropdown shows only
    // those six options". A seventh is a defect even if it is a perfectly real zone.
    expect(US_TIMEZONES).toHaveLength(6);
  });

  it('pairs each display label with the IANA id the issue specifies', () => {
    // Transcribed from the issue #421 table, not from the source file, so a typo in either is caught
    // rather than agreed with.
    expect(US_TIMEZONES.map((zone) => [zone.label, zone.id])).toEqual([
      ['Eastern', 'America/New_York'],
      ['Central', 'America/Chicago'],
      ['Mountain', 'America/Denver'],
      ['Pacific', 'America/Los_Angeles'],
      ['Hawaiian', 'Pacific/Honolulu'],
      ['Alaskan', 'America/Anchorage'],
    ]);
  });

  it('submits IANA ids, never the labels', () => {
    // `Eastern` in the column would be unusable for date arithmetic and is what the legacy free-text
    // value looked like — the thing FR-8.6 moved away from.
    for (const zone of US_TIMEZONES) {
      expect(zone.id).toMatch(/^[A-Za-z]+\/[A-Za-z_]+$/);
    }
  });

  it('has no duplicate id and no duplicate label', () => {
    // A duplicate would satisfy the count check while masking a missing zone, and would render two
    // indistinguishable options.
    expect(new Set(US_TIMEZONES.map((zone) => zone.id)).size).toBe(6);
    expect(new Set(US_TIMEZONES.map((zone) => zone.label)).size).toBe(6);
  });
});

/**
 * The two halves of the vocabulary, compared directly.
 *
 * Counting to six on both sides is not the same as agreeing. This reads the C# authority and diffs the
 * sets, which is the only assertion that rules out the drift. It fails CLOSED: if the layout moves,
 * `readFileSync` throws and the test fails rather than quietly comparing nothing.
 */
describe('US_TIMEZONES agrees with the server vocabulary it must match', () => {
  const AUTHORITY = join(__dirname, '../../../../../api/Modules/Compass/UsTimeZones.cs');

  /** The identifiers declared in `UsTimeZones.All`, read from the C# source. */
  function serverZones(): string[] {
    const source = readFileSync(AUTHORITY, 'utf8');

    // Scoped to the `All` array's own initializer rather than the whole file, so the ids quoted in the
    // class's doc comments, in `Default`, and in `IsValid`'s remarks cannot pad the set.
    const block = /public static readonly string\[\] All =\s*\[([\s\S]*?)\];/.exec(source);
    if (block === null) {
      // Thrown rather than asserted: an assertion would fail this case and still return [], leaving the
      // sibling cases comparing nothing.
      throw new Error(`could not find UsTimeZones.All in ${AUTHORITY}`);
    }

    return [...block[1].matchAll(/"([A-Za-z]+\/[A-Za-z_]+)"/g)].map((match) => match[1]);
  }

  it('found the authority array at all', () => {
    // Non-vacuity. An empty parse would make every comparison below trivially true.
    expect(serverZones()).toHaveLength(6);
  });

  it('offers exactly the ids the server accepts, and no others', () => {
    expect([...US_TIMEZONES.map((zone) => zone.id)].sort()).toEqual([...serverZones()].sort());
  });

  it('offers them in the order the server declares', () => {
    // `UsTimeZones.All` documents its order as the one a dropdown should present — east to west, then
    // the two non-contiguous zones. Same set in a different order would pass the check above.
    expect(US_TIMEZONES.map((zone) => zone.id)).toEqual(serverZones());
  });

  it('detects a difference rather than reporting agreement whatever it reads', () => {
    // Proves the comparison can fail: a zone the server does not carry must not be in the parsed set.
    expect(serverZones()).not.toContain('Europe/London');
  });
});
