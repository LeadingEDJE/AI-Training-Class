import {
  Children,
  cloneElement,
  useEffect,
  useId,
  useState,
  type Key,
  type ReactElement,
  type ReactNode,
} from 'react';
import { createPortal } from 'react-dom';
import { useIsNarrowViewport } from '../hooks/useIsNarrowViewport';
import { ACCENT_SURFACE, CONTROL_BORDER, buttonClassName } from './ui-classes';

/* ------------------------------------------------------------------------- PageHeader */

interface PageHeaderProps {
  title: string;
  breadcrumb?: ReactNode;
  description?: string;
  status?: ReactNode;
  actions?: ReactNode;
}

/** The mockups' `.pagehead`, with the breadcrumb wrapped in a plain `div` matching the mockup exactly. */
export function PageHeader({ title, breadcrumb, description, status, actions }: PageHeaderProps) {
  return (
    <div className="flex flex-col gap-1">
      {breadcrumb !== undefined && (
        <nav aria-label="Breadcrumb" className="text-xs text-brand-gray-muted">
          {breadcrumb}
        </nav>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
        {status}
        {actions !== undefined && <div className="ml-auto flex flex-wrap gap-2">{actions}</div>}
      </div>

      {description !== undefined && <p className="text-sm text-brand-gray-muted">{description}</p>}
    </div>
  );
}

/* ------------------------------------------------------------------------------- Card */

interface CardProps {
  title: string;
  description?: string;
  action?: ReactNode;
  footnote?: string;
  children: ReactNode;
}

/** The mockups' `.card`, as a labelled region. The tick before the heading announces "image" for screen readers. */
export function Card({ title, description, action, footnote, children }: CardProps) {
  const headingId = useId();
  const descriptionId = useId();
  const footnoteId = useId();

  const describedBy = [
    ...(description !== undefined ? [descriptionId] : []),
    ...(footnote !== undefined ? [footnoteId] : []),
  ].join(' ');

  return (
    <section
      aria-labelledby={headingId}
      {...(describedBy !== '' && { 'aria-describedby': describedBy })}
      className="rounded-lg border border-brand-taupe/40 bg-white p-5"
    >
      <div className="flex items-center gap-2">
        <span aria-hidden="true" className="h-4 w-1 flex-none rounded-sm bg-brand-green" />
        <h2
          id={headingId}
          className="min-w-0 flex-1 text-lg font-semibold tracking-tight break-words"
        >
          {title}
        </h2>
        {action !== undefined && <span className="flex-none">{action}</span>}
      </div>

      {description !== undefined && (
        <p id={descriptionId} className="mt-1 text-sm text-brand-gray-muted">
          {description}
        </p>
      )}

      <div className="mt-4">{children}</div>

      {footnote !== undefined && (
        <p id={footnoteId} className="mt-2.5 text-xs text-brand-gray-muted">
          {footnote}
        </p>
      )}
    </section>
  );
}

/* ------------------------------------------------------------------------------ Panel */

/** The mockups' `.card` surface, rendered as its own labelled `region` — the same accessible shape as {@link Card}. */
export function Panel({ children }: { children: ReactNode }) {
  return <div className="rounded-lg border border-brand-taupe/40 bg-white p-5">{children}</div>;
}

/* ----------------------------------------------------------------------------- Button */

interface ButtonProps {
  children: ReactNode;
  variant?: 'primary' | 'secondary';
  size?: 'default' | 'small';
  /** Defaults to `submit`, matching the native HTML button default. */
  type?: 'button' | 'submit';
  disabled?: boolean;
  onClick?: () => void;
  ariaLabel?: string;
}

/** The mockups' `.btn` as a real `<button>`. Appearance comes from {@link buttonClassName}. */
export function Button({
  children,
  variant = 'primary',
  size = 'default',
  type = 'button',
  disabled = false,
  onClick,
  ariaLabel,
}: ButtonProps) {
  return (
    <button
      type={type}
      disabled={disabled}
      onClick={onClick}
      {...(ariaLabel !== undefined && { 'aria-label': ariaLabel })}
      className={buttonClassName({ variant, size })}
    >
      {children}
    </button>
  );
}

/* ------------------------------------------------------------------------------ Alert */

export function Alert({ children }: { children: ReactNode }) {
  return (
    <div
      role="alert"
      className="rounded border border-brand-gray/70 bg-brand-taupe/10 px-3 py-2 text-sm"
    >
      {children}
    </div>
  );
}

/* -------------------------------------------------------------------------- StatusPill */

interface StatusPillProps {
  tone: 'affirmative' | 'neutral';
  label: string;
}

/** The mockups' `.pill`, unchanged — colour alone still distinguishes affirmative from neutral. */
export function StatusPill({ tone, label }: StatusPillProps) {
  const colourway =
    tone === 'affirmative'
      ? 'bg-brand-green-tint text-brand-green-accent'
      : 'bg-brand-gray-tint text-brand-gray';

  return (
    <span
      className={`inline-block rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${colourway}`}
    >
      {label}
    </span>
  );
}

/* ------------------------------------------------------------------------------ Toggle */

interface ToggleProps {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
  hint?: string;
  labelHidden?: boolean;
  stateLabels?: { on: string; off: string };
  stateLabelHidden?: boolean;
}

/** The mockups' `.tswitch`, rendered as a plain `<div class="tswitch">` exactly as drawn. */
export function Toggle({
  label,
  checked,
  onChange,
  disabled = false,
  hint,
  labelHidden = false,
  stateLabels,
  stateLabelHidden = false,
}: ToggleProps) {
  const hintId = useId();
  const stateWord = checked ? (stateLabels?.on ?? 'On') : (stateLabels?.off ?? 'Off');

  return (
    <span className="flex flex-col gap-1">
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        aria-label={`${label} ${stateWord}`}
        {...(hint !== undefined && { 'aria-describedby': hintId })}
        onClick={() => onChange(!checked)}
        className="flex items-center gap-2 text-sm font-medium text-brand-text disabled:cursor-not-allowed disabled:opacity-60"
      >
        <span
          aria-hidden="true"
          className={`relative h-5 w-9 flex-none rounded-full border ${
            checked ? ACCENT_SURFACE : `${CONTROL_BORDER} bg-brand-gray-tint`
          }`}
        >
          <span
            className={`absolute top-0.5 h-3.5 w-3.5 rounded-full border ${CONTROL_BORDER} bg-white ${
              checked ? 'left-4.5' : 'left-0.5'
            }`}
          />
        </span>
        <span className={labelHidden ? 'sr-only' : undefined}>{label}</span>
        <span className={stateLabelHidden ? 'sr-only' : 'text-brand-gray-muted'}>{stateWord}</span>
      </button>

      {hint !== undefined && (
        <span id={hintId} className="text-xs text-brand-gray-muted">
          {hint}
        </span>
      )}
    </span>
  );
}

/* --------------------------------------------------------------------------- FormField */

interface FormFieldProps {
  label: string;
  children: (id: string) => ReactElement;
  required?: boolean;
  hint?: string;
  error?: string;
}

/** The mockups' `.field`. Each call site is responsible for spreading `aria-required`/`aria-invalid`/`aria-describedby` onto its own control. */
export function FormField({ label, children, required = false, hint, error }: FormFieldProps) {
  const fieldId = useId();
  const hintId = useId();
  const errorId = useId();

  const control = children(fieldId);

  const existingDescribedBy = (control.props as { 'aria-describedby'?: string })[
    'aria-describedby'
  ];
  const describedBy =
    [
      existingDescribedBy,
      hint !== undefined ? hintId : undefined,
      error !== undefined ? errorId : undefined,
    ]
      .filter((id): id is string => id !== undefined)
      .join(' ') || undefined;

  return (
    <div className="flex flex-col gap-1">
      <label htmlFor={fieldId} className="text-sm font-medium text-brand-gray">
        {label}
        {required && (
          <span aria-hidden="true" className="ml-1 text-brand-danger">
            *
          </span>
        )}
      </label>

      {cloneElement(control, {
        ...(required && { 'aria-required': true }),
        ...(error !== undefined && { 'aria-invalid': true }),
        ...(describedBy !== undefined && { 'aria-describedby': describedBy }),
      } as Partial<typeof control.props>)}

      {hint !== undefined && (
        <span id={hintId} className="text-xs text-brand-gray-muted">
          {hint}
        </span>
      )}

      {error !== undefined && (
        <span id={errorId} role="alert" className="text-xs font-medium text-brand-danger">
          {error}
        </span>
      )}
    </div>
  );
}

/* ---------------------------------------------------------------------------- FormGrid */

/** The mockups' `.formgrid`, breaking at the mockup's own `@media(max-width:900px)`. */
export function FormGrid({ children }: { children: ReactNode }) {
  return <div className="grid grid-cols-1 gap-x-7 gap-y-4 md:grid-cols-2">{children}</div>;
}

/* --------------------------------------------------------------- ScrollableRegion */

interface ScrollableRegionProps {
  label: string;
  children: ReactNode;
}

/** A horizontally scrollable `region` landmark that a keyboard can reach, per issue #83. */
export function ScrollableRegion({ label, children }: ScrollableRegionProps) {
  return (
    <div className="relative">
      <div
        role="group"
        aria-label={label}
        tabIndex={0}
        className="overflow-x-auto focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand-gray"
      >
        {children}
      </div>

      <div
        aria-hidden="true"
        data-scroll-edge-mask=""
        className="pointer-events-none absolute inset-y-0 right-0 w-6 bg-gradient-to-l from-white to-transparent"
      />
    </div>
  );
}

/* ------------------------------------------------------------------------------- Table */

/** A column whose header orders the table. */
export interface TableSortableColumn {
  label: string;
  sortDirection?: 'ascending' | 'descending';
  onSort: () => void;
}

/** A header that is either plain text or a sort control. */
export type TableColumn = string | TableSortableColumn;

interface TableProps {
  caption: string;
  columns: TableColumn[];
  emptyMessage?: string;
  children?: ReactNode;
}

/** The mockups' `table`, with a caption. An empty table renders the header row with no body rows under it. */
export function Table({ caption, columns, emptyMessage, children }: TableProps) {
  const isEmpty = Children.toArray(children).length === 0;

  if (isEmpty && emptyMessage !== undefined) {
    return <p className="text-sm text-brand-gray-muted">{emptyMessage}</p>;
  }

  return (
    <ScrollableRegion label={caption}>
      <table className="w-full border-collapse text-sm">
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr>
            {columns.map((column, columnIndex) => (
              <th
                key={columnIndex}
                scope="col"
                {...(typeof column !== 'string' && {
                  'aria-sort': column.sortDirection ?? 'none',
                })}
                className="border-b-2 border-brand-taupe/40 bg-brand-gray-tint px-3 py-2 text-left text-xs font-semibold tracking-wide whitespace-nowrap text-brand-gray uppercase"
              >
                {typeof column === 'string' ? column : <SortButton column={column} />}
              </th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-brand-taupe/40">{children}</tbody>
      </table>
    </ScrollableRegion>
  );
}

function SortButton({ column }: { column: TableSortableColumn }) {
  const caret = column.sortDirection === 'ascending' ? '▲' : '▼';

  return (
    <button
      type="button"
      onClick={column.onSort}
      aria-label={`Sort by ${column.label.toLowerCase()}`}
      className="flex items-center gap-1 rounded text-xs font-semibold tracking-wide uppercase underline-offset-2 hover:underline"
    >
      {column.label}
      {column.sortDirection !== undefined && (
        <span aria-hidden="true" className="text-[0.625rem] leading-none">
          {caret}
        </span>
      )}
    </button>
  );
}

/* ------------------------------------------------------------------- StackedRows */

interface StackedRowsProps<TRow> {
  caption: string;
  columns: TableColumn[];
  rows: TRow[];
  cells: (row: TRow) => ReactNode[];
  rowKey: (row: TRow, index: number) => Key;
  cellClassNames?: (string | undefined)[];
  emptyMessage?: string;
}

/** A table at >= `md`, and stacked label-value cards below it. Both branches render simultaneously, toggled by CSS. */
export function StackedRows<TRow>({
  caption,
  columns,
  rows,
  cells,
  rowKey,
  cellClassNames,
  emptyMessage,
}: StackedRowsProps<TRow>) {
  const isNarrow = useIsNarrowViewport();

  if (rows.length === 0 && emptyMessage !== undefined) {
    return <p className="text-sm text-brand-gray-muted">{emptyMessage}</p>;
  }

  const labels = columns.map((column) => (typeof column === 'string' ? column : column.label));

  if (!isNarrow) {
    return (
      <Table caption={caption} columns={columns} emptyMessage={emptyMessage}>
        {rows.map((row, index) => (
          <tr key={rowKey(row, index)} className="border-b border-brand-taupe/40">
            {cells(row).map((cell, cellIndex) => {
              const extra = cellClassNames?.[cellIndex];
              return (
                <td
                  key={cellIndex}
                  className={extra ? `px-3 py-2 ${extra}` : 'px-3 py-2'}
                >
                  {cell}
                </td>
              );
            })}
          </tr>
        ))}
      </Table>
    );
  }

  return (
    <ul aria-label={caption} className="flex list-none flex-col gap-3">
      {rows.map((row, index) => {
        const values = cells(row);
        return (
          <li
            key={rowKey(row, index)}
            className="rounded-lg border border-brand-taupe/40 bg-white p-3"
          >
            <dl className="flex flex-col gap-1 text-sm">
              {labels.map((label, labelIndex) => (
                <div key={labelIndex} className="flex flex-wrap gap-x-2">
                  <dt className="font-semibold text-brand-gray">{label}</dt>
                  <dd
                    className={
                      cellClassNames?.[labelIndex]
                        ? `text-brand-text ${cellClassNames[labelIndex]}`
                        : 'text-brand-text'
                    }
                  >
                    {values[labelIndex]}
                  </dd>
                </div>
              ))}
            </dl>
          </li>
        );
      })}
    </ul>
  );
}

/* ------------------------------------------------------------------------------- Modal */

interface ModalProps {
  title: string;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
}

/** The controls inside the dialog that a Tab can land on, reproducing the browser's own tab order exactly. */
const FOCUSABLE_IN_DIALOG = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(', ');

/** The mockups' `.modalwrap`/`.modal`, rendered as a plain `div` overlay with no ARIA role. Focus management is left to the browser default. */
export function Modal({ title, onClose, children, footer }: ModalProps) {
  const titleId = useId();
  const [dialog, setDialog] = useState<HTMLDivElement | null>(null);

  useEffect(() => {
    if (dialog === null) {
      return;
    }

    const node = dialog;
    const opener = document.activeElement;
    node.focus();

    let shiftHeld = false;

    function controls(): HTMLElement[] {
      return [...node.querySelectorAll<HTMLElement>(FOCUSABLE_IN_DIALOG)];
    }

    function focusRingEnd(ring: HTMLElement[]): void {
      ring[shiftHeld ? ring.length - 1 : 0].focus();
    }

    function handleTab(event: KeyboardEvent) {
      if (event.key !== 'Tab') {
        return;
      }

      shiftHeld = event.shiftKey;
      const ring = controls();
      const index = ring.findIndex((control) => control === document.activeElement);
      const edge = event.shiftKey ? 0 : ring.length - 1;

      if (index !== -1 && index !== edge) {
        return;
      }

      event.preventDefault();
      focusRingEnd(ring);
    }

    function handleFocusIn(event: FocusEvent) {
      if (node.contains(event.target as Node)) {
        return;
      }

      focusRingEnd(controls());
    }

    document.addEventListener('keydown', handleTab);
    document.addEventListener('focusin', handleFocusIn);

    return () => {
      document.removeEventListener('keydown', handleTab);
      document.removeEventListener('focusin', handleFocusIn);
      if (opener instanceof HTMLElement) {
        opener.focus();
      }
    };
  }, [dialog]);

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        onClose();
      }
    }

    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
    >
      <div
        ref={setDialog}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
        onSubmit={(event) => event.stopPropagation()}
        className="w-full max-w-lg rounded-lg bg-white shadow-lg"
      >
        <div className="flex items-center justify-between gap-3 border-b border-brand-taupe/40 px-5 py-3">
          <h2 id={titleId} className="text-lg font-semibold tracking-tight">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded text-brand-gray hover:text-brand-text"
          >
            <span aria-hidden="true">✕</span>
          </button>
        </div>

        <div className="p-5">{children}</div>

        {footer !== undefined && (
          <div className="flex justify-end gap-2 border-t border-brand-taupe/40 px-5 py-3">
            {footer}
          </div>
        )}
      </div>
    </div>,
    document.body,
  );
}
