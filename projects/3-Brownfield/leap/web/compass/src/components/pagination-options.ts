/** The page sizes offered, in order, with `'all'` last. Kept in this file so `AdminLayout` can import it without pulling in `Pager`. */
export const PAGE_SIZES = [20, 50, 100, 'all'] as const;

/** One of {@link PAGE_SIZES}. */
export type PageSize = (typeof PAGE_SIZES)[number];

export const DEFAULT_PAGE_SIZE: PageSize = 20;

export interface PaginatedView<T> {
  rows: T[];
  firstIndex: number;
  currentPage: number;
  totalPages: number;
}

/** Slices a collection into the requested page. Throws when `requestedPage` is out of range. */
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
