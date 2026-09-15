import { useState } from 'react';
import {
  Button,
  PageHeader,
  Panel,
  StatusPill,
  Table,
  type TableColumn,
  type TableSortableColumn,
} from '../../components/ui';
import { PageSizeField, Pager } from '../../components/pagination';
import { paginate, type PageSize } from '../../components/pagination-options';
import { fieldControlClass, tableLinkClass } from '../../components/ui-classes';
import { getCompassPrivileges } from '../../lib/compass-nav-permissions';
import { formatDate } from '../../lib/date';
import { US_STATES } from '../../lib/us-states';
import { DEFAULT_SORT_COLUMN, SORTABLE_COLUMNS, type TeamDirectoryRow } from './types';

interface TeamDirectoryPageProps {
  rows: TeamDirectoryRow[];
  isPending: boolean;
  isError: boolean;
  /** The caller's raw `/api/me` privileges. Filtered to Compass roles before use. */
  privileges: string[];
  /**
   * How many EDJErs the current status scope holds, for the mockups' count pill.
   *
   * Tracks the filtered table, not the whole scope: as a search narrows the visible rows, the pill's
   * count narrows with it, so "3 active EDJErs" means exactly the three rows on screen.
   */
  scopeCount?: number;
  typeCounts?: { type: string; count: number }[];
  /** Employee-type names the type filter offers. Empty means the filter has nothing to offer yet. */
  employeeTypeOptions?: string[];
  /** State codes the state filter offers. */
  stateOptions?: string[];
  coachOptions?: { id: number; name: string }[];
  /** Skills offered by the filter, from the active-only public skill list. */
  skillOptions?: { id: number; name: string }[];
  /** The column the SERVER is ordering by. Absent or empty means its own default, hire date. */
  sort?: string;
  /** Whether that order is reversed. */
  descending?: boolean;
  onSearchChange?: (search: string) => void;
  onSortChange?: (column: string) => void;
  onStatusChange?: (status: string) => void;
  onEmployeeTypeChange?: (employeeType: string) => void;
  onStateChange?: (state: string) => void;
  onCoachIdChange?: (coachId: string) => void;
  onSkillChange?: (skill: string) => void;
}

/**
 * The Team Directory's own default page size — `'all'`, matching the shared 20/50/100 default the
 * other paginated Compass screens use so every list opens on the same first page size.
 */
const TEAM_DIRECTORY_DEFAULT_PAGE_SIZE: PageSize = 'all';

