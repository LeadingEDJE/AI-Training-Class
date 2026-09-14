/** One Client Directory row (AC-12), mirroring the server's `ClientDirectoryRowDto`. */
export interface ClientDirectoryRow {
  id: number;
  clientName: string;
  /**
   * `Active`, `Inactive` or `Former` — **closed and total** (AC-42, extended by issue #274). Never
   * blank and never null, for any client including one that has never been assigned. It is that
   * TOTALITY, not the number of values, that lets the column sort.
   *
   * Left as `string` rather than narrowed to {@link ClientStatus}: this row is rendered straight
   * through, and #274's lesson was that a union asserted in the wrong place is worse than no union —
   * see `features/client-view/types.ts`, where the assignment row carried the client union for months.
   */
  status: string;
}
