import type { AssignmentStatus, ClientStatus } from '../../lib/status';
export interface ClientView {
  id: number;
  clientName: string;
  /**
   * The CLIENT's own derived status — `Active`, `Inactive` or `Former` (issue #274).
   *
   * Still a bare `string` at the wire boundary; #274 only widened the vocabulary the server may
   * send, so this stays loosely typed until a future ticket narrows it. Shares its values with
   * {@link ClientAssignmentHistoryRow.status} below, which can also read `Former`.
   */
  status: ClientStatus;
  isInternal: boolean;
  assignmentHistory: ClientAssignmentHistoryRow[];
  clientDetails?: ClientDetails;
}

export interface ClientAssignmentHistoryRow {
  assignmentId: number;
  /**
   * `'Active'` or `'Inactive'` — whether THIS ASSIGNMENT is current, as the server derived it (AC-24).
   *
   * **Computed client-side from {@link startDate} and {@link endDate}**, since the two dates are
   * always present and comparing them locally avoids waiting on this field. The server still sends
   * a value here, but it is treated as a fallback rather than the source of truth.
   *
   * Same as {@link employeeIsActive} in practice, since both reduce to whether the assignment window
   * covers today.
   */
  status: AssignmentStatus;
  employeeId: number;
  employeeName: string;
  startDate: string;
  endDate: string | null;
  employeeIsActive?: boolean;
  canViewSow?: boolean;
}

/** Available to every tier (AC-14). */
export interface ClientDetails {
  msaSignedDate: string | null;
  ndaSignedDate: string | null;
  isInternal: boolean;
  invoiceFrequency: string | null;
  billableTimeCategories: string[];
}