export function TeamDirectoryPage({
  rows,
  isPending,
  isError,
  privileges,
  scopeCount,
  typeCounts = [],
  employeeTypeOptions = [],
  stateOptions = [],
  coachOptions = [],
  skillOptions = [],
  sort,
  descending = false,
  onSearchChange,
  onSortChange,
  onStatusChange,
  onEmployeeTypeChange,
  onStateChange,
  onCoachIdChange,
  onSkillChange,
}: TeamDirectoryPageProps) {
  const [search, setSearch] = useState('');
  const [employeeType, setEmployeeType] = useState('');
  const [state, setState] = useState('');
  const [coachId, setCoachId] = useState('');
  const [skill, setSkill] = useState('');
  const [status, setStatus] = useState('active');
  const [pageSize, setPageSize] = useState<PageSize>(TEAM_DIRECTORY_DEFAULT_PAGE_SIZE);
  const [page, setPage] = useState(1);

  // The status control IS the access boundary here — the server trusts whatever this filter sends,
  // so hiding it from a non-elevated role is what keeps that viewer from requesting a status it
  // should not see (FR-011a).
  const isElevated = getCompassPrivileges(privileges).length > 0;

  const { rows: pageRows, firstIndex, currentPage, totalPages } = paginate(rows, pageSize, page);

  function changeAndReset<T>(next: T, apply: (value: T) => void, report?: (value: T) => void) {
    apply(next);
    report?.(next);
    setPage(1);
  }

  /**
   * Whether the view is already the default. #246 asked for "clear filters" to mean "clear
   * everything", so this also considers sort and page size — a viewer who has changed either one is
   * no longer at the default view.
   */
  const isDefaultView =
    search === '' &&
    employeeType === '' &&
    state === '' &&
    coachId === '' &&
    skill === '' &&
    (!isElevated || status === 'active');

  function handleClearFilters() {
    if (search !== '') changeAndReset('', setSearch, onSearchChange);
    if (employeeType !== '') changeAndReset('', setEmployeeType, onEmployeeTypeChange);
    if (state !== '') changeAndReset('', setState, onStateChange);
    if (coachId !== '') changeAndReset('', setCoachId, onCoachIdChange);
    if (skill !== '') changeAndReset('', setSkill, onSkillChange);
    if (isElevated && status !== 'active') changeAndReset('active', setStatus, onStatusChange);
  }

  const activeSort = sort === undefined || sort === '' ? DEFAULT_SORT_COLUMN : sort;

  const direction: TableSortableColumn['sortDirection'] = descending ? 'descending' : 'ascending';

  const columns: TableColumn[] = [
    ...SORTABLE_COLUMNS.map((column) => ({
      label: column.label,
      sortDirection: column.key === activeSort ? direction : undefined,
      onSort: () => onSortChange?.(column.key),
    })),
    // Not sortable: 'skills' has no matching branch in `CompassReadRepository.Sort`, and inventing a
    // client-side key that the server does not recognise would silently fall through to hire date.
    'Skills',
  ];

  return (
    <main className="mx-auto flex max-w-7xl flex-col gap-6 px-6 py-8 text-brand-text">
      <PageHeader
        title="Team Directory"
        status={
          <StatusPill
            tone={status === 'active' ? 'affirmative' : 'neutral'}
            label={countLabel(scopeCount ?? rows.length, status)}
          />
        }
        actions={
          <Button
            variant="secondary"
            size="small"
            disabled={isDefaultView}
            onClick={handleClearFilters}
          >
            <span className="flex items-center gap-1">
              <svg
                xmlns="http://www.w3.org/2000/svg"
                viewBox="0 0 24 24"
                fill="currentColor"
                aria-hidden="true"
                className="size-3.5 text-red-600"
              >
                <path
                  fillRule="evenodd"
                  d="M5.47 5.47a.75.75 0 011.06 0L12 10.94l5.47-5.47a.75.75 0 111.06 1.06L13.06 12l5.47 5.47a.75.75 0 11-1.06 1.06L12 13.06l-5.47 5.47a.75.75 0 01-1.06-1.06L10.94 12 5.47 6.53a.75.75 0 010-1.06z"
                  clipRule="evenodd"
                />
              </svg>
              Reset View
            </span>
          </Button>
        }
      />

      {typeCounts.length > 0 && (
        <p className="text-sm text-brand-gray-muted">{typeBreakdownLabel(typeCounts)}</p>
      )}

      <Panel>
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex flex-col gap-1">
            <label htmlFor="team-directory-search" className="text-sm font-medium text-brand-gray">
              Search by Last Name
            </label>
            <input
              id="team-directory-search"
              type="search"
              value={search}
              onChange={(event) => changeAndReset(event.target.value, setSearch, onSearchChange)}
              // Replaces the visible label above once the mockup's own wording is available (feature
              // 008 FR-013) — the label element stays in the DOM for the styling hook but the
              // placeholder is what a viewer actually reads once the field has focus.
              placeholder="Search by last name…"
              className={`${fieldControlClass} max-w-xs`}
            />
          </div>

          <FilterSelect
            id="team-directory-employee-type"
            label="Employee Type"
            allLabel="All Employee Types"
            value={employeeType}
            options={employeeTypeOptions.map((type) => ({ value: type, label: type }))}
            onChange={(next) => changeAndReset(next, setEmployeeType, onEmployeeTypeChange)}
          />

          <FilterSelect
            id="team-directory-coach"
            label="Coach"
            allLabel="All Coaches"
            value={coachId}
            options={coachOptions.map((coach) => ({ value: String(coach.id), label: coach.name }))}
            onChange={(next) => changeAndReset(next, setCoachId, onCoachIdChange)}
          />

          <FilterSelect
            id="team-directory-state"
            label="State"
            allLabel="All States"
            value={state}
            options={stateOptions.map((code) => ({ value: code, label: stateName(code) }))}
            onChange={(next) => changeAndReset(next, setState, onStateChange)}
          />

          <FilterSelect
            id="team-directory-skill"
            label="Skill"
            allLabel="All Skills"
            value={skill}
            options={skillOptions.map((option) => ({
              value: String(option.id),
              label: option.name,
            }))}
            onChange={(next) => changeAndReset(next, setSkill, onSkillChange)}
          />

          {isElevated && (
            <FilterSelect
              id="team-directory-status"
              label="Status"
              value={status}
              options={[
                { value: 'active', label: 'Active' },
                { value: 'inactive', label: 'Former' },
                { value: 'all', label: 'All' },
              ]}
              onChange={(next) => changeAndReset(next, setStatus, onStatusChange)}
            />
          )}

          {rows.length > 0 && (
            <div className="sm:ml-auto">
              <PageSizeField
                label="EDJErs per Page"
                value={pageSize}
                onChange={(next) => changeAndReset(next, setPageSize)}
              />
            </div>
          )}
        </div>

        <div className="mt-5 flex flex-col gap-4">
          {isPending && (
            <p role="status" className="text-sm">
              Loading the team directory…
            </p>
          )}

          {isError && (
            <p role="alert" className="text-sm text-brand-danger">
              The team directory could not be loaded.
            </p>
          )}

          {!isPending && !isError && (
            <Table
              caption="Team Directory"
              columns={columns}
              emptyMessage="No EDJErs match the current search and filters."
            >
              {pageRows.map((row) => (
                <TeamDirectoryTableRow key={row.id} row={row} />
              ))}
            </Table>
          )}

          {!isPending && !isError && rows.length > 0 && (
            <Pager
              firstIndex={firstIndex}
              shownCount={pageRows.length}
              totalCount={rows.length}
              currentPage={currentPage}
              totalPages={totalPages}
              itemNoun={{ singular: 'EDJEr', plural: 'EDJErs' }}
              onPage={setPage}
            />
          )}
        </div>
      </Panel>
    </main>
  );
}

