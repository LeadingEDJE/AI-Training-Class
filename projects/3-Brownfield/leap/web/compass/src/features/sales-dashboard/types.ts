import type { ReportLoad } from '../../lib/report-load';

/** The four tile counts plus the date they were computed against, mirroring `SalesDashboardDto`. */
export interface SalesDashboardData {
  asOfDate: string;
  /** Unit: SOWs — every SOW at a non-internal client is counted, including more than one per assignment. */
  activeSowCount: number;
  /** Unit: SOWs (FR-003). */
  expiringSowCount: number;
  /** Unit: EDJErs (FR-004). */
  confirmedRolloutCount: number;
  /** Unit: EDJErs (FR-005). */
  beachCount: number;
}

/** One client named by a breakdown row, mirroring `DashboardBreakdownClientDto`. */
export interface DashboardBreakdownClient {
  id: number;
  name: string;
}

/** One drill-down row, in the ONE shape shared across all four categories, mirroring `DashboardBreakdownRowDto`. */
export interface DashboardBreakdownRow {
  employeeId: number;
  employeeName: string;
  employeeType: string;
  clients: DashboardBreakdownClient[];
  date: string | null;
  daysUntil: number | null;
  /**
   * The `beach` category's counterpart to `daysUntil` — `date` minus today, i.e. days remaining
   * until the internal assignment starts. Null for the other three categories.
   */
  daysAvailable: number | null;
  coachName: string | null;
  coachId: number | null;
  startDate: string | null;
}

/**
 * The outcome of a dashboard read.
 *
 * A refusal and a failure are rendered identically here — both show "the dashboard could not be
 * loaded" — since `CompassReporting` admits every Compass role including Admin, so the refused
 * state is never actually reachable in practice.
 */
export type DashboardLoad<T> = ReportLoad<T>;

/** The four route segments `GET /api/compass/dashboard/breakdown/{category}` accepts. */
export type DashboardCategory = 'active-sows' | 'expiring-sows' | 'confirmed-rollouts' | 'beach';

export type BreakdownSortColumn =
  | 'employee'
  | 'employeeType'
  | 'client'
  | 'date'
  | 'startDate'
  | 'daysUntil'
  | 'daysAvailable'
  | 'coach';
