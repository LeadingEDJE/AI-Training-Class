import { describe, expect, it } from 'vitest';
import { formatDate, formatDateTime, formatOptionalDate } from '../../../src/lib/date';

/**
 * Every Compass date field is a server `DateOnly`, which serialises as a bare `yyyy-MM-dd` string —
 * confirmed against `TeamDirectoryRow.hireDate`, `EmployeeAssignment.startDate`/`endDate`, and
 * `EmployeeSow.startDate`/`endDate`. Issue #234: that shape was being rendered verbatim, so every
 * Compass screen showed dates as yyyy-mm-dd instead of the mm/dd/yyyy the rest of the product uses.
 */
describe('formatDate', () => {
  it('formats a yyyy-MM-dd string as MM/dd/yyyy', () => {
    expect(formatDate('2023-06-30')).toBe('06/30/2023');
  });

  it('keeps leading zeros on single-digit months and days', () => {
    expect(formatDate('2024-01-05')).toBe('01/05/2024');
  });

  it('does not shift the date across a timezone boundary', () => {
    // The regression this guards against: `new Date('2023-06-30')` parses as UTC midnight, which
    // renders as 2023-06-29 in any timezone behind UTC. Parsing the string directly avoids the Date
    // constructor entirely, so there is no timezone to shift across.
    expect(formatDate('2023-01-01')).toBe('01/01/2023');
    expect(formatDate('2023-12-31')).toBe('12/31/2023');
  });

  it('uses only the date portion when a timestamp is passed', () => {
    // A blind `split('-')` produced `03/14T00:00:00/2016` here. The wire contract is a bare
    // `DateOnly`, but a timestamp must degrade to its calendar date rather than to garbage.
    expect(formatDate('2016-03-14T00:00:00')).toBe('03/14/2016');
  });

  it('returns an unrecognised value unchanged rather than fabricating a date (#234)', () => {
    // A blank or malformed value is the server telling us it has no date. Splitting it on `-`
    // rendered `undefined/undefined/undefined`; the honest response is to show what we were given.
    expect(formatDate('')).toBe('');
    expect(formatDate('not a date')).toBe('not a date');
    expect(formatDate('14/03/2016')).toBe('14/03/2016');
  });
});

describe('formatOptionalDate', () => {
  it('formats a present date exactly as formatDate does', () => {
    // Moved into the library from AvailabilityReportPage in #306. It must stay a thin wrapper: a
    // second formatting rule hiding behind the null check is how two screens start disagreeing.
    expect(formatOptionalDate('2024-04-01')).toBe(formatDate('2024-04-01'));
    expect(formatOptionalDate('2024-04-01')).toBe('04/01/2024');
  });

  it('renders an absent date as empty, never as today', () => {
    // Absent is a real answer in Compass: an assignment with no end date, an EDJEr never rolled off.
    // The Availability Report is read for triage, so defaulting a missing SOW end date to the
    // business date would render "expires today" -- the most urgent row the screen can show -- for
    // data it does not have.
    expect(formatOptionalDate(null)).toBe('');
  });

  it('passes an unrecognised value through, inheriting formatDate’s honesty', () => {
    expect(formatOptionalDate('not a date')).toBe('not a date');
  });
});

describe('formatDateTime', () => {
  it('renders a timestamp in the business timezone, not the viewer’s', () => {
    // Pinned to America/New_York so two people looking at the same record see the same moment. This
    // instant is 14:30 UTC, which is 10:30 Eastern in daylight time.
    expect(formatDateTime('2024-06-30T14:30:00Z')).toBe('06/30/2024, 10:30 AM');
  });

  it('applies the Eastern offset even when that changes the calendar date', () => {
    // 01:15 UTC on the 1st is 21:15 Eastern on the PREVIOUS day. A formatter that ignored the zone
    // would report the wrong date, which is the failure mode formatDate avoids by never parsing.
    expect(formatDateTime('2024-07-01T01:15:00Z')).toBe('06/30/2024, 9:15 PM');
  });

  it('falls back to the raw string when the runtime cannot format at all', () => {
    // The catch guards an environment whose Intl data lacks the America/New_York zone -- a slim
    // runtime, not a malformed input. Unreachable through arguments, so the runtime is stubbed;
    // the same approach covers the same branch in web/timesheet's date.test.ts.
    const original = Date.prototype.toLocaleString;
    Date.prototype.toLocaleString = () => {
      throw new RangeError('unsupported time zone');
    };

    try {
      expect(formatDateTime('2024-06-30T14:30:00Z')).toBe('2024-06-30T14:30:00Z');
    } finally {
      Date.prototype.toLocaleString = original;
    }
  });

  it('returns an unparseable value unchanged rather than the literal Invalid Date', () => {
    // `toLocaleString` returns "Invalid Date" WITHOUT throwing, so a malformed timestamp would reach
    // the screen looking like a rendered value rather than like missing data.
    expect(formatDateTime('')).toBe('');
    expect(formatDateTime('not a timestamp')).toBe('not a timestamp');
  });
});
