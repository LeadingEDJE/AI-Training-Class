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

/**
 * EDJEr administration — find a record and open it (issue #59).
 *
 * **Deliberately kept consistent with the read-only Team Directory (issue #659).** The two screens
 * share their column set, their two-column name layout, and the Type pill — an administrator moving
 * between them should not have to re-learn the shape of the data. Two differences are deliberate,
 * not oversights: **Email stays** (the owner asked to keep it, reversing an earlier removal), and
 * **Coach is plain text, never a link** — unlike Team Directory's own Coach column, which drills into
 * the coach's record.
 *
 * Every column sorts, client-side (issue #666) — see `sortEdjers`. Still not the Team Directory
 * itself: no employee-type filter, and no "Current Client(s)" column (spec A-6, Stream 3's).
 *
 * **The Status filter is the one Team Directory affordance this screen mimics beyond its columns
 * (issue #406).** It defaults to Active, same as Team Directory's own status filter, with Former and
 * All alongside it — filtered in the browser next to the existing search, since the whole collection
 * is already in hand (see `filterEdjers`).
 *
 * It IS paginated, at the owner's request: 20 / 50 / 100 / All, defaulting to All (issue #548 — same
 * reasoning and same shape as Team Directory's own default, see {@link EDJER_DEFAULT_PAGE_SIZE}).
 *
 * Reaching this screen is not what authorises anything behind it. Every route requires the Compass root
 * policy server-side and a Compass Admin is refused even on the reads; the `admin-config` nav gate is a
 * convenience over that enforcement, never a substitute (AC-44, Principle IV).
 */
/** The status filter's three states, mirroring Team Directory's own (issue #406). */
type EdjerStatusFilter = 'active' | 'inactive' | 'all';

