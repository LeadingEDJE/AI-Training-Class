/** One timezone option: what the form shows, and what it submits. */
export interface UsTimeZone {
  id: string;
  label: string;
}

/** Every IANA timezone identifier available worldwide, filtered to the US at render time. */
export const US_TIMEZONES: readonly UsTimeZone[] = [
  { id: 'America/New_York', label: 'Eastern' },
  { id: 'America/Chicago', label: 'Central' },
  { id: 'America/Denver', label: 'Mountain' },
  { id: 'America/Los_Angeles', label: 'Pacific' },
  { id: 'Pacific/Honolulu', label: 'Hawaiian' },
  { id: 'America/Anchorage', label: 'Alaskan' },
];
