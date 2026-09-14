/**
 * One Team Directory row, mirroring the server's `TeamDirectoryRowDto` (AC-5).
 *
 * `isActive` is optional because the server OMITS it for a baseline viewer: their result set is
 * all-active by construction, so the field carries no information for them. That is the wire
 * contract, not a convenience — FR-005 requires withheld data to be absent rather than falsy.
 */
export interface TeamDirectoryRow {
  id: number;
  firstName: string;
  lastName: string;
  hireDate: string;
  /**
   * Still on the wire, no longer a directory column (owner request 2026-08-18) — the EDJEr's name
   * is the drill-in instead (2026-08-19). Kept because `EmployeeDetailPage` renders it and the two
   * share the server's row shape.
   */
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
 * The columns AC-6 allows sorting by, labelled as `docs/design/edje-compass-mockups.html` labels them.
 *
 * The `key` is the value the SERVER's `sort` parameter takes and must keep matching
 * `CompassReadRepository.Sort`; the `label` is only what a viewer reads. They differ on purpose for
 * employee type, where the mockup's header is the single word "Type".
 */
export const SORTABLE_COLUMNS = [
  { key: 'firstName', label: 'First Name' },
  { key: 'lastName', label: 'Last Name' },
  { key: 'hireDate', label: 'Hire Date' },
  // No `email` entry (owner request 2026-08-18, superseding the mockup's own fourth column). The
  // address is still on the wire and still on the EDJEr's own record — it is the DIRECTORY that no
  // longer shows it, so `TeamDirectoryRow.email` stays. The server still accepts `sort=email`; there
  // is simply no header offering it now.
  { key: 'employeeType', label: 'Type' },
  { key: 'coach', label: 'Coach' },
  { key: 'state', label: 'State' },
  // Feature 008 FR-010. Sorting is SERVER-side, so this key must keep matching
  // `CompassReadRepository.Sort`'s `"currentclients"` branch — a key the server does not recognise
  // falls through to hire date and looks like it worked.
  { key: 'currentClients', label: 'Current Client(s)' },
] as const;

/**
 * The employee-type breakdown line's own order (issue #624, owner request) — Full Time → Part Time
 * → 1099 → Intern, with Unknown always trailing. Deliberately NOT alphabetical, and deliberately NOT
 * shared with `employeeTypeOptions` — the dropdown filter stays alphabetical by the same owner's
 * request, so this list has exactly one consumer: `countByType` in `TeamDirectoryRoute`.
 */
export const EMPLOYEE_TYPE_COUNT_ORDER = ['Full Time', 'Part Time', '1099', 'Intern'] as const;

/**
 * The column the server orders by when none is asked for.
 *
 * The default is the SERVER's (`CompassReadRepository.Sort` falls through to hire date), and the screen
 * has to name it to draw the sort indicator on the right column before anyone has clicked anything.
 * Sending it explicitly instead would be the obvious alternative and is worse: it makes the first
 * request differ from the unfiltered one the filter options come from, so the two stop sharing a cache
 * entry and the screen opens with two identical requests in flight.
 */
export const DEFAULT_SORT_COLUMN = 'hireDate';
