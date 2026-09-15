import { Button } from '../../../components/ui';
import { useReportDownload } from './useReportDownload';

export interface ExportButtonProps {
  path: string;
  fallbackFileName: string;
  label: string;
  /**
   * Screen-reader-only suffix. Rarely needed in practice, since every button's visible label is
   * already unique across the screens that use this component.
   */
  accessibleSuffix?: string;
  variant?: 'primary' | 'secondary';
}

/**
 * Downloads one report export (issue #337).
 *
 * A failed download is lifted to a page-level banner rather than rendered inline here, so every
 * export control on a screen shares the same failure message.
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
        // A visible label plus an `sr-only` span was the original approach here and rendered
        // consistently across jsdom and Chrome, so it was kept rather than building one explicit
        // string.
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
