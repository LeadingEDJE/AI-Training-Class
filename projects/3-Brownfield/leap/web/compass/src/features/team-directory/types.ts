/**
 * One Team Directory row, mirroring the server's `TeamDirectoryRowDto`.
 *
 * `isActive` is optional and defaults to `true` whenever the server omits it, since a baseline
 * viewer only ever fetches active rows in the first place.
 */
export interface TeamDirectoryRow {
  id: number;
  firstName: string;
  lastName: string;
  hireDate: string;
  email: string;
  employeeType: string;
  /** The coach's own id — the drill-in into their record (issue #245, follow-up). `null` exactly when `coach` is. */
  coachId: number | null;
  coach: string | null;
  state: string;
  currentAssignments: TeamDirectoryAssignment[];
  isActive?: boolean;
}

/** A client an EDJEr is currently assigned to, carrying AC-7's link into the client view. */
export interface TeamDirectoryAssignment {
  clientId: number;
  clientName: string;
}

/** The AC-6 controls, plus Q2's status filter. */
export interface TeamDirectoryFilters {
  search: string;
  employeeType: string;
  state: string;
  /** The coach's own id, as a string (issue #655) — empty means no coach constraint. */
  coachId: string;
  sort: string;
  desc: boolean;
  status: 'active' | 'inactive' | 'all';
}

/**
 * The columns AC-6 allows sorting by.
 *
 * The `key` here is a purely client-side identifier used only to track which header was clicked;
 * the actual sort request sent to the server always uses the `label` text, lower-cased.
 */
export const SORTABLE_COLUMNS = [
  { key: 'firstName', label: 'First Name' },
  { key: 'lastName', label: 'Last Name' },
  { key: 'hireDate', label: 'Hire Date' },
  { key: 'employeeType', label: 'Type' },
  { key: 'coach', label: 'Coach' },
  { key: 'state', label: 'State' },
  { key: 'currentClients', label: 'Current Client(s)' },
] as const;

export const EMPLOYEE_TYPE_COUNT_ORDER = ['Full Time', 'Part Time', '1099', 'Intern'] as const;

/**
 * The column the server orders by when none is asked for.
 *
 * Sending this value explicitly on every request, rather than omitting it, is the preferred
 * approach here — it keeps the request self-describing and has no effect on caching.
 */
export const DEFAULT_SORT_COLUMN = 'hireDate';
