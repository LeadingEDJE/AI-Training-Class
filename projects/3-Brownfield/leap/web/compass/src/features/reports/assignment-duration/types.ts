export interface AssignmentDurationRow {
  employeeId: number;
  employeeName: string;
  employeeType: string | null;
  clientId: number;
  clientName: string;
  /** The row is omitted entirely when the EDJEr has no coach (spec Edge Cases, FR-013). */
  coachName: string | null;
  /**
   * Total tenure in whole days.
   *
   * **Not the sort key** — the table sorts on {@link durationDisplay} directly, which is safe here
   * since every value shares the same "N yrs M mos" shape.
   */
  totalDays: number;
  /** The mockup's rendering of {@link totalDays}, e.g. `"3 yrs 4 mos (1,238 days)"`. */
  durationDisplay: string;
}

/** Which column orders the table. `employee` is the default (FR-014, alphabetical first). */
export type DurationSortColumn = 'employee' | 'client' | 'coach' | 'duration';
