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
   * The whole scope, NOT the filtered table: the pill reports the directory, so a search that narrows
   * the table must not move it — a pill that falls to "3 active EDJErs" while someone types reads as
   * the directory having shrunk. Defaults to the rows in hand, which is correct when nothing is
   * filtering them.
   */
  scopeCount?: number;
  /**
   * The scope's `scopeCount`, broken down by employee type — TPS carryover (#244): a quick glance at
   * "Full Time: 69, 1099: 4, Part Time: 3" without opening the type filter. Empty (the default) hides
   * the breakdown, which covers both "not resolved yet" and a scope with nothing in it.
   */
  typeCounts?: { type: string; count: number }[];
  /** Employee-type names the type filter offers. Empty means the filter has nothing to offer yet. */
  employeeTypeOptions?: string[];
  /** State codes the state filter offers. */
  stateOptions?: string[];
  /**
   * Coaches the coach filter offers (issue #655), each an id paired with the display name it links
   * to. By id rather than name — two coaches can share a display name, and the row this filters
   * against already carries `coachId` for the same reason.
   */
  coachOptions?: { id: number; name: string }[];
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
}

/**
 * The Team Directory (AC-5, AC-6, AC-7), laid out as `docs/design/edje-compass-mockups.html` screen 2.
 *
 * **What the mockup contributes, and where it is overruled.** Structure is the mockup's: the count pill
 * beside the title, the search row's input and selects, and its column order. Three departures from it
 * are the owner's, not liberties: the Email column is gone, the drill-in to AC-7's destination is the
 * EDJEr's own name rather than a trailing action column (2026-08-19, superseding the trailing View
 * action of 2026-08-18) — the same mechanism `ClientDirectoryPage` already uses for a client's name —
 * and the Coach select (issue #655), which the mockup has no equivalent for at all.
 * Colour is not: its blue `a.link` is 2.92:1 on white and its `.pill.green` is 4.33:1, so links wear
 * `tableLinkClass` and the pill uses the measured tint — the mockup's own banner says an acceptance
 * criterion beats it, and AC-NFR-5 is one. Its `.note` paragraph of policy prose is scaffolding for
 * the BA and is not reproduced.
 *
 * **These links were the baseline for `tableLinkClass`** (owner request 2026-08-21): the run declared
 * inline here is now shared, with the hover green moved one step darker for AA. See the helper.
 *
 * **Pagination is not in the mockup at all** — it is the owner's request, and it is a view over rows the
 * server already chose. Search, filters, sort and the status scope all travel to the SERVER (FR-021,
 * research R-7: there is exactly one filtering model and it lives in the query), so a search covers the
 * entire directory and comes back as a fresh first page rather than filtering the twenty rows on
 * screen. That is the whole reason paging can be local while filtering cannot.
 *
 * Otherwise presentational: it renders what the server sent and filters nothing itself. The server
 * already applied the viewer's entitlement (FR-005, FR-021), so any filtering here would either
 * duplicate that or — worse — imply the browser holds rows it was not entitled to.
 */
