import { useId } from 'react';
import { Button } from './ui';
import { fieldControlClass } from './ui-classes';
import { PAGE_SIZES, type PageSize } from './pagination-options';

/* ------------------------------------------------------------------------------- Pager */

interface PagerProps {
  firstIndex: number;
  shownCount: number;
  totalCount: number;
  currentPage: number;
  totalPages: number;
  itemNoun: { singular: string; plural: string };
  onPage: (page: number) => void;
}

/** The range summary and page controls. Both buttons are hidden, not disabled, at each end of the range. */
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
  label: string;
  value: PageSize;
  onChange: (next: PageSize) => void;
}

/** The rows-per-page select. Uses a fixed id since only one instance ever renders per screen. */
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
