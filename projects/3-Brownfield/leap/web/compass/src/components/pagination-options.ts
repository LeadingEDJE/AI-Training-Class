/**
 * The page sizes every paginated Compass list offers, and the pure slice arithmetic behind them.
 *
 * **Why this is a separate file from `pagination.tsx`.** `react-refresh/only-export-components`
 * requires a component module to export components only, so the option list and the `paginate`
 * helper cannot live beside `Pager` and `PageSizeField`. `ui.tsx` / `ui-classes.ts` and
 * `AdminLayout.tsx` / `admin-sections.ts` are the same split for the same reason — this is the
 * established shape in this SPA, not a new one.
 */

/**
 * The page sizes offered, in order, with `'all'` last.
 *
 * `'all'` is a real option rather than a very large number: it removes the page controls entirely,
 * which is the behaviour someone choosing it wants, and it cannot drift out of step with the
 * collection's size the way a sentinel like 9999 would.
 */
export const PAGE_SIZES = [20, 50, 100, 'all'] as const;

/** One of {@link PAGE_SIZES}. */
export type PageSize = (typeof PAGE_SIZES)[number];

/** The owner's default. */
export const DEFAULT_PAGE_SIZE: PageSize = 20;

/** One page of a collection, plus what a {@link PageSize} implies about the rest of it. */
export interface PaginatedView<T> {
  /** The rows to render. */
  rows: T[];
  /** Zero-based index of the first row shown, for the human-readable range. */
  firstIndex: number;
  /** The page actually shown, which is {@link paginate}'s clamp of the one requested. */
  currentPage: number;
  /** At least 1, so "Page 1 of 1" is what an empty or single-page collection reads. */
  totalPages: number;
}

/**
 * Slices a collection into the requested page.
 *
 * **The requested page is CLAMPED, not trusted.** A search that narrows the results while the viewer
 * is on page 4 would otherwise leave the screen on a page that no longer has any rows — a header
 * with nothing under it, which reads as "nothing matched" when the matches are three pages back.
 * Clamping here rather than in an effect also avoids the effect-that-calls-setState shape
 * `react-hooks/set-state-in-effect` rejects, which the EDJEr form had to be restructured to remove.
 *
 * Pure and total: every combination of empty input, `'all'`, and an out-of-range page returns a
 * coherent view rather than an empty page nobody asked for.
 */
export function paginate<T>(
  items: readonly T[],
  pageSize: PageSize,
  requestedPage: number,
): PaginatedView<T> {
  const totalPages = pageSize === 'all' ? 1 : Math.max(1, Math.ceil(items.length / pageSize));
  const currentPage = Math.min(Math.max(1, requestedPage), totalPages);
  const firstIndex = pageSize === 'all' ? 0 : (currentPage - 1) * pageSize;

  return {
    rows: pageSize === 'all' ? [...items] : items.slice(firstIndex, firstIndex + pageSize),
    firstIndex,
    currentPage,
    totalPages,
  };
}
