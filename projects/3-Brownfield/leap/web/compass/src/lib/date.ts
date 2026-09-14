/** Matches the leading `yyyy-MM-dd` of an ISO value, whether or not a time follows it. */
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})/;

/**
 * Formats a `yyyy-MM-dd` date string — the wire shape for every Compass `DateOnly` field — as
 * `MM/dd/yyyy` for display (issue #234).
 *
 * Matches the leading date with a regex rather than through `Date`: `new Date('2023-06-30')` parses
 * as UTC midnight, which renders as the previous day in any timezone behind UTC. Reading the digits
 * out of the string avoids the `Date` constructor entirely, so there is no timezone to shift across.
 *
 * An unrecognised value comes back UNCHANGED rather than as a fabricated date: a blind `split('-')`
 * rendered `undefined/undefined/undefined` for a blank or malformed value and `03/14T00:00:00/2016`
 * for a timestamp. The wire contract is a bare `DateOnly`, so neither is reachable today, but the
 * honest response to something this function does not model is to show what it was given.
 */
export function formatDate(isoDate: string): string {
  const parts = ISO_DATE.exec(isoDate);
  return parts ? `${parts[2]}/${parts[3]}/${parts[1]}` : isoDate;
}

/**
 * Formats a nullable date, rendering an empty string where there is no date to show.
 *
 * Many Compass reads model "not applicable" as `null` — an assignment with no end date, an EDJEr
 * never rolled off. The empty string is deliberate: a placeholder such as `—` or `N/A` is a design
 * decision that belongs to the screen, and a caller wanting one can supply it, whereas a caller that
 * gets one it did not ask for has to strip it back out.
 *
 * Lived in `features/reports/availability/AvailabilityReportPage.tsx` until issue #306, where it was
 * a private helper that three other screens would each have re-invented.
 */
export function formatOptionalDate(isoDate: string | null): string {
  return isoDate ? formatDate(isoDate) : '';
}

/**
 * Formats an ISO timestamp as `MM/dd/yyyy, h:mm AM/PM` for display (issue #234).
 *
 * Unlike {@link formatDate}, a timestamp genuinely represents a moment in time, so this DOES go
 * through `Date` and IS timezone-aware — pinned to `America/New_York`, the business timezone Compass
 * derives its day from (`ICompassBusinessDate` server-side), rather than the viewer's local
 * timezone, so two people looking at the same record see the same time regardless of where they are.
 *
 * Falls back to the raw string for an unparseable value, so a malformed timestamp degrades to
 * visible raw text rather than to the literal `Invalid Date` — which `toLocaleString` returns
 * WITHOUT throwing — or to a crash in the component that renders it.
 *
 * Ported from `web/timesheet/src/lib/date.ts` in issue #306. Compass has no timestamp display yet;
 * its audit surfaces will, and the alternative is somebody hand-rolling `toLocaleString` and
 * silently getting the viewer's timezone instead of the business one.
 */
export function formatDateTime(isoDateTime: string): string {
  const parsed = new Date(isoDateTime);
  if (Number.isNaN(parsed.getTime())) {
    return isoDateTime;
  }

  try {
    return parsed.toLocaleString('en-US', {
      timeZone: 'America/New_York',
      month: '2-digit',
      day: '2-digit',
      year: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
      hour12: true,
    });
  } catch {
    return isoDateTime;
  }
}
