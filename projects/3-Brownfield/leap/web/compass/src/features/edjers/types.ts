/**
 * The columns `EdjerListPage` allows sorting by (issue #666), labelled the way its `EdjerRow` labels
 * them.
 *
 * Lives here rather than in the `.tsx` file only for import-order convenience — `no-raw-dates.test.ts`
 * scans `.ts` and `.tsx` files equally, so the split has no effect on that gate.
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

export const DEFAULT_SORT_COLUMN: EdjerSortColumn = 'hireDate';
