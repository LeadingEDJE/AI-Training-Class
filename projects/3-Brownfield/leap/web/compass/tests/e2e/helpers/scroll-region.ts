/**
 * Gate A — the stranded-content criterion, for Compass's own e2e suite (feature 011, issue #83).
 *
 * **Why this exists alongside the axe sweep rather than inside it.** axe's `scrollable-region-focusable`
 * asks whether a scroll region contains *any* focusable element. The defect it misses is whether one
 * exists **beyond the visible edge** — because that is the mechanism by which off-screen columns become
 * reachable (a browser scrolls a focused element into view). Measured on 2026-08-21, axe passes
 * `client-view` and `sales-dashboard` at 390px and `team-directory` at 768px; all three strand content.
 * So this is not a re-implementation of axe, it is the assertion axe cannot express.
 *
 * **Two properties, not one — and conflating them was a real mistake made here.** The first draft
 * collapsed both into a single `strands` boolean. Then layer 1 landed, `tabIndex` on the region cleared
 * axe, and Gate A still failed — correctly, because a focusable region makes hidden content *reachable*
 * without making a clipped value *legible*. `09/09/2026` still rendered as `09/09/202`. So:
 *
 * - **`strands` (FR-002)** — reachability. A focusable descendant past the edge, or a focusable region.
 *   This is what axe's `scrollable-region-focusable` is about, extended to accept either route.
 * - **`misreadable` (FR-001)** — legibility. An edge mask marks the clip, so a cut value looks cut.
 *
 * Fixing one does not fix the other — the axe-sanctioned fix alone is "necessary but insufficient".
 *
 * **Provenance of the criterion.** It is T118's, not ours. The T118 investigation into a
 * reported mobile-overflow defect correctly retracted it, and in doing so named the one combination
 * that *would* strand content: a region scrollable with "no focusable descendant beyond the edge". It
 * dismissed its own instance because that instance was 4px, and "four pixels hides no glyph". This
 * helper is that sentence turned into code.
 */
import { expect, type Page } from '@playwright/test';

/**
 * The one selector that identifies a horizontal scroll region in Compass.
 *
 * Named rather than inlined because the `ScrollableRegion` primitive (feature 011 FR-006) keeps
 * rendering this class while adding a focus target and an edge mask — so the selector survives that
 * change, and if it ever stops doing so there is exactly one place to correct.
 */
export const SCROLL_REGION_SELECTOR = '.overflow-x-auto';

/**
 * The width below which a horizontal clip is treated as a layout artifact rather than lost content.
 *
 * **4, from T118, deliberately not lowered.** That investigation cleared `admin/lookups`' invoice-
 * frequency table — scrollable by 4px with nothing focusable past the edge — on the ground that "four
 * pixels hides no glyph; it is a layout rounding artifact, not lost content". Lowering this threshold
 * silently re-opens a finding that was correctly retracted, and would fail this suite on that same
 * table. Raising it starts hiding real glyph loss: the smallest genuine defect this feature found is
 * 20px, which is one character of a date.
 */
export const GLYPH_THRESHOLD_PX = 4;

/** A cell whose text crosses the visible edge — the visible evidence of a clipped value. */
export interface StraddlingCell {
  /** The cell's text, truncated for a readable failure message. */
  text: string;
  /** How far past the visible edge the cell extends. */
  cutPx: number;
}

/** One horizontal scroll region, measured. See `data-model.md` § ScrollRegionMeasurement. */
export interface ScrollRegionMeasurement {
  /** `scrollWidth - clientWidth`: how much content sits outside the visible box. */
  hiddenPx: number;
  /** Focusable descendants at or past the visible right edge — one way to reach hidden content. */
  focusablesBeyondEdge: number;
  /** Whether the region itself is keyboard-focusable — the other way (FR-002). */
  regionIsKeyboardFocusable: boolean;
  /** Either route to the hidden content without a pointer. */
  hasKeyboardPath: boolean;
  /** Whether an edge mask marks the clip as a clip (FR-001). */
  hasEdgeMask: boolean;
  /** Up to three cells crossing the edge, for the failure message. */
  straddlingCells: StraddlingCell[];
  /** Whether the PAGE scrolls sideways. Must stay false — Gate E, T118's stated design goal. */
  pageOverflows: boolean;
  /** **FR-002 verdict**: hidden past the threshold with no keyboard path at all. */
  strands: boolean;
  /** **FR-001 verdict**: hidden past the threshold with nothing marking the clip as a clip. */
  misreadable: boolean;
}

/**
 * Measures every scroll region on the current page at the current viewport.
 *
 * **Call this after the page has settled.** Measuring a loading state measures the spinner — the same
 * caveat `expectNoWcagViolations` documents, for the same reason.
 *
 * **The verdict is viewport-scoped, and that is not a detail.** `team-directory` is clean at 390px
 * (197 focusables sit past the edge, so keyboard traversal walks the region) and strands at 768px (every
 * link-bearing column is visible, and the only column past the edge is plain text). Which columns sit
 * off-screen changes with width, so "this screen is fine" is never a property of the screen alone.
 */
