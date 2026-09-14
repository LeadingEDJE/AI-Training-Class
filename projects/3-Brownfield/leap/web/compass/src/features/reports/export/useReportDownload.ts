import { useCallback, useState } from 'react';
import { apiFetch, apiUrl } from '../../../lib/api-url';

/**
 * Pulls a report export from the API and hands it to the browser as a file (issue #337).
 *
 * **Why this is a fetch-and-blob rather than an `<a href>` download.** A plain anchor would be
 * simpler, and it would work in local dev — but a deployed Compass can be a different origin from
 * the API, and only `apiFetch` sets `credentials: 'include'` so the HttpOnly session cookie travels.
 * That is non-negotiable for every API call, and it also
 * buys the 401 -> `session-expired` re-login handling that a raw navigation skips.
 *
 * **The filename comes from the server when it says one.** `Content-Disposition` carries a name the
 * service already composed (it is the same slug used inside the zip), so honouring it keeps one
 * naming decision in one place. `fallbackFileName` covers the case where the header is unreadable —
 * notably a cross-origin response, where the browser exposes no `Content-Disposition` unless the API
 * opts in via `Access-Control-Expose-Headers`.
 */
export interface ReportDownload {
  /** Starts a download of `path`, naming the file `fallbackFileName` if the server does not. */
  download: (path: string, fallbackFileName: string) => Promise<void>;
  /** True while a request is in flight, so the caller can disable its control. */
  isDownloading: boolean;
  /** A human-readable failure, or null. Cleared when a new download starts. */
  error: string | null;
}

/** The message shown when an export request fails. Asserted verbatim by the unit tests. */
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
      // A network failure or an aborted request. The message is deliberately the same as the
      // non-OK case: the user's next action is identical either way, and distinguishing them here
      // would leak transport detail into a report screen.
      setError(EXPORT_FAILED_MESSAGE);
    } finally {
      setIsDownloading(false);
    }
  }, []);

  return { download, isDownloading, error };
}

/**
 * Reads the filename out of a `Content-Disposition` header.
 *
 * Prefers RFC 5987's `filename*` when present, because that is the form that survives non-ASCII
 * characters — and these filenames can carry a client name. Returns null when the header is absent
 * or carries no filename, which is the caller's signal to use its fallback.
 */
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
 * The object URL is revoked immediately after the synthetic click. Skipping that leaks the whole
 * blob for the lifetime of the document, which on a large report is not a rounding error.
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
