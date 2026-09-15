import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useCurrentUser } from '../../hooks/useCurrentUser';
import { fetchSkillOptions, skillOptionsQueryKey } from '../skills/skill-api';
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
  const [skill, setSkill] = useState('');
  const [sort, setSort] = useState('');
  const [desc, setDesc] = useState(false);
  const [status, setStatus] = useState('active');

  const { data: user } = useCurrentUser();
  const { data, isPending, isError } = useTeamDirectory({
    search,
    employeeType,
    state,
    coachId,
    skill,
    sort,
    desc,
    status,
  });
  const scope = useTeamDirectory({ status });
  // The public, active-only endpoint (any authenticated viewer, no admin role) — distinct from the
  // admin-gated skill-api reads the Skills admin screen uses.
  const skillOptionsQuery = useQuery({
    queryKey: skillOptionsQueryKey(),
    queryFn: fetchSkillOptions,
  });

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
      skillOptions={skillOptionsQuery.data ?? []}
      sort={sort}
      descending={desc}
      onSearchChange={setSearch}
      onEmployeeTypeChange={setEmployeeType}
      onStateChange={setState}
      onCoachIdChange={setCoachId}
      onSkillChange={setSkill}
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
