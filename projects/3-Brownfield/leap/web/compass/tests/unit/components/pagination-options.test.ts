import { describe, expect, it } from 'vitest';
import {
  DEFAULT_PAGE_SIZE,
  PAGE_SIZES,
  paginate,
} from '../../../src/components/pagination-options';

const ITEMS = Array.from({ length: 45 }, (_, index) => index + 1);

describe('PAGE_SIZES', () => {
  it('offers All last, so the option that removes paging is not buried mid-list', () => {
    expect(PAGE_SIZES[PAGE_SIZES.length - 1]).toBe('all');
  });

  it('defaults to a size it actually offers', () => {
    expect(PAGE_SIZES).toContain(DEFAULT_PAGE_SIZE);
  });
});

describe('paginate', () => {
  it('returns the requested page and the range it starts at', () => {
    const view = paginate(ITEMS, 20, 2);

    expect(view.rows).toEqual(ITEMS.slice(20, 40));
    expect(view.firstIndex).toBe(20);
    expect(view.currentPage).toBe(2);
    expect(view.totalPages).toBe(3);
  });

  it('returns a short final page rather than padding it', () => {
    const view = paginate(ITEMS, 20, 3);

    expect(view.rows).toHaveLength(5);
    expect(view.firstIndex).toBe(40);
  });

  it('clamps a page past the end back to the last one that has rows', () => {
    // The case this exists for: a search narrows the results while the viewer is on page 4. Without
    // the clamp the screen shows a header with nothing under it, which reads as "nothing matched".
    const view = paginate(ITEMS, 20, 9);

    expect(view.currentPage).toBe(3);
    expect(view.rows).toHaveLength(5);
  });

  it('clamps a page below the first', () => {
    const view = paginate(ITEMS, 20, 0);

    expect(view.currentPage).toBe(1);
    expect(view.firstIndex).toBe(0);
  });

  it('returns every row as one page when the size is All', () => {
    const view = paginate(ITEMS, 'all', 3);

    expect(view.rows).toHaveLength(ITEMS.length);
    expect(view.totalPages).toBe(1);
    expect(view.currentPage).toBe(1);
    expect(view.firstIndex).toBe(0);
  });

  it('reports one page for an empty collection, so the summary never reads "Page 1 of 0"', () => {
    const view = paginate([], 20, 1);

    expect(view.rows).toEqual([]);
    expect(view.totalPages).toBe(1);
    expect(view.currentPage).toBe(1);
  });

  it('does not hand back the caller’s array, so a page cannot be mutated into the source', () => {
    const view = paginate(ITEMS, 'all', 1);

    expect(view.rows).not.toBe(ITEMS);
  });
});
