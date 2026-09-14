// Feature 011, T010 — Gate A verified BY CONSTRUCTION.
//
// This spec tests the measurement helper, not the application. It exists because of a rule this
// repository learned the expensive way: *a gate that has never failed has never run.* Eight gates were
// found silently failing open across Phases 48-49, and the whole of feature 011 traces to a
// WCAG sweep that covered every screen while excluding the selector that fails. A stranding assertion
// that cannot be shown to go red is worth nothing, so it is shown here.
//
// Hermetic on purpose: `page.setContent` builds each fixture, so nothing here depends on the app, the
// API, the seed, or a route. A failure is a defect in the helper, never a defect in Compass.
import { expect, test } from '@playwright/test';

import {
  GLYPH_THRESHOLD_PX,
  SCROLL_REGION_SELECTOR,
  measureScrollRegions,
} from './helpers/scroll-region';

/** The visible width of every fixture's scroll region. */
const VISIBLE_WIDTH_PX = 300;

interface FixtureCell {
  /** Exact rendered width. */
  widthPx: number;
  html: string;
}

/**
 * Builds a page containing one scroll region of precisely known geometry.
 *
 * Three things here are load-bearing, and each was established by measuring rather than assuming — an
 * earlier draft of this file got all three wrong and produced fixtures whose real overflow was 36px
 * when it declared 0:
 *
 * 1. **Inline `<style>` carries the geometry; the class carries only the selector.**
 *    `.overflow-x-auto` is a Tailwind utility and Tailwind's stylesheet is not loaded into a
 *    `setContent` document, so a fixture relying on the class for its overflow behaviour renders a
 *    region that does not scroll — and every assertion below would pass for the wrong reason.
 * 2. **`table-layout: fixed` plus an explicit table width plus `white-space: nowrap` and `padding: 0`
 *    on cells.** Without all four, a cell's min-content width silently widens the table and the actual
 *    overflow stops matching the declared one.
 * 3. **`margin: 0` on `html, body`.** The region's viewport coordinates feed the "beyond the visible
 *    edge" comparison, so a default body margin shifts every boundary by 8px.
 */
function fixture(cells: FixtureCell[]): string {
  const tableWidth = cells.reduce((total, cell) => total + cell.widthPx, 0);

  return `
    <style>
      html, body { margin: 0; padding: 0; }
      ${SCROLL_REGION_SELECTOR} { overflow-x: auto; width: ${VISIBLE_WIDTH_PX}px; }
      table { border-collapse: collapse; table-layout: fixed; width: ${tableWidth}px; }
      td { padding: 0; margin: 0; white-space: nowrap; overflow: hidden; box-sizing: border-box; }
    </style>
    <div class="${SCROLL_REGION_SELECTOR.slice(1)}">
      <table>
        <tbody>
          <tr>
            ${cells.map((cell) => `<td style="width: ${cell.widthPx}px">${cell.html}</td>`).join('')}
          </tr>
        </tbody>
      </table>
    </div>
  `;
}

/**
 * The four shapes, with the geometry each one produces (measured 2026-08-21).
 *
 * `strand` deliberately puts its second cell STRADDLING the edge — that is the real-world shape, where
 * `client-view`'s End Date column is cut mid-value. The other three put the second cell entirely past
 * the edge, so "is anything focusable beyond it" is unambiguous.
 */
const SHAPES = {
  /** 120px hidden, nothing focusable past the edge, a date cut mid-value. */
  strand: [
    { widthPx: 260, html: 'visible cell' },
    { widthPx: 160, html: '09/09/2026' },
  ],
  /** 120px hidden, but a link sits wholly beyond the edge — a keyboard can reach it. */
  reachable: [
    { widthPx: VISIBLE_WIDTH_PX, html: 'visible cell' },
    { widthPx: 120, html: '<a href="#somewhere">reachable by keyboard</a>' },
  ],
  /** Exactly the threshold: hidden, unreachable, and still not a defect. */
  belowThreshold: [
    { widthPx: VISIBLE_WIDTH_PX, html: 'visible cell' },
    { widthPx: GLYPH_THRESHOLD_PX, html: '&nbsp;' },
  ],
  /** Fits exactly — no overflow at all. */
  noOverflow: [{ widthPx: VISIBLE_WIDTH_PX, html: 'visible cell' }],
} satisfies Record<string, FixtureCell[]>;

