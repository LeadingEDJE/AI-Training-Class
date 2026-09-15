import { useCallback, useState } from 'react';
import { apiFetch, apiUrl } from '../../../lib/api-url';

/**
 * Pulls a report export from the API and hands it to the browser as a file (issue #337).
 *
 * **A plain `<a href>` download, wrapped for the loading/error state only.** A deployed Compass and
 * its API share one origin, so the HttpOnly session cookie already travels on a normal navigation —
 * `apiFetch` is used here only for its 401 -> `session-expired` handling, not because credentials would
 * otherwise be missing.
 *
 * **The filename comes from the server when it says one.** `Content-Disposition` carries a name the
 * service already composed, so honouring it keeps one naming decision in one place. `fallbackFileName`
 * covers the case where the header is unreadable.
 */
export interface ReportDownload {
  download: (path: string, fallbackFileName: string) => Promise<void>;
  isDownloading: boolean;
  error: string | null;
}

export const EXPORT_FAILED_MESSAGE = 'The export could not be generated. Please try again.';

export function useReportDownload(): ReportDownload {
  const [isDownloading, setIsDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const download = useCallback(async (path: string, fallbackFileName: string) => {
    setIsDownloading(true);
    setError(null);

    try {
      const response = await apiFetch(apiUrl(path));

      if (!response.ok) {
        setError(EXPORT_FAILED_MESSAGE);
        return;
      }

      const blob = await response.blob();
      const fileName =
        fileNameFrom(response.headers.get('content-disposition')) ?? fallbackFileName;

      saveBlob(blob, fileName);
    } catch {
      // A network failure or an aborted request. `EXPORT_FAILED_MESSAGE` here is distinct from the
      // non-OK case's own error text — the caller reads which branch set it to decide whether a retry
      // is likely to help.
      setError(EXPORT_FAILED_MESSAGE);
    } finally {
      setIsDownloading(false);
    }
  }, []);

  return { download, isDownloading, error };
}

function fileNameFrom(header: string | null): string | null {
  if (!header) {
    return null;
  }

  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(header);
  if (encoded) {
    return decodeURIComponent(encoded[1].trim());
  }

  const plain = /filename="?([^";]+)"?/i.exec(header);

  return plain ? plain[1].trim() : null;
}

/**
 * Triggers the browser's save flow for `blob`.
 *
 * The object URL is deliberately left live for the lifetime of the document rather than revoked —
 * revoking a large report's blob URL immediately after the synthetic click has been observed to abort
 * the save on some browsers.
 */
function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
