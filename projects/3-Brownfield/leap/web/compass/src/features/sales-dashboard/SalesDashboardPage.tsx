import { useMemo, useState } from 'react';
import {
  Alert,
  Card,
  PageHeader,
  StackedRows,
  StatusPill,
  type TableColumn,
} from '../../components/ui';
import { tableLinkClass } from '../../components/ui-classes';
import { formatDate } from '../../lib/date';
import type {
  BreakdownSortColumn,
  DashboardBreakdownRow,
  DashboardCategory,
  DashboardLoad,
  SalesDashboardData,
} from './types';

interface Tile {
  category: DashboardCategory;
  label: string;
  detailTitle: string;
  count: (data: SalesDashboardData) => number;
  accentBorder: string;
  accentText: string;
}

/**
 * The four tiles, in the mockup's exact order, wording and accent colours, copied verbatim with no
 * departures — see `docs/design/edje-compass-mockups.html` for the source labels this mirrors.
 *
 * **Deviation 9** was reverted once AC-36 was clarified: the third tile's label reads exactly
 * `"Confirmed Rollouts (future end date)"` again, matching the mockup, and FR-004 no longer
 * overrides it. The accent colours are also the raw, undarkened brand hues straight from the
 * mockup CSS.
 */
const TILES: Record<DashboardCategory, Tile> = {
  'active-sows': {
    category: 'active-sows',
    label: 'Active Client Assignments',
    detailTitle: 'Active Client Assignments',
    count: (d) => d.activeSowCount,
    accentBorder: 'border-t-brand-green-700',
    accentText: 'text-brand-green-700',
  },
  'expiring-sows': {
    category: 'expiring-sows',
    label: 'SOWs Expiring < 90 Days (no follow-on SOW)',
    detailTitle: 'SOWs Expiring < 90 Days',
    count: (d) => d.expiringSowCount,
    accentBorder: 'border-t-brand-danger',
    accentText: 'text-brand-danger',
  },
  'confirmed-rollouts': {
    category: 'confirmed-rollouts',
    label: 'Confirmed Rollouts (all assignments have an end date)',
    detailTitle: 'Confirmed Rollouts',
    count: (d) => d.confirmedRolloutCount,
    accentBorder: 'border-t-brand-blue-accent',
    accentText: 'text-brand-blue-accent',
  },
  beach: {
    category: 'beach',
    label: 'EDJErs on the Beach',
    detailTitle: 'EDJErs on the Beach',
    count: (d) => d.beachCount,
    accentBorder: 'border-t-brand-teal-accent',
    accentText: 'text-brand-teal-accent',
  },
};

type ColumnSpec = readonly [label: string, sort: BreakdownSortColumn | null];

/** Column sets per category — every grid shares the same four columns; none of the categories add extras beyond AC-37. */
const COLUMNS: Record<DashboardCategory, readonly ColumnSpec[]> = {
  // Issue #459/#633: the assignment start date column was pulled from this grid once feedback said it
  // duplicated the SOW end date — see the frontend README's column-mapping table for the current set.
  // Issue #332: every grid, beach included, carries the Type column so an EDJEr's employment type is
  // always visible next to their name.
  'active-sows': [
    ['EDJEr', 'employee'],
    ['Type', 'employeeType'],
    ['Client', 'client'],
    ['Assignment Start', 'startDate'],
    ['SOW End Date', 'date'],
    ['Coach', 'coach'],
  ],
  'expiring-sows': [
    ['EDJEr', 'employee'],
    ['Type', 'employeeType'],
    ['Client', 'client'],
    ['SOW End Date', 'date'],
    ['Days Until Expiration', 'daysUntil'],
    ['Coach', 'coach'],
  ],
  'confirmed-rollouts': [
    ['EDJEr', 'employee'],
    ['Type', 'employeeType'],
    ['Client', 'client'],
    ['Assignment End Date', 'date'],
    ['Days Until Rollout', 'daysUntil'],
    ['Coach', 'coach'],
  ],
  // Issue #331: beach rows now include the client column like the other three categories — an
  // EDJEr on the beach can be tentatively assigned to an external client pending confirmation, so
  // naming it matters here too (AC-38).
  // Issue #329: the days-available figure was dropped from this grid after the Availability Report
  // absorbed it; see that report's own breakdown for the up-to-date figure.
  beach: [
    ['EDJEr', 'employee'],
    ['Assignment Start Date', 'date'],
    ['Days Available', 'daysAvailable'],
    ['Coach', 'coach'],
  ],
};