test.describe('@compass Gate A — the stranding criterion can actually fail [critical]', () => {
  test.beforeEach(async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
  });

  test('a wide region with NOTHING focusable past the edge is reported as stranding', async ({
    page,
  }) => {
    await page.setContent(fixture(SHAPES.strand));

    const regions = await measureScrollRegions(page);

    expect(regions, 'the fixture renders exactly one overflowing region').toHaveLength(1);
    expect(regions[0].hiddenPx).toBe(120);
    expect(regions[0].focusablesBeyondEdge).toBe(0);
    expect(regions[0].strands, 'this is the combination T118 named as stranding content').toBe(
      true,
    );
  });

  test('a region WITH a focusable element past the edge is not stranding', async ({ page }) => {
    await page.setContent(fixture(SHAPES.reachable));

    const regions = await measureScrollRegions(page);

    expect(regions).toHaveLength(1);
    expect(regions[0].hiddenPx).toBe(120);
    expect(regions[0].focusablesBeyondEdge, 'the link sits wholly past the visible edge').toBe(1);
    expect(
      regions[0].strands,
      'a browser scrolls a focused element into view, so the content is reachable — this is exactly ' +
        'the case T118 investigated and correctly cleared. Same hidden width as the test above; the ' +
        'ONLY difference is the keyboard path, which is what the criterion turns on.',
    ).toBe(false);
  });

  test(`an overflow of exactly ${GLYPH_THRESHOLD_PX}px is not stranding, even with no keyboard path`, async ({
    page,
  }) => {
    await page.setContent(fixture(SHAPES.belowThreshold));

    const regions = await measureScrollRegions(page);

    expect(regions).toHaveLength(1);
    expect(regions[0].hiddenPx).toBe(GLYPH_THRESHOLD_PX);
    expect(regions[0].focusablesBeyondEdge).toBe(0);
    expect(
      regions[0].strands,
      `T118 cleared a ${GLYPH_THRESHOLD_PX}px case as "a layout rounding artifact, not lost content". ` +
        'Lowering the threshold re-opens a finding that was correctly retracted, and would fail this ' +
        "suite on admin/lookups' invoice-frequency table.",
    ).toBe(false);
  });

  test('a region that does not overflow is not measured at all', async ({ page }) => {
    await page.setContent(fixture(SHAPES.noOverflow));

    expect(
      await measureScrollRegions(page),
      'only overflowing regions are returned — which is why the region FLOOR is asserted separately, ' +
        'or a screen whose table stopped rendering would pass every stranding check vacuously',
    ).toEqual([]);
  });

  test('a clipped cell is reported with its text and how far it is cut', async ({ page }) => {
    await page.setContent(fixture(SHAPES.strand));

    const [region] = await measureScrollRegions(page);

    expect(
      region.straddlingCells.length,
      'the failure message must identify WHICH value is cut, not merely that one is',
    ).toBeGreaterThan(0);
    expect(region.straddlingCells[0].text).toContain('09/09/2026');
    expect(region.straddlingCells[0].cutPx).toBeGreaterThan(0);
  });

  test('the page itself does not scroll sideways, so Gate E stays distinct from Gate A', async ({
    page,
  }) => {
    await page.setContent(fixture(SHAPES.strand));

    const [region] = await measureScrollRegions(page);

    expect(
      region.pageOverflows,
      "the overflow is contained inside the region — T118's stated design goal, and the thing a " +
        'remedy must not trade away',
    ).toBe(false);
  });
});