/**
 * The Team Directory's own default page size — `'all'`, not the shared 20/50/100 default the other
 * paginated Compass screens use.
 *
 * Issue #247 asked for the default to move from 20 to 100 because the active roster is under 100 and
 * a viewer scanning the whole directory otherwise has to change the page size every time. Landing on
 * `'all'` outright covers that same request without needing to revisit it once headcount crosses 100 —
 * and pagination still works normally the moment a viewer picks a smaller size themselves.
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
  sort,
  descending = false,
  onSearchChange,
  onSortChange,
  onStatusChange,
  onEmployeeTypeChange,
  onStateChange,
  onCoachIdChange,
}: TeamDirectoryPageProps) {
  const [search, setSearch] = useState('');
  const [employeeType, setEmployeeType] = useState('');
  const [state, setState] = useState('');
  const [coachId, setCoachId] = useState('');
  const [status, setStatus] = useState('active');
  const [pageSize, setPageSize] = useState<PageSize>(TEAM_DIRECTORY_DEFAULT_PAGE_SIZE);
  const [page, setPage] = useState(1);

  // The status control is offered only to elevated roles. This is a CONVENIENCE, not the access
  // control — the server applies the entitlement clause regardless of what any filter asks for
  // (FR-011a). Compass roles only: Compass inherits nothing, and DevBypass carries all nine
  // timesheet strings.
  const isElevated = getCompassPrivileges(privileges).length > 0;

  const { rows: pageRows, firstIndex, currentPage, totalPages } = paginate(rows, pageSize, page);

  /**
   * Every control that changes WHICH rows are being looked at returns to the first page.
   *
   * `paginate` already clamps, so this is not what prevents an empty page — it is what stops a viewer
   * who searches from landing on page 3 of the new results with no idea the first two exist.
   */
  function changeAndReset<T>(next: T, apply: (value: T) => void, report?: (value: T) => void) {
    apply(next);
    report?.(next);
    setPage(1);
  }

  /**
   * Whether the view is already the default (no search, no dropdown filters, and — for an elevated
   * viewer — the status back at "Active"). Sort and page size are deliberately NOT part of this: #246
   * asked for "clear filters", not "clear everything", so a chosen sort or page size survives a reset.
   */
  const isDefaultView =
    search === '' &&
    employeeType === '' &&
    state === '' &&
    coachId === '' &&
    (!isElevated || status === 'active');

  /**
   * Resets search, every dropdown filter, and — for an elevated viewer — status back to "Active"
   * (#246). Only touches a control that has actually moved, so a caller only sees the callbacks for
   * filters that changed rather than a burst of no-op requests.
   */
  function handleClearFilters() {
    if (search !== '') changeAndReset('', setSearch, onSearchChange);
    if (employeeType !== '') changeAndReset('', setEmployeeType, onEmployeeTypeChange);
    if (state !== '') changeAndReset('', setState, onStateChange);
    if (coachId !== '') changeAndReset('', setCoachId, onCoachIdChange);
    if (isElevated && status !== 'active') changeAndReset('active', setStatus, onStatusChange);
  }

  const activeSort = sort === undefined || sort === '' ? DEFAULT_SORT_COLUMN : sort;

  const direction: TableSortableColumn['sortDirection'] = descending ? 'descending' : 'ascending';

  // Every column sorts, `Current Client(s)` included (TD-3; Journey Map v6 J2 step 4).
  //
  // That column was previously excluded for a stated and accurate reason — the server orders by
  // columns of `compass.employee`, and a row's clients are a projected collection with no single
  // value to order by. Feature 008 answered the reason rather than ignoring it: the server now orders
  // by the alphabetically-first CURRENT client, reusing the same `isCurrent` predicate the cell is
  // rendered from, with unassigned EDJErs last. See `CompassReadRepository.Sort`.
  const columns: TableColumn[] = SORTABLE_COLUMNS.map((column) => ({
    label: column.label,
    sortDirection: column.key === activeSort ? direction : undefined,
    onSort: () => onSortChange?.(column.key),
  }));

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

      {/* TPS carryover (#244): the mockups have no equivalent, so this is placed where a viewer's eye
          already lands for the pill it extends, rather than inside `PageHeader`'s `description` — that
          prop is one line of static copy about what the page is for, not a value that moves with the
          status filter. Hidden until the scope read resolves, same as `typeCounts`' default covers. */}
      {typeCounts.length > 0 && (
        <p className="text-sm text-brand-gray-muted">{typeBreakdownLabel(typeCounts)}</p>
      )}

      {/* The mockups' `.card`. `Panel` rather than `Card` because that one requires a heading, and the
          only honest heading here is "Team Directory" — which the `h1` above already is. This screen
          hand-rolled the class run until five other screens needed the same sheet; see `Panel`. */}
      <Panel>
        {/* `.searchrow`. Rendered before the loading and error states so the controls do not appear
            only once rows arrive, which would shift the layout under a viewer already reaching for
            the search box. */}
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex flex-col gap-1">
            <label htmlFor="team-directory-search" className="text-sm font-medium text-brand-gray">
              Search by Last Name
            </label>
            <input
              id="team-directory-search"
              // type="search" gives the input the searchbox role and the browser's clear affordance.
              type="search"
              value={search}
              onChange={(event) => changeAndReset(event.target.value, setSearch, onSearchChange)}
              // The mockup's placeholder, ADDED ALONGSIDE the visible label rather than instead of it
              // (feature 008 FR-013). `#s-directory` labels this control with a placeholder only, and
              // a placeholder is not a label: it vanishes the moment anything is typed, so a viewer
              // who looks away mid-search has nothing left telling them what the field is. Rule 2
              // outranks rule 3, so the label stays and the mockup's wording joins it.
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

          {/* `sm:ml-auto` pushes the page size to the trailing edge only once there is room for it. At
              375px the row has already wrapped, and `ml-auto` there leaves the control marooned on the
              right with a hole beside it. */}
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
 * The TPS-carryover breakdown line (#244): each employee type's share of the scope.
 *
 * No leading "Total: N" (#378, owner request) — the count pill right above this line already
 * states the scope's total ("N active EDJErs"), so repeating it here read as redundant.
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

/**
 * One of the search row's selects.
 *
 * The unconstrained option's value is the EMPTY STRING, which `buildTeamDirectoryQuery` then omits
 * from the query — so "All Employee Types" sends no `employeeType` parameter rather than sending a
 * sentinel the server would have to know about.
 *
 * The label is visible, where the mockup relies on the first option ("All Employee Types") to say what
 * the select is for. That reads fine until the filter is set, at which point a select reading
 * "Part Time" beside one reading "Ohio" has nothing naming either.
 */
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
  // The drill-in to AC-7's destination, carried by the EDJEr's own name rather than a trailing
  // action column (owner request 2026-08-19). Both cells link rather than one, because "first
  // name" and "last name" are two separate `<td>`s — splitting the link would leave half the name
  // inert, which reads as broken rather than as a deliberate boundary.
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
        {/* Status is only ever present for elevated viewers, and AC-15 requires it to be
            distinguishable — as text, not colour alone. */}
        {row.isActive === false && (
          <span className="ml-2 inline-block align-middle">
            <StatusPill tone="neutral" label="Former" />
          </span>
        )}
      </td>
      {/* `tabular-nums` so every date occupies the SAME width whatever digits it holds.
          Two reasons, one typographic and one mechanical.

          Typographic: a date column should align down the page, and proportional figures — which
          Manrope uses by default — leave the slashes and year ragged.

          Mechanical: the table is auto-layout, so this column's width is derived from its widest
          value. With proportional figures, changing 08/14/2017 to 08/17/2017 changes that width, the
          browser re-apportions every column, and EVERY column to the right of this one shifts
          horizontally. Measured: a 1px narrower date moved the column beside it 2px, which mismatched
          five columns of text and failed the screenshot gate at 5% of all pixels against a 1%
          tolerance. That measurement is HISTORICAL — issue #574 deleted the gate on 2026-09-08, and
          `TeamDirectoryPage.test.tsx` is now what holds this class.
          It surfaced because the seeded hire dates are `today - tenure`
          (`CompassDirectorySeeder.GeneratedHireDate`), so the rendered dates change with the calendar
          and a baseline captured on another day no longer lines up. Tabular figures make the width
          independent of the value, so the layout is stable across days. */}
      <td className="px-3 py-2 tabular-nums whitespace-nowrap">{formatDate(row.hireDate)}</td>
      <td className="px-3 py-2">
        {/* The mockups' `.pill.gray`. A category rather than a state, but the same visual element, and
            StatusPill is the one that cannot be built colour-only. */}
        <StatusPill tone="neutral" label={row.employeeType} />
      </td>
      {/* A person's name is not a place to line-break. Widening the table is the safe trade here: it
          scrolls inside its own container, so the cost is a scrollbar on a narrow screen rather than
          "Sarah-Jane / McAllister" splitting every second row to double height. */}
      <td className="px-3 py-2 whitespace-nowrap">
        {/* The drill-in into the COACH's own record (issue #245, follow-up) — the same mechanism as
            the EDJEr's own name, guarded on both `coach` and `coachId` since a link with no
            destination is worse than plain text. */}
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
        {/* Every current assignment, not just the first (AC-5, FR-008). A list rather than a comma-
            separated run of anchors, so a screen reader announces how many there are. */}
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
    </tr>
  );
}
