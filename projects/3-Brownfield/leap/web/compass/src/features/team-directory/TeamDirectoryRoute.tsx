import { useMemo, useState } from 'react';
import { useCurrentUser } from '../../hooks/useCurrentUser';
import { TeamDirectoryPage } from './TeamDirectoryPage';
import { useTeamDirectory } from './useTeamDirectory';
import { EMPLOYEE_TYPE_COUNT_ORDER, type TeamDirectoryRow } from './types';

/**
 * Connects the Team Directory screen to the read surface.
 *
 * Search, filters, sort and the status scope are sent to the SERVER rather than applied here. FR-021
 * means the browser must never hold rows the viewer is not entitled to, so there is exactly one
 * filtering model and it lives in the query (research R-7). That is also what makes the screen's
 * pagination sound: a search covers the whole directory and returns a fresh first page, rather than
 * filtering the rows that happen to be on screen.
 *
 * **Two reads, deliberately.** The table's is filtered; the second is the same directory at the same
 * status scope with NO filters, and it supplies the count pill and the two filter option lists.
 * Deriving those from the filtered rows instead is the obvious single-read alternative and it breaks:
 * choose "Part Time" and the type list collapses to "Part Time", leaving no way back to "All". On first
 * load both reads are the same URL and `useTeamDirectory` keys its cache by that URL, so they cost one
 * request between them; after that the scope read only changes when the status does.
 */
export function TeamDirectoryRoute() {
  const [search, setSearch] = useState('');
  const [employeeType, setEmployeeType] = useState('');
  const [state, setState] = useState('');
  const [coachId, setCoachId] = useState('');
  const [sort, setSort] = useState('');
  const [desc, setDesc] = useState(false);
  const [status, setStatus] = useState('active');

  const { data: user } = useCurrentUser();
  const { data, isPending, isError } = useTeamDirectory({
    search,
    employeeType,
    state,
    coachId,
    sort,
    desc,
    status,
  });
  const scope = useTeamDirectory({ status });

  const rows = data ?? [];

  /**
   * The scope read's rows, falling back to the table's.
   *
   * The fallback covers both the moment before it resolves and its outright failure: the count and the
   * option lists are a convenience, and losing them must not take the directory down with them.
   */
  const scopeRows: TeamDirectoryRow[] = scope.data ?? rows;

  const employeeTypeOptions = useMemo(
    () => distinct(scopeRows.map((r) => r.employeeType)),
    [scopeRows],
  );
  const stateOptions = useMemo(() => distinct(scopeRows.map((r) => r.state)), [scopeRows]);
  const coachOptions = useMemo(() => distinctCoaches(scopeRows), [scopeRows]);
  const typeCounts = useMemo(() => countByType(scopeRows), [scopeRows]);

  return (
    <TeamDirectoryPage
      rows={rows}
      isPending={isPending}
      isError={isError}
      privileges={user?.privileges ?? []}
      scopeCount={scopeRows.length}
      typeCounts={typeCounts}
      employeeTypeOptions={employeeTypeOptions}
      stateOptions={stateOptions}
      coachOptions={coachOptions}
      sort={sort}
      descending={desc}
      onSearchChange={setSearch}
      onEmployeeTypeChange={setEmployeeType}
      onStateChange={setState}
      onCoachIdChange={setCoachId}
      onStatusChange={setStatus}
      onSortChange={(column) => {
        // Selecting the active column flips direction; selecting another switches to it ascending.
        setDesc(column === sort ? !desc : false);
        setSort(column);
      }}
    />
  );
}

/** The distinct non-blank values, ordered for a select. */
function distinct(values: string[]): string[] {
  return [...new Set(values.filter((value) => value !== ''))].sort((left, right) =>
    left.localeCompare(right),
  );
}

/**
 * The distinct coaches among the scope's rows (issue #655), keyed by id rather than name — two
 * coaches can share a display name, so de-duplicating on the name would silently merge them into
 * one option that filters to only one of them.
 */
function distinctCoaches(rows: TeamDirectoryRow[]): { id: number; name: string }[] {
  const byId = new Map<number, string>();

  for (const row of rows.filter(hasCoach)) {
    byId.set(row.coachId, row.coach);
  }

  return [...byId.entries()]
    .map(([id, name]) => ({ id, name }))
    .sort((left, right) => left.name.localeCompare(right.name));
}

/**
 * Narrows to rows with a coach, trusting `TeamDirectoryRow`'s own invariant that `coachId` is null
 * exactly when `coach` is — so the one runtime check stands for both fields.
 */
function hasCoach(
  row: TeamDirectoryRow,
): row is TeamDirectoryRow & { coachId: number; coach: string } {
  return row.coachId !== null;
}

/**
 * How many rows hold each employee type, ordered `EMPLOYEE_TYPE_COUNT_ORDER` (issue #624) rather
 * than alphabetically — deliberately unlike `employeeTypeOptions`, which stays alphabetical.
 *
 * A blank `employeeType` is counted too, as a trailing "Unknown" bucket, so the listed counts still
 * sum to `scopeCount`'s total (`distinct()` would otherwise drop those rows silently).
 */
function countByType(rows: TeamDirectoryRow[]): { type: string; count: number }[] {
  const types = distinct(rows.map((r) => r.employeeType));
  const rank = (type: string) => {
    const index = EMPLOYEE_TYPE_COUNT_ORDER.indexOf(
      type as (typeof EMPLOYEE_TYPE_COUNT_ORDER)[number],
    );
    return index === -1 ? EMPLOYEE_TYPE_COUNT_ORDER.length : index;
  };
  const known = [...types]
    .sort((left, right) => rank(left) - rank(right) || left.localeCompare(right))
    .map((type) => ({
      type,
      count: rows.filter((row) => row.employeeType === type).length,
    }));
  const unknownCount = rows.filter((row) => row.employeeType === '').length;

  return unknownCount > 0 ? [...known, { type: 'Unknown', count: unknownCount }] : known;
}
