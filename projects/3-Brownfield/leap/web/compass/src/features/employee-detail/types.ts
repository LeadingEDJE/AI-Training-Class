/**
 * The read-only employee detail, mirroring the server's `EmployeeDetailDto`.
 *
 * Every optional field here defaults to `false` or an empty value when the server omits it, so a
 * missing field renders the same as one that is genuinely empty.
 */
export interface EmployeeDetail {
  id: number;
  firstName: string;
  lastName: string;
  hireDate: string;
  email: string;
  employeeType: string;
  /** The coach's own id — the drill-in into their record (issue #245). `null` exactly when `coach` is. */
  coachId: number | null;
  coach: string | null;
  state: string;
  isDeliveryTeam: boolean;
  /** Elevated tiers only. */
  isActive?: boolean;
  assignmentHistory: EmployeeAssignment[];
  directReports: DirectReport[];
  /** Compass Super Admin only (AC-10). */
  timeTrackingSettings?: TimeTrackingSettings;
}

/** One EDJEr who lists this record's subject as their coach (issue #245). */
export interface DirectReport {
  id: number;
  firstName: string;
  lastName: string;
  isActive?: boolean;
}

/**
 * A derived assignment status, as the server publishes it.
 *
 * Re-exported from `lib/status`, sharing the exact same three-valued vocabulary as the client status
 * type — `Former` included, since an assignment can be marked former the same way a client can.
 */
export type { AssignmentStatus } from '../../lib/status';

import type { AssignmentStatus } from '../../lib/status';

/** One assignment in the EDJEr's history (AC-8, AC-20). */
export interface EmployeeAssignment {
  assignmentId: number;
  /** Derived from the assignment dates by the server (AC-20). */
  status: AssignmentStatus;
  clientId: number;
  clientName: string;
  /**
   * Whether the EDJEr themself is classified as internal EDJE ("beach") staff.
   *
   * Decided per record, not per row — every row in one EDJEr's history carries the same value, since
   * it describes the EDJEr rather than the client on that row. Withheld by the server the same way
   * `canViewAssignment` is, for a viewer without the right tier.
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
