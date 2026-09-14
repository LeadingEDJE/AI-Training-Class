import { useId } from 'react';
import { Button } from './ui';
import { fieldControlClass } from './ui-classes';
import { PAGE_SIZES, type PageSize } from './pagination-options';

/**
 * The paging controls every paginated Compass list shares.
 *
 * **The mockups contain no pagination at all** — they show a single unbroken table. These exist at the
 * owner's request and are therefore a design liberty the mockups' own banner permits, built from the
 * same primitives as everything else on the page so they do not read as bolted on.
 *
 * They live here rather than inside a screen because a second screen now needs them: the EDJEr
 * administration list shipped them first (feature 004) and the Team Directory needs exactly the same
 * pair. Copying sixty lines to get there is the duplication the fourth rule of simple design is about.
 */

/* ------------------------------------------------------------------------------- Pager */

interface PagerProps {
  /** Zero-based index of the first row shown, for the human-readable range. */
  firstIndex: number;
  shownCount: number;
  totalCount: number;
  currentPage: number;
  totalPages: number;
  /** What is being counted. Both forms are asked for rather than guessed — "EDJErs" is not "EDJEr" + s in every case a future list will need. */
  itemNoun: { singular: string; plural: string };
  onPage: (page: number) => void;
}

/**
 * The range summary, and the page controls when there is more than one page.
 *
 * The summary renders unconditionally because it is useful at any size — "Showing 1 to 3 of 3" answers
 * a question a bare table does not. The prev/next pair only appears when it can do something, which is
 * why choosing "All" removes it entirely.
 *
 * Within a page, the two buttons are **disabled rather than hidden** at each end. Hiding a control
 * makes the row reflow as you reach a boundary and leaves a keyboard user's focus somewhere
 * unpredictable.
 */
export function Pager({
  firstIndex,
  shownCount,
  totalCount,
  currentPage,
  totalPages,
  itemNoun,
  onPage,
}: PagerProps) {
  const from = firstIndex + 1;
  const to = firstIndex + shownCount;
  const noun = totalCount === 1 ? itemNoun.singular : itemNoun.plural;

  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      {/* A polite live region: the range is what tells someone their Next click did anything, and it is
          the only part of the screen that reliably changes on every page move. */}
      <p role="status" className="text-sm text-brand-gray-muted">
        Showing {from} to {to} of {totalCount} {noun}
      </p>

      {totalPages > 1 && (
        <nav aria-label="Pagination" className="flex flex-wrap items-center gap-2">
          <Button
            variant="secondary"
            size="small"
            disabled={currentPage <= 1}
            onClick={() => onPage(currentPage - 1)}
          >
            Previous
          </Button>
          <span className="text-sm text-brand-gray-muted">
            Page {currentPage} of {totalPages}
          </span>
          <Button
            variant="secondary"
            size="small"
            disabled={currentPage >= totalPages}
            onClick={() => onPage(currentPage + 1)}
          >
            Next
          </Button>
        </nav>
      )}
    </div>
  );
}

/* ----------------------------------------------------------------------- PageSizeField */

interface PageSizeFieldProps {
  /** The visible label, which is also the select's accessible name. Names what is being counted. */
  label: string;
  value: PageSize;
  onChange: (next: PageSize) => void;
}

/**
 * The rows-per-page select.
 *
 * `useId` rather than a fixed id, so two paginated lists on one screen cannot end up sharing one — a
 * defect that is invisible until something clicks a label.
 */
export function PageSizeField({ label, value, onChange }: PageSizeFieldProps) {
  const fieldId = useId();

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={fieldId} className="text-sm font-medium text-brand-gray">
        {label}
      </label>
      <select
        id={fieldId}
        value={String(value)}
        onChange={(event) =>
          onChange(event.target.value === 'all' ? 'all' : (Number(event.target.value) as PageSize))
        }
        className={fieldControlClass}
      >
        {PAGE_SIZES.map((size) => (
          <option key={String(size)} value={String(size)}>
            {size === 'all' ? 'All' : String(size)}
          </option>
        ))}
      </select>
    </div>
  );
}
