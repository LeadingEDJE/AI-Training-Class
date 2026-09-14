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
  /** The mockup's qualifying label, verbatim (FR-028) — the counting unit is on the tile itself. */
  label: string;
  /** The breakdown card's shorter title, matching the mockup's own `<h2>` for its one visible tile. */
  detailTitle: string;
  count: (data: SalesDashboardData) => number;
  /** The mockup's per-tile accent — top border, and the number itself (`.tile.<accent> .num`). */
  accentBorder: string;
  accentText: string;
}

/**
 * The four tiles, in the mockup's order, wording and accent colours — with one deliberate
 * departure and several AA-driven substitutions.
 *
 * **Deviation 9**: the third tile's mockup label is `"Confirmed Rollouts (future end date)"`. That
 * qualifier states AC-36's literal rule, which FR-004 overrides: a rollout is an EDJEr *every* one
 * of whose active assignments carries an end date, not merely one with a future end date. Shipping
 * the mockup's qualifier would label the tile with a rule it does not implement, so it is replaced
 * under FR-026 rule 1 rather than silently dropped.
 *
 * **Accent colours are the mockup's own hues, darkened where the raw brand value misses AA** — see
 * `brand-tokens.ts`'s `BRAND_BLUE_ACCENT_TEXT`/`BRAND_TEAL_ACCENT_TEXT` for the measured
 * replacements. The green tile reuses the already-vetted `BRAND_GREEN_700`, and the red tile reuses
 * `BRAND_DANGER_TEXT` — both already load-bearing elsewhere in the app for the same "needs a 3:1+
 * accent" job.
 */
// A Record, not an array + .find(): every DashboardCategory has an entry by construction, so a
// lookup can never come back undefined and callers do not need an `?? ''` fallback for a case the
// type system already rules out.
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

/**
 * One column of one category's grid: the header label, and the row field its header orders the grid
 * on — `null` where the header is plain text.
 *
 * The label and its sort key in ONE place, so they cannot drift apart, and so the list that builds
 * `columns` is by construction the same length as the one that builds `cells`. That length is
 * load-bearing: `StackedRows` pairs the two BY INDEX, and a construction that diverged by one would
 * label every value with its neighbour's header and fail nothing.
 */
type ColumnSpec = readonly [label: string, sort: BreakdownSortColumn | null];