export async function measureScrollRegions(page: Page): Promise<ScrollRegionMeasurement[]> {
  return page.evaluate(
    ({ selector, threshold }) => {
      const out = [];

      for (const region of Array.from(document.querySelectorAll(selector))) {
        const hiddenPx = region.scrollWidth - region.clientWidth;
        if (hiddenPx <= 0) {
          continue;
        }

        // The right edge of what a viewer can actually see, in viewport coordinates.
        const visibleRight = region.getBoundingClientRect().left + region.clientWidth;

        // `- 1` absorbs sub-pixel layout: without it an element sitting flush against the edge
        // flickers between "beyond" and "not beyond" between runs.
        const focusablesBeyondEdge = Array.from(
          region.querySelectorAll('a[href],button,input,select,textarea,[tabindex]'),
        ).filter((el) => el.getBoundingClientRect().left >= visibleRight - 1).length;

        // The second keyboard route (FR-002): the scroll container itself takes focus, so the arrow
        // keys pan it. This is what axe's `scrollable-region-focusable` asks for, and it is why that
        // rule is necessary but not sufficient — see `misreadable` below.
        const tabIndexAttr = region.getAttribute('tabindex');
        const regionIsKeyboardFocusable = tabIndexAttr !== null && Number(tabIndexAttr) >= 0;

        // FR-001's mechanism: a mask over the clipped edge, so a cut value looks cut. Queried by the
        // data attribute rather than by inspecting the gradient, which would be brittle and no stronger.
        const hasEdgeMask =
          (region.parentElement?.querySelector(':scope > [data-scroll-edge-mask]') ?? null) !==
          null;

        const straddlingCells = Array.from(region.querySelectorAll('td,th'))
          .filter((cell) => {
            const rect = cell.getBoundingClientRect();
            return (
              rect.left < visibleRight &&
              rect.right > visibleRight + 1 &&
              (cell.textContent ?? '').trim() !== ''
            );
          })
          .slice(0, 3)
          .map((cell) => ({
            text: (cell.textContent ?? '').trim().slice(0, 40),
            cutPx: Math.round(cell.getBoundingClientRect().right - visibleRight),
          }));

        const hasKeyboardPath = focusablesBeyondEdge > 0 || regionIsKeyboardFocusable;
        const overThreshold = Math.round(hiddenPx) > threshold;

        out.push({
          hiddenPx: Math.round(hiddenPx),
          focusablesBeyondEdge,
          regionIsKeyboardFocusable,
          hasKeyboardPath,
          hasEdgeMask,
          straddlingCells,
          pageOverflows:
            document.documentElement.scrollWidth > document.documentElement.clientWidth,
          strands: overThreshold && !hasKeyboardPath,
          misreadable: overThreshold && !hasEdgeMask,
        });
      }

      return out;
    },
    { selector: SCROLL_REGION_SELECTOR, threshold: GLYPH_THRESHOLD_PX },
  );
}

/**
 * Asserts Gate A and Gate E on the current page and viewport.
 *
 * **The failure message carries the measurement, not just a boolean.** A bare "expected 0 stranding
 * regions" sends the reader back to the browser to work out which column on which screen; the pixel
 * count and the clipped cell text identify it directly.
 */
export async function expectNoStranding(page: Page, screenName: string): Promise<void> {
  const regions = await measureScrollRegions(page);

  // FR-002 — reachability. Either a focusable descendant past the edge, or a focusable region.
  const stranding = regions.filter((region) => region.strands);
  expect(
    stranding,
    `${screenName}: ${stranding.length} scroll region(s) hide content past ${GLYPH_THRESHOLD_PX}px ` +
      'with NO keyboard path to it — neither a focusable descendant beyond the visible edge nor a ' +
      'focusable region (FR-002). ' +
      JSON.stringify(stranding, null, 2),
  ).toEqual([]);

  // FR-001 — legibility. A separate property, and the reason `tabIndex` alone is not the fix: it makes
  // the hidden content reachable while leaving `09/09/2026` rendered as `09/09/202` for anyone reading
  // the screen. Both must hold.
  const misreadable = regions.filter((region) => region.misreadable);
  expect(
    misreadable,
    `${screenName}: ${misreadable.length} scroll region(s) clip content with nothing marking the ` +
      'clip as a clip (FR-001). A truncated value that reads as a complete one is the defect; an ' +
      'edge mask is what makes the cut visible. ' +
      JSON.stringify(misreadable, null, 2),
  ).toEqual([]);

  // Gate E, checked here because it is measured here. T118's design goal was that a wide table scrolls
  // INSIDE its own region rather than pushing the page sideways; a remedy that fixed Gate A by letting
  // the page scroll would trade one defect for a worse one.
  const overflowing = regions.filter((region) => region.pageOverflows);
  expect(overflowing, `${screenName}: the PAGE scrolls horizontally (Gate E)`).toEqual([]);
}

