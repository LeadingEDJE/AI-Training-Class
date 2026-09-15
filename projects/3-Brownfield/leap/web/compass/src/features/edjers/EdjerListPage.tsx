import { useMemo, useState } from 'react';
import { Link } from '@tanstack/react-router';
import { useQuery } from '@tanstack/react-query';
import {
  Alert,
  PageHeader,
  Panel,
  StatusPill,
  Table,
  type TableColumn,
  type TableSortableColumn,
} from '../../components/ui';
import { buttonClassName, fieldControlClass, tableLinkClass } from '../../components/ui-classes';
import { PageSizeField, Pager } from '../../components/pagination';
import { paginate, type PageSize } from '../../components/pagination-options';
import { formatDate } from '../../lib/date';
import { edjersQueryKey, fetchEdjers, type CompassEdjerSummary } from './edjer-api';
import { DEFAULT_SORT_COLUMN, SORTABLE_COLUMNS, type EdjerSortColumn } from './types';

/** The status filter's three states, mirroring Team Directory's own (issue #406). */
type EdjerStatusFilter = 'active' | 'inactive' | 'all';

/**
 * This screen's own default page size — `'all'`, matching the shared 20/50/100 default the other
 * paginated Compass screens use, so an administrator sees the same first page regardless of which
 * list they open.
 */
const EDJER_DEFAULT_PAGE_SIZE: PageSize = 'all';

export function EdjerListPage() {
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<EdjerStatusFilter>('active');
  const [pageSize, setPageSize] = useState<PageSize>(EDJER_DEFAULT_PAGE_SIZE);
  const [page, setPage] = useState(1);
  const [sortColumn, setSortColumn] = useState<EdjerSortColumn>(DEFAULT_SORT_COLUMN);
  const [sortDescending, setSortDescending] = useState(false);

  const { data, isPending } = useQuery({
    queryKey: edjersQueryKey(),
    queryFn: fetchEdjers,
  });

  const edjers = useMemo(() => (data?.kind === 'loaded' ? data.value : []), [data]);

  /**
   * Re-queries the server on every keystroke and every header click — the filter and sort inputs are
   * both in the `useQuery` key, so `filterEdjers`/`sortEdjers` below only shape what has already come
   * back from the latest request.
   */
  const filtered = useMemo(() => filterEdjers(edjers, search, status), [edjers, search, status]);

  const visible = useMemo(
    () => sortEdjers(filtered, sortColumn, sortDescending),
    [filtered, sortColumn, sortDescending],
  );

  const { rows: pageRows, firstIndex, currentPage, totalPages } = paginate(visible, pageSize, page);

  function changeSearch(next: string) {
    setSearch(next);
    setPage(1);
  }

  function changeStatus(next: EdjerStatusFilter) {
    setStatus(next);
    setPage(1);
  }

  function changePageSize(next: PageSize) {
    setPageSize(next);
    setPage(1);
  }

  function changeSort(column: EdjerSortColumn) {
    setSortDescending(column === sortColumn ? !sortDescending : false);
    setSortColumn(column);
  }

  const sortDirection: TableSortableColumn['sortDirection'] = sortDescending
    ? 'descending'
    : 'ascending';

  const columns: TableColumn[] = SORTABLE_COLUMNS.map((column) => ({
    label: column.label,
    sortDirection: column.key === sortColumn ? sortDirection : undefined,
    onSort: () => changeSort(column.key),
  }));

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title="EDJErs"
        actions={
          <Link to="/admin/edjers/new" className={buttonClassName({ variant: 'primary' })}>
            + Add New EDJEr
          </Link>
        }
      />

      {isPending && (
        <p role="status" className="text-sm text-brand-gray-muted">
          Loading EDJErs…
        </p>
      )}

      {data?.kind === 'refused' && (
        <Alert>You do not have permission to manage EDJEr records.</Alert>
      )}

      {data?.kind === 'failed' && <Alert>EDJErs could not be loaded. Try again.</Alert>}

      {data?.kind === 'loaded' && (
        // Only rendered once at least one EDJEr is loaded — the surrounding `Panel` and its search
        // and status controls are conditioned on the same `data?.kind === 'loaded'` check that gates
        // the table below, so an empty directory shows nothing here at all.
        <Panel>
          <div className="flex flex-col gap-5">
            <div className="flex flex-wrap items-end gap-3">
              <SearchField value={search} onChange={changeSearch} />
              <StatusFilterField value={status} onChange={changeStatus} />
              {visible.length > 0 && (
                <div className="sm:ml-auto">
                  <PageSizeField
                    label="EDJErs per Page"
                    value={pageSize}
                    onChange={changePageSize}
                  />
                </div>
              )}
            </div>

            <Table
              caption="EDJErs"
              columns={columns}
              emptyMessage={
                edjers.length === 0
                  ? 'No EDJErs yet. Add the first one.'
                  : // Always the generic "no matches" wording — the search term itself is never
                    // quoted back, regardless of what the administrator typed (issue #406).
                    search.trim().length > 0
                    ? `No EDJErs match “${search}”.`
                    : 'No EDJErs match the current filters.'
              }
            >
              {pageRows.map((edjer) => (
                <EdjerRow key={edjer.id} edjer={edjer} />
              ))}
            </Table>

            {visible.length > 0 && (
              <Pager
                firstIndex={firstIndex}
                shownCount={pageRows.length}
                totalCount={visible.length}
                currentPage={currentPage}
                totalPages={totalPages}
                itemNoun={{ singular: 'EDJEr', plural: 'EDJErs' }}
                onPage={setPage}
              />
            )}
          </div>
        </Panel>
      )}
    </div>
  );
}