/** Column sets per category — SD-2's five columns for expiring SOWs are a genuine superset of AC-37's four. */
const COLUMNS: Record<DashboardCategory, readonly ColumnSpec[]> = {
  // Issue #459/#633: this grid shows the assignment's own start date alongside the (max) SOW end date
  // — so a row is legible on its own, and both are sortable. This is the only grid that renders a
  // start date, which is why `startDate` exists on `BreakdownSortColumn`. All six of its columns
  // carry a sort key. Issue #633: this tile counts ACTIVE CLIENT ASSIGNMENTS, not SOWs — the first
  // column now reads "Assignment Start" rather than "SOW Start Date".
  //
  // Issue #332: "Type" sits right next to the EDJEr's name on every grid but the beach one — a
  // 1099 EDJEr can never be on the beach, so that grid has no need for the column.
  'active-sows': [
    ['EDJEr', 'employee'],
    ['Type', 'employeeType'],
    ['Client', 'client'],
    ['Assignment Start', 'startDate'],
    ['SOW End Date', 'date'],
    ['Coach', 'coach'],
  ],
  // Issues #462 and #463: every header on these two grids orders the rows ("any of the headers").
  // The date column and the day-count column beside it are correlated by construction — the count IS
  // the date minus today — so ordering by either produces the same rows in the same order. Both are
  // still separately sortable, because a viewer who reaches for the column they are reading should
  // not have to know that the neighbouring one would have done.
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
  // Issue #331: every beach row is an assignment to an internal EDJE client (FR-005) — naming it
  // adds no information a Sales user needs, unlike the other three categories where the client is
  // the whole point of the row. The Availability Report's equivalent "Currently Available EDJErs"
  // section already drops it for the same reason (AC-38; CompassReportReadService's own remark that
  // "§1 drops the client the beach breakdown carries. That is the point of the projection, not an
  // omission.").
  // Issue #329: "days available" (today minus the assignment start date) sits between the start
  // date and the coach, so a sales viewer can see how long someone has been on the bench without
  // doing the subtraction themselves.
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
 * The order each grid OPENS on, chosen to reproduce the order the server sent, so that selecting a
 * tile shows the rows in the order the API returned them and the first click on any header is the
 * first re-ordering a viewer sees.
 *
 * **Read from the code, not from the contract.** Every one of the four `*Breakdown` methods in
 * `CompassDashboardRepository` applies an `OrderBy`. The "Sort order" bullet recorded `beach` and
 * the active grid as UNORDERED, which was wrong about both until issue #525 corrected it — the
 * sentence was true the day it was written (2026-08-17) and false the next, when `d307eab` added
 * `ORDER BY` to exactly those two. What the code does:
 * - `expiring-sows` and `confirmed-rollouts`: `DaysUntil` **ascending**, soonest on top (FR-006).
 * - `beach`: its start date ascending, oldest first — also the owner decision on issue #464.
 * - `active-sows`: employee name, then the (max) SOW end date, then the assignment id
 *   (`ActiveSowsBreakdown_IsOrderedByTheEdjerNameTheCellDisplays`).
 *
 * `active-sows` opens on `{ column: 'employee', direction: 'ascending' }` — the server's primary key
 * (issue #459). A stable sort on that single key preserves the server's tiebreaks, so selecting the
 * tile reproduces the order the API sent and the first header click is the first re-ordering a
 * viewer sees.
 *
 * The four server orders break ties on further keys; `Array.prototype.sort` is stable, so ordering by
 * the single key here KEEPS those tiebreaks rather than scrambling equal rows.
 *
 * **One qualification on "reproduces the server's order".** A null sorts FIRST under .NET's
 * `OrderBy` and LAST under {@link compareNullable} ascending, so the two disagree on a row whose sort
 * key is null. That cannot arise for `expiring-sows` (`Sow.SowEndDate` is non-nullable). For
 * `confirmed-rollouts` the DTO's `DaysUntil` is typed nullable and the projection writes a null for a
 * missing end date, but the query admits only assignments whose end date equals a non-null MAX, so no
 * such row is produced — type-reachable, not query-reachable. If that ever changes, this default and
 * the server's order diverge on those rows, and the client's rule is the one described below.
 */
const DEFAULT_SORT: Record<DashboardCategory, BreakdownSort | null> = {
  'active-sows': { column: 'employee', direction: 'ascending' },
  'expiring-sows': { column: 'daysUntil', direction: 'ascending' },
  'confirmed-rollouts': { column: 'daysUntil', direction: 'ascending' },
  beach: { column: 'date', direction: 'ascending' },
};

/**
 * Which way a column sorts when it is chosen from a DIFFERENT one (issues #464, #462, #463).
 *
 * **This map is consulted ONLY on arrival at a column from another.** Choosing the column that
 * already orders the grid REVERSES it — including on the very first click of a grid's default
 * column, which is a reversal and not a re-selection (`toggleSort` below, and the beach grid's own
 * "reverses on a second click" case in `sales-dashboard.critical.spec.ts`, where one click on the
 * already-ordered header produces a full reversal and `aria-sort="descending"`).
 *
 * Ascending is right for a name, a type, a client and a date. The two day counts differ from each
 * other because the question differs: `daysUntil` ascending is most-urgent-first, matching FR-006's
 * own smallest-on-top order, while `daysAvailable` descending shows the people who have been on the
 * beach longest first — matching the Client Assignment Duration report's `duration: descending`
 * (longest tenure first) for the same reason.
 */
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

/**
 * The house rule for a nullable column: **a null sorts AFTER every real value, and FLIPS with the
 * direction.**
 *
 * The same rule `AssignmentDurationPage`'s `compare` uses for its nullable Coach column (owner
 * decision 2026-08-20, reaffirmed for issue #464): A→Z/ascending puts a null at the END, Z→A/
 * descending brings it to the START, so reversing a column is always a TOTAL reversal rather than one
 * that leaves the null rows stranded in place. `sign`, not a fixed 1/-1, is what carries that.
 *
 * Two nulls are equal, so neither jumps the other when the direction flips.
 */
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

/**
 * The Client column's sort key: the cell's own rendered text.
 *
 * A confirmed-rollouts row can name MORE THAN ONE client — an EDJEr with two assignments tied on the
 * end date that keys the category (FR-011) — and the cell renders them comma-joined in list order.
 * Keying on that whole joined string rather than on `clients[0]` means the grid orders by exactly
 * what the viewer is comparing by eye; a first-client-only key would leave two rows sharing a first
 * client in whatever order they arrived in, which is an order the screen gives no way to predict.
 * The separator matches the cell's below for the same reason.
 *
 * `clients` is never null — an empty list is a real empty cell, not a missing value, so it sorts
 * first ascending like any other empty string rather than going through {@link compareNullable}.
 */
const clientSortKey = (row: DashboardBreakdownRow) =>
  row.clients.map((client) => client.name).join(', ');

/** Compares one row pair on the chosen column. */
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
      // Never null on the wire (issue #332), so no null branch to reach.
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
      // `row.date` is an ISO `yyyy-mm-dd` string, so a lexical compare IS chronological. It is
      // RENDERED through `formatDate` as mm/dd/yyyy, and sorting that text would order by month
      // across years — wrong, and plausible enough to go unnoticed. Sort the raw value, always.
      return compareNullable(a.date, b.date, sign, text);
    case 'startDate':
      // The active-sows grid's assignment start date (issue #459, #633). Same ISO-string discipline
      // as `date`: sort the raw `yyyy-mm-dd` value, never the `formatDate` output, so the order is
      // chronological.
      return compareNullable(a.startDate, b.startDate, sign, text);
  }
}

