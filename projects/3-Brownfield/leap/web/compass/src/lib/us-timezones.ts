/** One timezone option: what the form shows, and what it submits. */
export interface UsTimeZone {
  /** The IANA identifier — what `compass.employee.timezone` holds. */
  id: string;
  /** The zone's everyday name — what a person reads in the select. */
  label: string;
}

/**
 * The six US timezones an EDJEr may be recorded in, IANA-valued (FR-8.1, FR-8.1b).
 *
 * **`api/Modules/Compass/UsTimeZones.cs` is the authority, not this file.** That array is what
 * `CompassEmployeeService` validates a submitted zone against, and the column stores the id verbatim.
 * This is the presentation half of the same vocabulary: labels because "Eastern" is what a human reads,
 * ids because an IANA identifier is what OOTO does date arithmetic with. `us-timezones.test.ts` reads
 * that C# array back and diffs the two sets — the same technique `us-states.test.ts` uses, and for the
 * same reason: a drift is a 400 on a value the form itself offered.
 *
 * **Exactly six, and no worldwide IANA list — that is the requirement, not a shortcut.** FR-8.1b says
 * opening the dropdown shows only these. So there is also no "keep an unrecognised stored value as an
 * option" escape hatch here, unlike the employee-type and coach selects on the same form: those exist
 * because a lookup can be retired out from under a record, whereas an offered seventh option would
 * contradict the acceptance criterion outright. A record somehow carrying a zone outside these six
 * therefore renders as unchosen, and the administrator must pick one — visible, and correctable.
 *
 * **US-only *at this time*, and the list is data-driven so it can widen without a schema change.**
 * `compass.employee.timezone` carries no CHECK constraint for exactly that reason. Widening means
 * adding an entry here AND to `UsTimeZones.All`; adding it to only one produces the drift above.
 *
 * Ordered east to west, then the two non-contiguous zones — the order `UsTimeZones.All` declares and
 * the one OOTO lists them in.
 */
export const US_TIMEZONES: readonly UsTimeZone[] = [
  { id: 'America/New_York', label: 'Eastern' },
  { id: 'America/Chicago', label: 'Central' },
  { id: 'America/Denver', label: 'Mountain' },
  { id: 'America/Los_Angeles', label: 'Pacific' },
  { id: 'Pacific/Honolulu', label: 'Hawaiian' },
  { id: 'America/Anchorage', label: 'Alaskan' },
];