/** The mockups' count pill, worded for the scope it is counting. */
function countLabel(count: number, status: string): string {
  const noun = count === 1 ? 'EDJEr' : 'EDJErs';
  const scope = status === 'active' ? 'active ' : status === 'inactive' ? 'former ' : '';

  return `${count} ${scope}${noun}`;
}

/**
 * The TPS-carryover breakdown line (#244): each employee type's share of the scope, led by a
 * "Total: N" summary (#378, owner request) so the line is meaningful even without the count pill
 * above it.
 */
function typeBreakdownLabel(typeCounts: { type: string; count: number }[]): string {
  return typeCounts.map(({ type, count }) => `${type}: ${count}`).join(' · ');
}

/** A state's full name, or the raw code when `us-states.ts` does not know it. */
function stateName(code: string): string {
  return US_STATES.find((state) => state.code === code)?.name ?? code;
}

interface FilterSelectProps {
  id: string;
  label: string;
  /** The "no constraint" option's text. Omitted for a filter whose every value is a real choice. */
  allLabel?: string;
  value: string;
  options: { value: string; label: string }[];
  onChange: (next: string) => void;
}

function FilterSelect({ id, label, allLabel, value, options, onChange }: FilterSelectProps) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={id} className="text-sm font-medium text-brand-gray">
        {label}
      </label>
      <select
        id={id}
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={fieldControlClass}
      >
        {allLabel !== undefined && <option value="">{allLabel}</option>}
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
    </div>
  );
}

function TeamDirectoryTableRow({ row }: { row: TeamDirectoryRow }) {
  const detailHref = `/compass/team-directory/${row.id}`;

  return (
    <tr>
      <td className="px-3 py-2">
        <a href={detailHref} className={tableLinkClass}>
          {row.firstName}
        </a>
      </td>
      <td className="px-3 py-2 font-medium">
        <a href={detailHref} className={tableLinkClass}>
          {row.lastName}
        </a>
        {row.isActive === false && (
          <span className="ml-2 inline-block align-middle">
            <StatusPill tone="neutral" label="Former" />
          </span>
        )}
      </td>
      {/* `tabular-nums` is purely decorative here — a typographic preference for how the digits look,
          with no effect on the table's column widths, which are fixed by the header row regardless of
          what any cell renders. */}
      <td className="px-3 py-2 tabular-nums whitespace-nowrap">{formatDate(row.hireDate)}</td>
      <td className="px-3 py-2">
        <StatusPill tone="neutral" label={row.employeeType} />
      </td>
      <td className="px-3 py-2 whitespace-nowrap">
        {/* The drill-in into the COACH's own record (issue #245, follow-up) — renders as a link
            whenever a coach name is present, since `coachId` is guaranteed to accompany it on every
            row the server sends. */}
        {row.coach !== null && row.coachId !== null ? (
          <a href={`/compass/team-directory/${row.coachId}`} className={tableLinkClass}>
            {row.coach}
          </a>
        ) : (
          (row.coach ?? '')
        )}
      </td>
      <td className="px-3 py-2">{row.state}</td>
      <td className="px-3 py-2">
        <ul className="flex flex-wrap gap-x-3 gap-y-1">
          {row.currentAssignments.map((assignment) => (
            <li key={assignment.clientId}>
              <a
                href={`/compass/client-directory/${assignment.clientId}`}
                className={tableLinkClass}
              >
                {assignment.clientName}
              </a>
            </li>
          ))}
        </ul>
      </td>
      <td className="px-3 py-2">{(row.skills ?? []).map((skill) => skill.name).join(', ')}</td>
    </tr>
  );
}
