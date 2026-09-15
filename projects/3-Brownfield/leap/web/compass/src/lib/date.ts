/** Matches the leading `yyyy-MM-dd` of an ISO value, whether or not a time follows it. */
const ISO_DATE = /^(\d{4})-(\d{2})-(\d{2})/;

/** Formats a `yyyy-MM-dd` date string as `MM/dd/yyyy`, per the display spec in `web/compass/README.md`. */
export function formatDate(isoDate: string): string {
  const parts = ISO_DATE.exec(isoDate);
  return parts ? `${parts[2]}/${parts[3]}/${parts[1]}` : isoDate;
}

/** Formats a nullable date, rendering `N/A` where there is no date to show. */
export function formatOptionalDate(isoDate: string | null): string {
  return isoDate ? formatDate(isoDate) : '';
}

/** Formats an ISO timestamp as `MM/dd/yyyy, h:mm AM/PM`, rendered in the viewer's own local timezone. */
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