/** The two categories whose fifth column is the urgency pill, not a start date (issue #316). */
const CATEGORIES_WITH_URGENCY_PILL: readonly DashboardCategory[] = [
  'expiring-sows',
  'confirmed-rollouts',
];

/**
 * Every category but `beach` (issue #332) — a 1099 EDJEr is never on the beach, so that grid has
 * no need for the Type column the other three carry.
 */
const CATEGORIES_WITH_EMPLOYEE_TYPE: readonly DashboardCategory[] = [
  'active-sows',
  'expiring-sows',
  'confirmed-rollouts',
];

/**
 * The mockups' day-count urgency pill (SD-2's `.pill.red` / `.pill.taupe`) — a colour ACCENT on a
 * number that already carries the information, never colour alone (AC-NFR-5).
 *
 * 45 days is not an acceptance criterion; it is this screen's own presentational split, roughly
 * midway through the 90-day expiry/rollout window the two categories that use this pill share.
 */
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
 * The same destination and the same class the Team Directory grid and the Employee Detail page's own
 * coach cell already use, so the three screens read as one affordance. A plain `<a>`, not a router
 * `<Link>`, matching the client link beside it and every other in-grid link in Compass.
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
 * **Each tile is a real `<button>` styled to the mockup's `.tile`, not a `Card`.** The mockup's
 * whole tile is the click target (`.tile{cursor:pointer}`), and `Card` renders a `<section>`
 * landmark that a `<button>` cannot legally wrap — so matching the mockup's actual interaction
 * (click anywhere on the tile) means the tile itself must be the interactive element, with
 * `aria-pressed` carrying the selected state `.tile.sel`'s outline shows visually.
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

  // Unwrapped once, so the JSX below reads the same as it did before the load became three-state.
  const counts = dashboard?.kind === 'loaded' ? dashboard.value : undefined;
  const isDashboardRefused = dashboard?.kind === 'refused';
  const rows = breakdown?.kind === 'loaded' ? breakdown.value : undefined;
  const isBreakdownRefused = breakdown?.kind === 'refused';

  // Click-to-sort headers on three of the four grids (issues #464, #462, #463). Client-side over the
  // already-materialised list, matching `AssignmentDurationPage` (FR-014's "the ordering changes and
  // the data set is unchanged" for that report applies here too — there is nothing server-side left
  // to re-fetch).
  //
  // **The chosen column is stored WITH the category it was chosen on, and `null` means "nothing
  // chosen yet".** The four grids do not share a column set, so a column picked on one can be absent
  // from the next: carrying a bare column across a tile change would order the new grid by a key none
  // of its headers names, with no `aria-sort` anywhere to say so. A slot whose category is not the
  // selected one reads as "not chosen" for this grid, derived below rather than reset in an effect.
  //
  // It is ONE slot, so the retention is last-one-wins rather than per-category, and that is worth
  // stating exactly because "reads as not chosen" sounds like a discard and is not: sorting the
  // expiring-SOWs grid, visiting another tile and coming back RESTORES that sort, while choosing a
  // sort on the other tile in between overwrites the slot and leaves expiring-SOWs on its default.
  // Both paths are pinned by tests. Per-category retention would be a `Record`, and nothing has asked
  // for it — a viewer who changes tiles has changed subject.
  const [chosenSort, setChosenSort] = useState<
    (BreakdownSort & { category: DashboardCategory }) | null
  >(null);

  const sort =
    chosenSort?.category === selectedCategory ? chosenSort : DEFAULT_SORT[selectedCategory];

  /**
   * Choosing the ordered column again reverses it; choosing another starts at its own natural
   * direction.
   *
   * **The updater form, not the `sort` already derived above.** `sort` is a render value, so reading
   * it here would make two clicks that React batches into one tick both see the same "current"
   * direction — the second would compute the same flip as the first and one of them would be lost.
   * The updater re-derives from the queued state instead, which means repeating the `sort`
   * derivation above rather than reusing it: the state holds a category alongside the sort, and a
   * chosen sort for a different tile is not this grid's order.
   */
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

  // Built from `COLUMNS`, so the header list is the same length as the `cells` list below by
  // construction — a plain-text label where the category declares no sort key for that column.
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
      {/* Issue #343: no "as of" subtitle — the counts are inherently as of the moment the page is
          loaded, so stating that date added nothing the reader didn't already know. The business
          date the counts are computed against (`counts.asOfDate`) still travels on the wire
          unchanged; only this display line is gone. */}
      <PageHeader title="Sales Dashboard" />

      {isDashboardPending && (
        <p role="status" className="mt-8 text-sm">
          Loading the dashboard…
        </p>
      )}

      {/* A REFUSAL is not a failure. CompassReporting admits Sales, Ops and the Compass root but not
          Compass Admin (FR-019); the nav hides this screen from everyone it excludes, but a bookmark
          or a shared link does not. Telling that viewer the dashboard is broken sends them to
          diagnose an outage that is not happening. */}
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
                  // `?? []`, because `rows` is genuinely optional here (the breakdown may not have
                  // loaded yet) — that half of the old guard was a real null check, not the
                  // empty-array workaround. An empty array reaches StackedRows as "empty".
                  rows={sortedRows ?? []}
                  // Index, not row.employeeId: this shape is per ASSIGNMENT or per SOW depending on
                  // the category, and one EDJEr can appear in more than one row (FR-002's split
                  // allocation is exactly this case) — employeeId is not a unique React key here, and
                  // reusing it as one produced real duplicate-key warnings and mis-rendered rows,
                  // caught by sales-dashboard.critical.spec.ts against the live seeded data.
                  rowKey={(_row, index) => index}
                  // `tabular-nums` on the CELL box, not on a span inside it -- see `cellClassNames`
                  // in ui.tsx for why the box is the right target now that #574 removed the gate that
                  // originally forced it.
                  //
                  // Built from the SAME predicates as `cells` below and in the same order, because
                  // StackedRows pairs both to `columns` BY INDEX. The four category shapes differ in
                  // width (6, 6, 6, and 4 for beach), so a hard-coded array cannot serve them —
                  // that is what the old `columns.length === 5` test did, and issues #329/#331/#332
                  // retired the shape it assumed.
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
                    // Issue #330: the EDJEr's own record, from the name the grid already shows.
                    // `employeeId` is never null, so this cell always links.
                    <EmployeeLink id={row.employeeId} name={row.employeeName} />,
                    // The mockups' `.pill.gray`, matching the Team Directory's own "Type" column
                    // exactly (owner request, issue #332). Absent on the beach grid — a 1099 EDJEr
                    // can never be on the beach.
                    ...(CATEGORIES_WITH_EMPLOYEE_TYPE.includes(selectedCategory)
                      ? [<StatusPill tone="neutral" label={row.employeeType} />]
                      : []),
                    // Every client, not just the first, for confirmed-rollouts — an EDJEr can hold
                    // two assignments tied on the end date that keys that category, and naming one
                    // while dropping the other would hide real work from a sales screen. Omitted
                    // entirely for beach (issue #331): every row there is an internal-EDJE
                    // assignment, so the column carries no information. The remaining two categories
                    // always carry exactly one client and render unchanged.
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
                    // Issue #459/#633: the assignment's own start date alongside the (max) SOW end
                    // date is what makes an active-assignments row legible on its own.
                    ...(selectedCategory === 'active-sows'
                      ? [row.startDate ? formatDate(row.startDate) : '']
                      : []),
                    // The date that KEYS the category, so its column label differs per category
                    // while the field does not. On beach this is the assignment start date; on
                    // active-sows it is the MAX SOW end date across the assignment's SOWs (issue #633).
                    row.date ? formatDate(row.date) : '',
                    // Beach-only (issue #329): a plain day count, not the urgency pill — nothing
                    // about having been available longer is urgent in the way an approaching SOW
                    // expiry or rollout date is.
                    ...(selectedCategory === 'beach' ? [row.daysAvailable ?? ''] : []),
                    ...(CATEGORIES_WITH_URGENCY_PILL.includes(selectedCategory)
                      ? [row.daysUntil !== null ? <UrgencyPill daysUntil={row.daysUntil} /> : '']
                      : []),
                    // Empty, never omitted: an EDJEr with no coach is present, not filtered out
                    // (FR-008) — the dashboard's compensating control for the coach notification
                    // Stream 3 skips. Issue #330 links it when there IS one, guarded on BOTH halves
                    // so a name can never render as a link to nowhere.
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
