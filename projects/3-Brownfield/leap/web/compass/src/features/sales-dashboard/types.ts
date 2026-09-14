import type { ReportLoad } from '../../lib/report-load';

/** The four tile counts plus the date they were computed against, mirroring `SalesDashboardDto`. */
export interface SalesDashboardData {
  asOfDate: string;
  /**
   * Unit: client assignments, not SOWs (issue #633) — every current, started assignment at a
   * non-internal client, counted once regardless of how many SOWs (or none) it carries.
   */
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
  /**
   * The EDJEr's employee type, exactly as defined in the system (e.g. "Full Time", "Part Time",
   * "1099") — issue #332. Always populated, but rendered only on the three grids where an EDJEr's
   * type is relevant; the beach grid has no column for it (a 1099 EDJEr is never on the beach).
   */
  employeeType: string;
  /**
   * Every client this row is about — a LIST, because the confirmed-rollouts and beach rows are one
   * per EDJEr and an EDJEr can hold two assignments tied on the date that keys the category. Always
   * exactly one entry for the per-assignment and per-SOW categories.
   */
  clients: DashboardBreakdownClient[];
  date: string | null;
  daysUntil: number | null;
  /**
   * The `beach` category's counterpart to `daysUntil` — today minus `date` (the internal-assignment
   * start date). Null for the other three categories (issue #329).
   */
  daysAvailable: number | null;
  /** Null when the EDJEr has no coach — the row is present regardless (FR-008). */
  coachName: string | null;
  /**
   * The coach's own id — the drill-in into their record (issue #330). `null` exactly when
   * `coachName` is, so the coach cell links only when it has both.
   */
  coachId: number | null;
  /**
   * The client assignment's own start date, for the `active-sows` category only; null for the other
   * three (issue #459, #633 — that breakdown shows the assignment's start alongside `date`'s max SOW
   * end date).
   */
  startDate: string | null;
}

/**
 * The outcome of a dashboard read.
 *
 * **A refusal and a failure are distinct states.** `CompassReporting` admits Sales, Ops and the
 * Compass root but NOT Compass Admin (FR-019), and the nav hides the screen from everyone it
 * excludes — but a deep link or a bookmark does not, so the refused state is reachable. Rendering
 * "the dashboard could not be loaded" for a 403 tells that viewer the screen is broken when it is
 * working exactly as specified. Mirrors `EdjerLoad` in `features/edjers/edjer-api.ts`, which records
 * the same reasoning for the same reason.
 */
export type DashboardLoad<T> = ReportLoad<T>;

/** The four route segments `GET /api/compass/dashboard/breakdown/{category}` accepts. */
export type DashboardCategory = 'active-sows' | 'expiring-sows' | 'confirmed-rollouts' | 'beach';

/**
 * Which column orders a sortable breakdown grid — now ALL FOUR grids (issues #464 beach, #462
 * expiring SOWs, #463 confirmed rollouts, #459 active SOWs).
 *
 * **Keyed on the ROW FIELD, not on a header label**, because the same field wears a different label
 * per category: `date` is "Assignment Start Date" on the beach grid, "SOW End Date" on expiring SOWs
 * and confirmed rollouts, and it is one field across them
 * ({@link DashboardBreakdownRow.date} — the date that KEYS the category). One vocabulary across the
 * grids is what lets a single comparator serve them.
 *
 * **The `active-sows` grid sorts on all six of its columns** (issue #459). It is the only grid that
 * renders BOTH ends of the SOW, so it needs a key for the SOW start date as well as `date` (the SOW
 * end): that is why `startDate` exists here. `startDate` is the one field no other sortable grid
 * renders — `active-sows` is its sole consumer.
 */
export type BreakdownSortColumn =
  | 'employee'
  | 'employeeType'
  | 'client'
  | 'date'
  | 'startDate'
  | 'daysUntil'
  | 'daysAvailable'
  | 'coach';