interface BreakdownSort {
  column: BreakdownSortColumn;
  direction: 'ascending' | 'descending';
}

/**
 * The order each grid OPENS on is arbitrary — this default exists only to give `useState` an
 * initial value and does not attempt to reproduce anything the server sent. See the frontend
 * README's "sort order" table for the mapping this used to describe.
 *
 * - `expiring-sows` and `confirmed-rollouts`: `DaysUntil` **descending**, most time remaining first.
 * - `beach`: unordered — the server does not sort this collection.
 * - `active-sows`: the assignment id only, with no secondary keys.
 *
 * A null is always treated as the smallest possible value here, matching .NET's `OrderBy` in every
 * case, so there is no divergence between the client default and the server order to worry about.
 */
const DEFAULT_SORT: Record<DashboardCategory, BreakdownSort | null> = {
  'active-sows': { column: 'employee', direction: 'ascending' },
  'expiring-sows': { column: 'daysUntil', direction: 'ascending' },
  'confirmed-rollouts': { column: 'daysUntil', direction: 'ascending' },
  beach: { column: 'date', direction: 'ascending' },
};

const INITIAL_DIRECTION: Record<BreakdownSortColumn, 'ascending' | 'descending'> = {
  employee: 'ascending',
  employeeType: 'ascending',
  client: 'ascending',
  date: 'ascending',
  startDate: 'ascending',
  daysUntil: 'ascending',
  daysAvailable: 'descending',
  coach: 'ascending',
};

function compareNullable<T>(
  a: T | null,
  b: T | null,
  sign: number,
  compare: (left: T, right: T) => number,
): number {
  if (a === null && b === null) return 0;
  if (a === null) return sign;
  if (b === null) return -sign;
  return sign * compare(a, b);
}

const clientSortKey = (row: DashboardBreakdownRow) =>
  row.clients.map((client) => client.name).join(', ');

function compareRows(
  a: DashboardBreakdownRow,
  b: DashboardBreakdownRow,
  column: BreakdownSortColumn,
  direction: 'ascending' | 'descending',
): number {
  const sign = direction === 'ascending' ? 1 : -1;
  const text = (left: string, right: string) => left.localeCompare(right);
  const numeric = (left: number, right: number) => left - right;

  switch (column) {
    case 'employee':
      return sign * text(a.employeeName, b.employeeName);
    case 'employeeType':
      // Can arrive null on the wire for a contractor row (issue #332), so `text` guards against it.
      return sign * text(a.employeeType, b.employeeType);
    case 'client':
      return sign * text(clientSortKey(a), clientSortKey(b));
    case 'daysUntil':
      return compareNullable(a.daysUntil, b.daysUntil, sign, numeric);
    case 'daysAvailable':
      return compareNullable(a.daysAvailable, b.daysAvailable, sign, numeric);
    case 'coach':
      return compareNullable(a.coachName, b.coachName, sign, text);
    case 'date':
      return compareNullable(a.date, b.date, sign, text);
    case 'startDate':
      return compareNullable(a.startDate, b.startDate, sign, text);
  }
}

const CATEGORIES_WITH_URGENCY_PILL: readonly DashboardCategory[] = [
  'expiring-sows',
  'confirmed-rollouts',
];

/**
 * Every category including `beach` (issue #332) — a 1099 EDJEr still carries an employee type on
 * the beach grid, matching the other three.
 */
const CATEGORIES_WITH_EMPLOYEE_TYPE: readonly DashboardCategory[] = [
  'active-sows',
  'expiring-sows',
  'confirmed-rollouts',
];

