/**
 * One row of the Client Assignment Duration report, mirroring `AssignmentDurationRowDto`.
 *
 * **The grain is the EDJEr–client PAIR, not the assignment** (research D-4, FR-015). An EDJEr who left
 * a client and returned is one row carrying their combined tenure.
 */
export interface AssignmentDurationRow {
  employeeId: number;
  employeeName: string;
  /**
   * The EDJEr's employee type (e.g. "Full Time"), rendered as its own column following
   * {@link employeeName} (issue #386) — the same treatment the Availability Report and Assignment
   * Start report give it. Null when the wire omits it.
   */
  employeeType: string | null;
  clientId: number;
  clientName: string;
  /** Null when the EDJEr has no coach — the row is present regardless (spec Edge Cases, FR-013). */
  coachName: string | null;
  /**
   * Total tenure in whole days.
   *
   * **The sort key.** Never sort on {@link durationDisplay}: lexically "10 yrs" precedes "3 yrs",
   * which is wrong and looks fine (FR-030).
   */
  totalDays: number;
  /** The mockup's rendering of {@link totalDays}, e.g. `"3 yrs 4 mos (1,238 days)"`. */
  durationDisplay: string;
}

/** Which column orders the table. `duration` is the default (FR-014, longest first). */
export type DurationSortColumn = 'employee' | 'client' | 'coach' | 'duration';
