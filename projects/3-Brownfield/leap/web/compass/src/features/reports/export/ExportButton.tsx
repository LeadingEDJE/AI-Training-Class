import { Button } from '../../../components/ui';
import { useReportDownload } from './useReportDownload';

export interface ExportButtonProps {
  /** The API path to pull, e.g. `/api/compass/reports/assignment-duration/export`. */
  path: string;
  /** The filename to use if the server sends no `Content-Disposition`. */
  fallbackFileName: string;
  /** The button's visible text. Doubles as its accessible name. */
  label: string;
  /**
   * Screen-reader-only suffix, for when several buttons on one screen share a label.
   *
   * The Availability Report has three "Export CSV" buttons, one per section. Visually the column of
   * section headings tells you which is which; a screen-reader user tabbing between them would hear
   * three identical names, so each passes its section here (WCAG 2.4.6, AC-NFR-5).
   */
  accessibleSuffix?: string;
  /** Visual weight. Sections use `secondary`; the report-level "Export All" is the primary action. */
  variant?: 'primary' | 'secondary';
}

/**
 * Downloads one report export (issue #337).
 *
 * Renders its own failure inline rather than lifting it to the page: an export failure is local to the
 * control that caused it, and a page-level banner would leave the user guessing which of the four
 * buttons on the Availability Report had failed.
 */
export function ExportButton({
  path,
  fallbackFileName,
  label,
  accessibleSuffix,
  variant = 'secondary',
}: ExportButtonProps) {
  const { download, isDownloading, error } = useReportDownload();

  return (
    <span className="inline-flex flex-col items-end gap-1">
      <Button
        variant={variant}
        size="small"
        disabled={isDownloading}
        onClick={() => void download(path, fallbackFileName)}
        // One explicit name rather than a visible label plus an `sr-only` span. The span version
        // came out as "Export CSVConfirmed rollouts" in jsdom and "Export CSV : Confirmed rollouts"
        // in Chrome -- the engines join adjacent text nodes differently, so no single assertion held
        // in both. The visible text stays the name's prefix (WCAG 2.5.3).
        ariaLabel={accessibleSuffix === undefined ? undefined : `${label}: ${accessibleSuffix}`}
      >
        {label}
      </Button>

      {error !== null && (
        <span role="alert" className="text-xs font-medium text-brand-danger">
          {error}
        </span>
      )}
    </span>
  );
}