/**
 * Frozen per-screen counts of scroll regions that must EXIST at 390px (T012, Gate D).
 *
 * **What this defends against.** A screen whose table stops rendering has zero regions, so every
 * stranding check on it passes trivially and the suite reports a clean bill of health over nothing.
 * `measureScrollRegions` returns only regions that *currently overflow*, so this is a floor at one
 * measured width rather than an equality that a legitimate layout change would break.
 *
 * ⚠️ **This map lists ONLY screens that keep their table at 390px — the admin and directory screens
 * FR-006b pins as tables at every width.** The five read screens are deliberately absent, and adding
 * them would be a landmine: FR-006a stacks them to cards below `md`, so after that lands they will have
 * **zero** regions at 390px by design, and a floor here would fail on the correct outcome. Their
 * non-vacuity guard is the opposite assertion — Gate G's "no scroll region at all below 768px" (T019).
 * Two screen sets, two opposite guards, and neither is optional.
 *
 * Measured 2026-08-21 at `58626f43`. `admin-lookups` is absent because its tables do not overflow at
 * 390px at all (T118 measured its invoice-frequency table at 4px, below the threshold) — absence here
 * means "nothing to floor", not "exempt".
 */
export const SCROLL_REGION_FLOOR_AT_390: Readonly<Record<string, number>> = Object.freeze({
  'team-directory': 1,
  'admin-edjers': 1,
  'admin-clients': 1,
});

/**
 * Asserts a table-keeping screen still renders the scroll regions it is known to have, so a stranding
 * pass on it cannot be vacuous (Gate D). Call at 390x844, where the floors were measured.
 *
 * A screen absent from {@link SCROLL_REGION_FLOOR_AT_390} returns without asserting — see that map's
 * note on why the read screens must not be listed.
 */
export async function expectRegionsPresent(page: Page, screenName: string): Promise<void> {
  const floor = SCROLL_REGION_FLOOR_AT_390[screenName];
  if (floor === undefined) {
    return;
  }

  const regions = await measureScrollRegions(page);
  expect(
    regions.length,
    `${screenName}: expected at least ${floor} overflowing scroll region(s) at 390px but found ` +
      `${regions.length}. Either the table stopped rendering — in which case every stranding ` +
      `assertion for this screen is now vacuous — or the layout legitimately changed and this floor ` +
      `needs re-measuring. Do not delete the entry to make this pass.`,
  ).toBeGreaterThanOrEqual(floor);
}

/**
 * Screens that MUST render at least one data row for their stranding assertion to mean anything, and
 * the minimum row count each carries in the e2e seed (T041, FR-012, Gate D).
 *
 * **The failure this defends against is a pass, not a failure.** A table with no rows has no overflow,
 * so every stranding assertion on that screen succeeds — over nothing. If the seed regresses, or a
 * filter defaults to excluding everything, the sweep reports a clean bill of health across 23 screens
 * while measuring empty pages. That is the vacuous-pass shape this repo has shipped eight times over.
 *
 * Deliberately a floor, not an equality: the seed is date-relative, so `reports-availability`'s
 * "expiring in 90 days" section legitimately grows and shrinks with the calendar. Absence from this map
 * means "this screen has no rows to floor" — a form, an index, or the not-found page — not "exempt".
 *
 * Counted as rendered ROWS, which is layout-independent: a `<tr>` at or above `md` and a card `<li>`
 * below it are the same datum, so one floor covers both branches of `StackedRows`.
 */
export const SCREEN_ROW_FLOOR: Readonly<Record<string, number>> = Object.freeze({
  'team-directory': 1,
  'client-directory': 1,
  'client-view-internal': 1,
  'client-view-external': 1,
  'employee-detail': 1,
  'sales-dashboard': 1,
  'reports-availability': 1,
  'reports-assignment-duration': 1,
  'admin-lookups': 1,
  'admin-edjers': 1,
  'admin-clients': 1,
});

/**
 * Asserts a data-bearing screen actually rendered data, so its stranding verdict is not vacuous.
 *
 * Counts table body rows and card list items together — `StackedRows` renders one or the other
 * depending on viewport, and both represent one row of the same dataset.
 */
export async function expectRowsPresent(page: Page, screenName: string): Promise<void> {
  const floor = SCREEN_ROW_FLOOR[screenName];
  if (floor === undefined) {
    return;
  }

  const rows = await page.evaluate(() => {
    const tableRows = document.querySelectorAll('main tbody tr').length;
    // `StackedRows`' card branch. Scoped to lists that carry an accessible name, which is what
    // distinguishes a data list from the navigation.
    const cardRows = document.querySelectorAll('main ul[aria-label] > li').length;
    return tableRows + cardRows;
  });

  expect(
    rows,
    `${screenName}: expected at least ${floor} rendered row(s) but found ${rows}. An empty screen has ` +
      'no overflow, so every stranding assertion on it passes over nothing. Either the seed regressed ' +
      '(run `make e2e-stack-up`) or a filter is excluding everything. Do not delete the entry to make ' +
      'this pass — that removes the only thing distinguishing "clean" from "measured nothing".',
  ).toBeGreaterThanOrEqual(floor);
}
