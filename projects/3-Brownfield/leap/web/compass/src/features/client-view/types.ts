import type { AssignmentStatus, ClientStatus } from '../../lib/status';
/**
 * The client view (AC-13 to AC-16), mirroring the server's `ClientViewDto`.
 *
 * `clientName` sits at the top level rather than inside `clientDetails`, and that placement IS
 * AC-13: the details panel is hidden from four of the five tiers, so a name nested inside it would
 * leave those viewers on a nameless page.
 */
export interface ClientView {
  id: number;
  clientName: string;
  /**
   * The CLIENT's own derived status — `Active`, `Inactive` or `Former` (issue #274).
   *
   * Narrowed from a bare `string` while #274 was widening the vocabulary, which is the moment it is
   * cheapest to get right. Not the same field as {@link ClientAssignmentHistoryRow.status} below,
   * which is one assignment's currency and cannot be `Former`.
   */
  status: ClientStatus;
  /**
   * Served to every tier, unlike the rest of {@link ClientDetails} (issue #243). An internal client
   * has no contracts, so this decides whether the assignment history's SOW column renders at all —
   * a client-side conditional, not a server entitlement, which is why it cannot wait for
   * `clientDetails` the way the rest of the panel does.
   */
  isInternal: boolean;
  assignmentHistory: ClientAssignmentHistoryRow[];
  /** Compass Super Admin only (AC-14). Absent means withheld. */
  clientDetails?: ClientDetails;
}

/** One EDJEr's engagement with this client (AC-15). */
export interface ClientAssignmentHistoryRow {
  /** The assignment's own id — links a history row into its detail screen (AC-2, mockup screen 5). */
  assignmentId: number;
  /**
   * `'Active'` or `'Inactive'` — whether THIS ASSIGNMENT is current, as the server derived it (AC-24).
   *
   * **Typed as {@link AssignmentStatus}, not as the client vocabulary** (issue #274). It was the client
   * union until #274 widened that to three values, at which point this row would have silently started
   * admitting `'Former'` — a word the server never sends here, because `Former` describes a client's
   * history rather than one engagement. The mistyping was harmless while both unions read the same; it
   * stopped being harmless the moment one of them grew.
   *
   * **Not the same thing as {@link employeeIsActive}**, which is the EDJEr's stored flag: a departed
   * EDJEr can hold an assignment that is still current. Always present, because unlike its two optional
   * neighbours it is the row's own state rather than a disclosure the server may withhold.
   *
   * **Rendered, never recomputed.** A screen must display this rather than compare the two dates below —
   * the server owns the business date and the inclusive end-date boundary, which is the comparison AC-42
   * names as the one independent implementations get wrong. Typing it narrows what may arrive; it does
   * not decide it, and mapping it to different words in the browser would be a second derivation.
   */
  status: AssignmentStatus;
  employeeId: number;
  employeeName: string;
  startDate: string;
  endDate: string | null;
  /** Elevated tiers only — a baseline viewer's rows are all active by construction. */
  employeeIsActive?: boolean;
  /** Elevated tiers only (AC-16). Absent means the server did not grant the affordance. */
  canViewSow?: boolean;
}

/** Client configuration (AC-14, Super Admin only). */
export interface ClientDetails {
  msaSignedDate: string | null;
  ndaSignedDate: string | null;
  isInternal: boolean;
  invoiceFrequency: string | null;
  billableTimeCategories: string[];
}