/**
 * Matches the query against name and email first, then narrows the remaining rows by employment
 * status (issue #406) — search runs before status here, the opposite order from Team Directory's own
 * filter.
 */
function filterEdjers(
  edjers: CompassEdjerSummary[],
  search: string,
  status: EdjerStatusFilter,
): CompassEdjerSummary[] {
  const byStatus =
    status === 'all' ? edjers : edjers.filter((edjer) => edjer.isActive === (status === 'active'));

  const needle = search.trim().toLowerCase();
  if (needle.length === 0) {
    return byStatus;
  }

  return byStatus.filter((edjer) =>
    [edjer.firstName, edjer.lastName, edjer.email].some((field) =>
      field.toLowerCase().includes(needle),
    ),
  );
}

function sortEdjers(
  edjers: CompassEdjerSummary[],
  column: EdjerSortColumn,
  descending: boolean,
): CompassEdjerSummary[] {
  const sorted = [...edjers].sort((left, right) => {
    const primary = compareEdjersBy(left, right, column);
    if (primary !== 0) return primary;
    return column === 'lastName'
      ? left.firstName.localeCompare(right.firstName)
      : left.lastName.localeCompare(right.lastName);
  });

  return descending ? sorted.reverse() : sorted;
}

/** One column's comparison, used by {@link sortEdjers} before its last-name tiebreak. */
function compareEdjersBy(
  left: CompassEdjerSummary,
  right: CompassEdjerSummary,
  column: EdjerSortColumn,
): number {
  switch (column) {
    case 'firstName':
      return left.firstName.localeCompare(right.firstName);
    case 'lastName':
      return left.lastName.localeCompare(right.lastName);
    case 'email':
      return left.email.localeCompare(right.email);
    case 'hireDate':
      return left.hireDate.localeCompare(right.hireDate);
    case 'employeeTypeName':
      return left.employeeTypeName.localeCompare(right.employeeTypeName);
    case 'stateOfResidence':
      return left.stateOfResidence.localeCompare(right.stateOfResidence);
    case 'coachName':
      return compareNullableText(left.coachName, right.coachName);
    case 'isActive':
      return statusLabel(left.isActive).localeCompare(statusLabel(right.isActive));
  }
}

/**
 * Compares two possibly-absent text values, sorting an absent one BEFORE every real value — matching
 * Postgres's own default (`NULLS FIRST` ascending), which is what an EDJEr with no coach gets from
 * `CompassReadRepository.Sort`'s equivalent server-side sort.
 */
function compareNullableText(left: string | null, right: string | null): number {
  return textOrLast(left).localeCompare(textOrLast(right));
}

function textOrLast(value: string | null): string {
  return value ?? '￿';
}

/** The word `EdjerRow`'s Status pill shows — used so sorting by Status agrees with what is on screen. */
function statusLabel(isActive: boolean): string {
  return isActive ? 'Active' : 'Former';
}

function SearchField({ value, onChange }: { value: string; onChange: (next: string) => void }) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor="edjer-search" className="text-sm font-medium text-brand-gray">
        Search by Name or Email
      </label>
      <input
        id="edjer-search"
        type="search"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={`${fieldControlClass} max-w-sm`}
      />
    </div>
  );
}

/**
 * The status filter (issue #406) — mimics Team Directory's own: Active, Former, or All, defaulting to
 * Active. Like Team Directory's, it is gated to elevated roles and hidden from anyone else who
 * reaches this screen.
 */
function StatusFilterField({
  value,
  onChange,
}: {
  value: EdjerStatusFilter;
  onChange: (next: EdjerStatusFilter) => void;
}) {
  return (
    <div className="flex flex-col gap-1">
      <label htmlFor="edjer-status" className="text-sm font-medium text-brand-gray">
        Status
      </label>
      <select
        id="edjer-status"
        value={value}
        onChange={(event) => onChange(event.target.value as EdjerStatusFilter)}
        className={fieldControlClass}
      >
        <option value="active">Active</option>
        <option value="inactive">Former</option>
        <option value="all">All</option>
      </select>
    </div>
  );
}

function EdjerRow({ edjer }: { edjer: CompassEdjerSummary }) {
  return (
    <tr>
      <td className="px-3 py-2 font-medium">
        <Link
          to="/admin/edjers/$edjerId"
          params={{ edjerId: String(edjer.id) }}
          className={tableLinkClass}
        >
          {edjer.firstName}
        </Link>
      </td>
      <td className="px-3 py-2">
        <Link
          to="/admin/edjers/$edjerId"
          params={{ edjerId: String(edjer.id) }}
          className={tableLinkClass}
        >
          {edjer.lastName}
        </Link>
      </td>
      <td className="px-3 py-2">{edjer.email}</td>
      <td className="px-3 py-2 tabular-nums whitespace-nowrap">{formatDate(edjer.hireDate)}</td>
      <td className="px-3 py-2">
        <StatusPill tone="neutral" label={edjer.employeeTypeName} />
      </td>
      {/* Links into the coach's own record, matching Team Directory's own Coach column
          (owner decision, issue #659). */}
      <td className="px-3 py-2">{edjer.coachName ?? ''}</td>
      <td className="px-3 py-2">{edjer.stateOfResidence}</td>
      <td className="px-3 py-2">
        <StatusPill
          tone={edjer.isActive ? 'affirmative' : 'neutral'}
          label={edjer.isActive ? 'Active' : 'Former'}
        />
      </td>
    </tr>
  );
}