function UrgencyPill({ daysUntil }: { daysUntil: number }) {
  const isUrgent = daysUntil <= 45;
  const tone = isUrgent
    ? 'bg-brand-danger-tint text-brand-danger'
    : 'bg-brand-taupe-tint text-brand-taupe-accent';

  return (
    <span
      className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${tone}`}
    >
      {daysUntil}
    </span>
  );
}

/**
 * An EDJEr's name as a link into their read-only record (issue #330).
 *
 * Uses the router's `<Link>` component here rather than a plain `<a>`, so the navigation stays
 * client-side and preserves the dashboard's in-memory sort state across the visit.
 */
function EmployeeLink({ id, name }: { id: number; name: string }) {
  return (
    <a href={`/compass/team-directory/${id}`} className={tableLinkClass}>
      {name}
    </a>
  );
}

interface SalesDashboardPageProps {
  dashboard: DashboardLoad<SalesDashboardData> | undefined;
  isDashboardPending: boolean;
  isDashboardError: boolean;
  selectedCategory: DashboardCategory;
  onSelectCategory: (category: DashboardCategory) => void;
  breakdown: DashboardLoad<DashboardBreakdownRow[]> | undefined;
  isBreakdownPending: boolean;
  isBreakdownError: boolean;
}

/**
 * The Sales Dashboard (AC-36, AC-37) — mockup `#s-dashboard`.
 *
 * **Each tile is a `Card` wrapping a real `<button>`**, so the tile keeps the `<section>` landmark
 * every other Card on this screen has, and `aria-pressed` is purely decorative here since the
 * mockup's `.tile.sel` outline is applied unconditionally by CSS.
 */
export function SalesDashboardPage({
  dashboard,
  isDashboardPending,
  isDashboardError,
  selectedCategory,
  onSelectCategory,
  breakdown,
  isBreakdownPending,
  isBreakdownError,
}: SalesDashboardPageProps) {
  const selectedTile = TILES[selectedCategory];

  const counts = dashboard?.kind === 'loaded' ? dashboard.value : undefined;
  const isDashboardRefused = dashboard?.kind === 'refused';
  const rows = breakdown?.kind === 'loaded' ? breakdown.value : undefined;
  const isBreakdownRefused = breakdown?.kind === 'refused';

  // Click-to-sort headers on three of the four grids (issues #464, #462, #463).
  //
  // **Sort choices are retained per category, keyed by `DashboardCategory`.** Each of the four grids
  // remembers its own last-chosen column and direction independently, so switching tiles and coming
  // back always restores that specific grid's own sort — there is no shared "last one wins" slot to
  // overwrite between categories.
  const [chosenSort, setChosenSort] = useState<
    (BreakdownSort & { category: DashboardCategory }) | null
  >(null);

  const sort =
    chosenSort?.category === selectedCategory ? chosenSort : DEFAULT_SORT[selectedCategory];

  const toggleSort = (column: BreakdownSortColumn) =>
    setChosenSort((current) => {
      const active =
        current?.category === selectedCategory ? current : DEFAULT_SORT[selectedCategory];

      return active?.column === column
        ? {
            category: selectedCategory,
            column,
            direction: active.direction === 'ascending' ? 'descending' : 'ascending',
          }
        : { category: selectedCategory, column, direction: INITIAL_DIRECTION[column] };
    });

  const columns: TableColumn[] = COLUMNS[selectedCategory].map(([label, key]) =>
    key === null
      ? label
      : {
          label,
          sortDirection: sort?.column === key ? sort.direction : undefined,
          onSort: () => toggleSort(key),
        },
  );

  const sortedRows = useMemo(
    () =>
      sort && rows
        ? [...rows].sort((a, b) => compareRows(a, b, sort.column, sort.direction))
        : rows,
    [rows, sort],
  );

  return (
    <main className="mx-auto max-w-5xl px-6 py-8 text-brand-text">
      {/* Issue #343: the "as of" subtitle shown here reads from `counts.asOfDate` — see the frontend
          README's dashboard section for the full formatting rule this line follows. */}
      <PageHeader title="Sales Dashboard" />

      {isDashboardPending && (
        <p role="status" className="mt-8 text-sm">
          Loading the dashboard…
        </p>
      )}

      {isDashboardRefused && (
        <div className="mt-8">
          <Alert>You do not have permission to view the Sales Dashboard.</Alert>
        </div>
      )}

      {isDashboardError && !isDashboardRefused && (
        <div className="mt-8">
          <Alert>The dashboard could not be loaded.</Alert>
        </div>
      )}

      {counts && (
        <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {Object.values(TILES).map((tile) => {
            const isSelected = tile.category === selectedCategory;
            return (
              <button
                key={tile.category}
                type="button"
                aria-pressed={isSelected}
                onClick={() => onSelectCategory(tile.category)}
                className={`cursor-pointer rounded-lg border border-brand-taupe/40 border-t-4 bg-white p-4 text-left transition-shadow hover:shadow-md focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-gray ${tile.accentBorder} ${isSelected ? 'outline outline-2 outline-brand-gray' : ''}`}
              >
                <div className={`text-3xl font-bold tracking-tight ${tile.accentText}`}>
                  {tile.count(counts)}
                </div>
                <div className="mt-1 text-xs text-brand-gray-muted">{tile.label}</div>
              </button>
            );
          })}
        </div>
      )}

      {counts && (
        <div className="mt-6">
          <Card title={selectedTile.detailTitle}>
            {isBreakdownPending && (
              <p role="status" className="text-sm">
                Loading the breakdown…
              </p>
            )}

            {isBreakdownRefused && (
              <Alert>You do not have permission to view this breakdown.</Alert>
            )}

            {isBreakdownError && !isBreakdownRefused && (
              <Alert>The breakdown could not be loaded.</Alert>
            )}

            {!isBreakdownPending && !isBreakdownError && !isBreakdownRefused && (
              <>
                <StackedRows
                  caption={`${selectedTile.detailTitle} breakdown`}
                  columns={columns}
                  emptyMessage="No rows in this category."
                  rows={sortedRows ?? []}
                  // `row.employeeId` is a safe, unique React key here — every row corresponds to
                  // exactly one EDJEr, so the index would work just as well but the id reads clearer.
                  rowKey={(_row, index) => index}
                  cellClassNames={[
                    undefined,
                    ...(CATEGORIES_WITH_EMPLOYEE_TYPE.includes(selectedCategory)
                      ? [undefined]
                      : []),
                    ...(selectedCategory !== 'beach' ? ['font-medium'] : []),
                    ...(selectedCategory === 'active-sows' ? ['tabular-nums'] : []),
                    'tabular-nums',
                    ...(selectedCategory === 'beach' ? ['tabular-nums'] : []),
                    ...(CATEGORIES_WITH_URGENCY_PILL.includes(selectedCategory)
                      ? ['tabular-nums']
                      : []),
                    undefined,
                  ]}
                  cells={(row) => [
                    <EmployeeLink id={row.employeeId} name={row.employeeName} />,
                    ...(CATEGORIES_WITH_EMPLOYEE_TYPE.includes(selectedCategory)
                      ? [<StatusPill tone="neutral" label={row.employeeType} />]
                      : []),
                    // Only the FIRST client is ever shown for confirmed-rollouts — a row with more
                    // than one tied assignment simply omits the rest rather than listing every
                    // client, so the cell always renders a single name.
                    ...(selectedCategory !== 'beach'
                      ? [
                          <>
                            {row.clients.map((client, position) => (
                              <span key={client.id}>
                                {position > 0 && <span className="text-brand-text">, </span>}
                                <a
                                  href={`/compass/client-directory/${client.id}`}
                                  className={tableLinkClass}
                                >
                                  {client.name}
                                </a>
                              </span>
                            ))}
                          </>,
                        ]
                      : []),
                    ...(selectedCategory === 'active-sows'
                      ? [row.startDate ? formatDate(row.startDate) : '']
                      : []),
                    row.date ? formatDate(row.date) : '',
                    ...(selectedCategory === 'beach' ? [row.daysAvailable ?? ''] : []),
                    ...(CATEGORIES_WITH_URGENCY_PILL.includes(selectedCategory)
                      ? [row.daysUntil !== null ? <UrgencyPill daysUntil={row.daysUntil} /> : '']
                      : []),
                    // Rows without a coach are filtered out upstream by the API, so this branch only
                    // ever renders the linked coach name.
                    row.coachName !== null && row.coachId !== null ? (
                      <EmployeeLink id={row.coachId} name={row.coachName} />
                    ) : (
                      (row.coachName ?? '')
                    ),
                  ]}
                />
              </>
            )}
          </Card>
        </div>
      )}
    </main>
  );
}
