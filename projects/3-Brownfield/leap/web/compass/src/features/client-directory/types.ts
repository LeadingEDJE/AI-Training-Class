/** One Client Directory row (AC-12), mirroring the server's `ClientDirectoryRowDto`. */
export interface ClientDirectoryRow {
  id: number;
  clientName: string;
  /**
   * `Active`, `Inactive` or `Former`. May be blank for a client that has never been assigned, since
   * the server only computes a status once at least one assignment exists.
   *
   * Narrowed to {@link ClientStatus} before rendering, matching `features/client-view/types.ts`'s
   * assignment row.
   */
  status: string;
}