/**
 * This screen's own default page size — `'all'`, not the shared 20/50/100 default the other paginated
 * Compass screens use.
 *
 * Issue #548 asked for the default to be "All" rather than paging a roster that is usually well under
 * 100 rows. This is the same fix TeamDirectoryPage already made for the analogous issue #247 — a local
 * override constant rather than changing the shared `DEFAULT_PAGE_SIZE`, since Client and Lookup admin
 * lists still want the smaller default.
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

  /**
   * Memoised so it is a stable reference, not because the conditional is expensive.
   *
   * A fresh `[]` on every render would change `visible`'s dependency identity every time, defeating the
   * memo below — which `react-hooks/exhaustive-deps` says out loud rather than leaving to be discovered.
   */
  const edjers = useMemo(() => (data?.kind === 'loaded' ? data.value : []), [data]);

  /**
   * Filtered in the browser, not by re-querying.
   *
   * The whole collection is already in hand at this volume, so a request per keystroke would be slower
   * AND would need debouncing to avoid hammering the endpoint. `EdjerListPage.test.tsx` asserts no
   * request is made while typing.
   */
  const filtered = useMemo(() => filterEdjers(edjers, search, status), [edjers, search, status]);

  /**
   * Sorted in the browser too, for the same reason filtering is — the whole collection is already in
   * hand (issue #666). `EdjerListPage.test.tsx` asserts no request is made when a header is clicked.
   */
  const visible = useMemo(
    () => sortEdjers(filtered, sortColumn, sortDescending),
    [filtered, sortColumn, sortDescending],
  );

  // Paging is DERIVED, not stored — see `paginate` for why the requested page is clamped rather than
  // corrected in an effect.
  const { rows: pageRows, firstIndex, currentPage, totalPages } = paginate(visible, pageSize, page);

  /** Every control that changes what is being looked at also returns to the first page. */
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

  /**
   * Selecting the active column flips its direction; selecting another switches to it ascending —
   * the same convention `TeamDirectoryRoute`'s own `onSortChange` uses.
   */
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
          // A link rather than a button: it navigates, so it belongs in the tab order as a link and
          // supports open-in-new-tab like any other.
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
        // The white sheet the design source puts a list on. Its content used to sit directly on the
        // `#f4f5f6` shell, so the table's own header tint was the only thing separating it from the
        // page (owner request 2026-08-18).
        <Panel>
          <div className="flex flex-col gap-5">
            {/* Rendered even when the directory is empty, so the layout does not shift once it fills —
                the same reasoning AdminLayout applies to its own area navigation. */}
            <div className="flex flex-wrap items-end gap-3">
              <SearchField value={search} onChange={changeSearch} />
              <StatusFilterField value={status} onChange={changeStatus} />
              {/* `sm:ml-auto` matches Team Directory's own layout: it pushes the page size to the
                  trailing edge only once there is room for it, rather than leaving it marooned once
                  the row has already wrapped on a narrow screen. */}
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
                  : // A distinct message, because "the search found nothing" and "the directory is
                    // empty" are different facts and one of them would be alarming if reported as
                    // the other. A blank search with an empty result is the status filter's doing
                    // (issue #406) rather than the search's, so it gets its own generic wording
                    // instead of quoting an empty string.
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
 * Matches the query against name and email, and narrows by employment status (issue #406).
 *
 * Search is case-insensitive, because nobody types a surname in the case it happens to be stored in —
 * and on email as well as name, since the address is the identity BR-9 turns on and is often what an
 * administrator has been given. Status is applied first: it is the coarser filter, and Team Directory's
 * own status control is likewise a hard include/exclude rather than something search can override.
 *
 * Exported for the sake of nothing: kept module-private and covered through the screen, because its
 * behaviour is only meaningful as "what the list shows".
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

/**
 * Orders the visible rows by the chosen column (issue #666).
 *
 * Every column breaks ties on last name — the same secondary sort `CompassReadRepository.Sort` applies
 * server-side for Team Directory — except Last Name itself, which breaks ties on first name for the
 * identical reason.
 */
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
      // ISO `yyyy-MM-dd` sorts correctly as plain strings — no `Date` needed.
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
 * Compares two possibly-absent text values, sorting an absent one AFTER every real value — matching
 * Postgres's own default (`NULLS LAST` ascending), which is what an EDJEr with no coach gets from
 * `CompassReadRepository.Sort`'s equivalent server-side sort.
 *
 * Each side's absence is resolved independently, through {@link textOrLast}, rather than as a pair of
 * branches keyed on which side is null — the pair form has a branch ("this side is null, but is the
 * OTHER side too?") no test data can hit deterministically, since `Array.sort` decides comparator
 * argument order and this module exposes no lower-level function to call directly.
 */
function compareNullableText(left: string | null, right: string | null): number {
  return textOrLast(left).localeCompare(textOrLast(right));
}

/**
 * `￿` is a Unicode noncharacter, never a real coach name, and sorts after every ordinary letter —
 * so a missing value reads as "after everything" with no comparison to the OTHER side at all.
 */
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
        // type="search" gives the input the searchbox role, which is how both the unit tests and the
        // Playwright spec address it — and gives the browser its clear affordance for free.
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
 * Active. Unlike Team Directory's, it is offered unconditionally rather than gated to elevated roles,
 * because reaching this screen at all already requires the Compass root policy server-side (see the
 * module doc comment above) — there is no lesser-privileged viewer here for the control to be hidden
 * from.
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
      {/* Both name cells link to the edit route (issue #659) — the same two-column drill-in Team
          Directory uses for its own (read-only) destination. Splitting the link across both cells
          rather than picking one keeps First Name and Last Name each independently meaningful. */}
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
        {/* The mockups' `.pill.gray`, matching Team Directory's own Type column (issue #659). */}
        <StatusPill tone="neutral" label={edjer.employeeTypeName} />
      </td>
      {/* Plain text, never a link — deliberately unlike Team Directory's own Coach column, which
          drills into the coach's record (owner decision, issue #659). */}
      <td className="px-3 py-2">{edjer.coachName ?? ''}</td>
      <td className="px-3 py-2">{edjer.stateOfResidence}</td>
      <td className="px-3 py-2">
        {/* The word, not a colour. A pill that means something only by its hue conveys nothing in
            greyscale and nothing at all to a screen reader (AC-NFR-5). */}
        <StatusPill
          tone={edjer.isActive ? 'affirmative' : 'neutral'}
          label={edjer.isActive ? 'Active' : 'Former'}
        />
      </td>
    </tr>
  );
}
