/**
 * The read-only employee detail (AC-8), mirroring the server's `EmployeeDetailDto`.
 *
 * **Every optional field here is a disclosure decision, not a convenience.** The server OMITS what
 * the viewer is not entitled to, so `undefined` means "withheld", never "empty". The page must
 * therefore branch on presence — rendering a section with blank values would tell the viewer a
 * section exists that they cannot see.
 */
export interface EmployeeDetail {
  id: number;
  firstName: string;
  lastName: string;
  hireDate: string;
  /**
   * Still on the wire, no longer displayed on this page (issue #245) — kept because
   * `CompassEmployeeDetailEndpointsTests` and the DTO it mirrors still carry it.
   */
  email: string;
  employeeType: string;
  /** The coach's own id — the drill-in into their record (issue #245). `null` exactly when `coach` is. */
  coachId: number | null;
  coach: string | null;
  state: string;
  /**
   * Whether this EDJEr is on the delivery team (issue #502).
   *
   * Required, unlike `isActive` below and `timeTrackingSettings` at the foot of this interface. Those
   * are optional because the server withholds them from a tier that is not entitled to them; this one
   * is published to every tier that can reach the record at all, so an absent value would mean the
   * payload is wrong rather than the viewer is unentitled.
   */
  isDeliveryTeam: boolean;
  /** Elevated tiers only. */
  isActive?: boolean;
  assignmentHistory: EmployeeAssignment[];
  /**
   * Every EDJEr who lists this one as their coach (issue #245) — the org-chart direction opposite
   * `coachId`. Always present, empty rather than absent when this EDJEr coaches no one: an absence
   * of data, not a disclosure withholding, so it does not need presence-branching the way
   * `timeTrackingSettings` does.
   */
  directReports: DirectReport[];
  /** Compass Super Admin only (AC-10). */
  timeTrackingSettings?: TimeTrackingSettings;
}

/** One EDJEr who lists this record's subject as their coach (issue #245). */
export interface DirectReport {
  id: number;
  firstName: string;
  lastName: string;
  /**
   * Elevated tiers only (issue #399) — lets a viewer who still sees former direct reports (issue
   * #245) tell them apart from current ones. Omitted, not `false`, for a baseline viewer, whose
   * direct reports are all-active already.
   */
  isActive?: boolean;
}

/**
 * A derived assignment status, as the server publishes it (BR-7).
 *
 * Re-exported from `lib/status`, where it sits beside the three-valued client vocabulary it must not
 * be confused with — an assignment has no `Former` state (issue #274).
 */
export type { AssignmentStatus } from '../../lib/status';

// Imported as well as re-exported — see the note in `features/clients/client-api.ts`.
import type { AssignmentStatus } from '../../lib/status';

/** One assignment in the EDJEr's history (AC-8, AC-20). */
export interface EmployeeAssignment {
  /**
   * The assignment's own id — links a history row into its detail screen (AC-2, mockup screen 5).
   *
   * Not the client's: an EDJEr can be assigned to the same client more than once over time, so
   * `clientId` does not identify a row.
   */
  assignmentId: number;
  /** Derived from the assignment dates by the server (AC-20). */
  status: AssignmentStatus;
  clientId: number;
  clientName: string;
  /**
   * Whether THIS ROW's client is an internal EDJE ("beach") client (issue #518).
   *
   * Internal work has no statements of work, so the row's "View SOWs" link would lead to a screen
   * with no such section. Decided per row, not per record: an EDJEr's history mixes a beach
   * allocation with real engagements, so the column stays and only that row's link goes.
   *
   * Always present, unlike `canViewAssignment` — a fact about the client, not an entitlement the
   * server may withhold.
   */
  isInternal: boolean;
  startDate: string;
  endDate: string | null;
  /** Elevated tiers only (AC-11) — withheld even on the viewer's own record. */
  note?: string;
  /** Elevated tiers, or a baseline viewer on their own record (AC-11). */
  sows?: EmployeeSow[];
  /** Whether the viewer may open this assignment's own detail screen — elevated tiers only (AC-16/FR-025). */
  canViewAssignment?: boolean;
}

/** A contract on an assignment (AC-11). */
export interface EmployeeSow {
  sowType: string;
  startDate: string;
  endDate: string;
  /** Elevated tiers only. */
  rateIncrease?: boolean;
  /** Elevated tiers only. */
  note?: string;
}

/** The three time-tracking flags (AC-10). */
export interface TimeTrackingSettings {
  timesheetRequired: boolean;
  canSubmitUnder40: boolean;
  includeInPayroll: boolean;
}
