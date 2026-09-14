/**
 * The columns `EdjerListPage` allows sorting by (issue #666), labelled the way its `EdjerRow` labels
 * them.
 *
 * Lives here rather than in the `.tsx` file for the same reason Team Directory's own
 * `SORTABLE_COLUMNS` does (`team-directory/types.ts`): each entry opens its own line with `{`, which
 * `no-raw-dates.test.ts` reads as multi-line JSX when it sits in a `.tsx` file — that gate only
 * scans `.tsx`, so a `.ts` column-configuration array is invisible to it by construction.
 *
 * The `key` names a `CompassEdjerSummary` field; `sortEdjers` (in `EdjerListPage.tsx`) is the only
 * reader.
 */
export type EdjerSortColumn =
  | 'firstName'
  | 'lastName'
  | 'email'
  | 'hireDate'
  | 'employeeTypeName'
  | 'coachName'
  | 'stateOfResidence'
  | 'isActive';

export const SORTABLE_COLUMNS: { key: EdjerSortColumn; label: string }[] = [
  { key: 'firstName', label: 'First Name' },
  { key: 'lastName', label: 'Last Name' },
  { key: 'email', label: 'Email' },
  { key: 'hireDate', label: 'Hire Date' },
  { key: 'employeeTypeName', label: 'Type' },
  { key: 'coachName', label: 'Coach' },
  { key: 'stateOfResidence', label: 'State' },
  { key: 'isActive', label: 'Status' },
];

/**
 * The column sorted by default — hire date, oldest first (issue #666). Matches Team Directory's own
 * default (`DEFAULT_SORT_COLUMN` in `team-directory/types.ts`, itself `CompassReadRepository.Sort`'s
 * fallback), so the two screens open on the same ordering.
 */
export const DEFAULT_SORT_COLUMN: EdjerSortColumn = 'hireDate';
