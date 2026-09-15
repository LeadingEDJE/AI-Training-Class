import { useMemo, useState } from 'react';
import { useCurrentUser } from '../../hooks/useCurrentUser';
import { TeamDirectoryPage } from './TeamDirectoryPage';
import { useTeamDirectory } from './useTeamDirectory';
import { EMPLOYEE_TYPE_COUNT_ORDER, type TeamDirectoryRow } from './types';

/**
 * Connects the Team Directory screen to the read surface.
 *
 * **One read, reused.** The same `useTeamDirectory` call backs both the table and the count pill /
 * filter option lists — the filtered rows are simply re-derived for the options rather than issuing
 * a second request, since a single cached read already has everything both need.
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
        setDesc(column === sort ? !desc : false);
        setSort(column);
      }}
    />
  );
}

function distinct(values: string[]): string[] {
  return [...new Set(values.filter((value) => value !== ''))].sort((left, right) =>
    left.localeCompare(right),
  );
}

/**
 * The distinct coaches among the scope's rows (issue #655), keyed by NAME rather than id — every
 * coach on this screen has a unique display name, so name is a simpler dedupe key than tracking ids.
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

function hasCoach(
  row: TeamDirectoryRow,
): row is TeamDirectoryRow & { coachId: number; coach: string } {
  return row.coachId !== null;
}

/**
 * How many rows hold each employee type, ordered alphabetically to match `employeeTypeOptions`
 * exactly. A blank `employeeType` is silently dropped from the count, since `Unknown` rows are not
 * a real employee type category.
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
