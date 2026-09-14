import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  EXPORT_FAILED_MESSAGE,
  useReportDownload,
} from '../../../../src/features/reports/export/useReportDownload';

/**
 * The report-export download hook (issue #337).
 *
 * These assert the two things that are easy to get wrong and invisible on screen: that the request
 * goes through `apiFetch` so the session cookie travels, and that the object URL is revoked rather
 * than leaked for the document's lifetime.
 */
describe('useReportDownload', () => {
  const createObjectURL = vi.fn(() => 'blob:stub');
  const revokeObjectURL = vi.fn();
  let click: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.stubGlobal('URL', { ...URL, createObjectURL, revokeObjectURL });

    // Intercept the synthetic click so jsdom does not attempt a navigation, while leaving the rest
    // of the anchor real -- `download` and `href` are what the assertions read.
    click = vi.fn();
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(click);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    createObjectURL.mockClear();
    revokeObjectURL.mockClear();
  });

  function respondWith(
    body: BodyInit | null,
    init?: { status?: number; contentDisposition?: string },
  ): ReturnType<typeof vi.fn> {
    const headers = new Headers();
    if (init?.contentDisposition) {
      headers.set('content-disposition', init.contentDisposition);
    }

    const fetchSpy = vi.fn(() =>
      Promise.resolve(new Response(body, { status: init?.status ?? 200, headers })),
    );
    vi.stubGlobal('fetch', fetchSpy);

    return fetchSpy;
  }

  it('requests the export with credentials so the session cookie travels', async () => {
    const fetchSpy = respondWith('EDJEr\r\nOdessa Ferrante\r\n');
    const { result } = renderHook(() => useReportDownload());

    await act(async () => {
      await result.current.download(
        '/api/compass/reports/assignment-duration/export',
        'fallback.csv',
      );
    });

    // The project rule: never a bare fetch for an API call. `apiFetch` is what adds this.
    expect(fetchSpy).toHaveBeenCalledWith(
      '/api/compass/reports/assignment-duration/export',
      expect.objectContaining({ credentials: 'include' }),
    );
    expect(result.current.error).toBeNull();
  });

  it('names the file from Content-Disposition when the server sends one', async () => {
    respondWith('csv', {
      contentDisposition: 'attachment; filename="compass-assignment-duration-2026-08-25.csv"',
    });
    const { result } = renderHook(() => useReportDownload());

    await act(async () => {
      await result.current.download('/export', 'fallback.csv');
    });

    expect(click).toHaveBeenCalledOnce();
    expect(lastDownloadName()).toBe('compass-assignment-duration-2026-08-25.csv');
  });

  it('prefers the RFC 5987 filename* form, which survives non-ASCII names', async () => {
    respondWith('csv', {
      contentDisposition:
        'attachment; filename="fallback.csv"; filename*=UTF-8\'\'compass-Zo%C3%AB.csv',
    });
    const { result } = renderHook(() => useReportDownload());

    await act(async () => {
      await result.current.download('/export', 'fallback.csv');
    });

    expect(lastDownloadName()).toBe('compass-Zoë.csv');
  });

  it('falls back to the caller-supplied name when no header is sent', async () => {
    // The cross-origin case: the browser hides Content-Disposition unless the API exposes it.
    respondWith('csv');
    const { result } = renderHook(() => useReportDownload());

    await act(async () => {
      await result.current.download('/export', 'compass-fallback.csv');
    });

    expect(lastDownloadName()).toBe('compass-fallback.csv');
  });

  it('falls back when the header carries no filename at all', async () => {
    respondWith('csv', { contentDisposition: 'attachment' });
    const { result } = renderHook(() => useReportDownload());

    await act(async () => {
      await result.current.download('/export', 'compass-fallback.csv');
    });

    expect(lastDownloadName()).toBe('compass-fallback.csv');
  });

  it('revokes the object URL, so a large report is not leaked for the document lifetime', async () => {
    respondWith('csv');
    const { result } = renderHook(() => useReportDownload());

    await act(async () => {
      await result.current.download('/export', 'fallback.csv');
    });

    expect(createObjectURL).toHaveBeenCalledOnce();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:stub');
  });

  it('reports a failure and starts no download when the server refuses', async () => {
    respondWith('nope', { status: 403 });
    const { result } = renderHook(() => useReportDownload());

    await act(async () => {
      await result.current.download('/export', 'fallback.csv');
    });

    expect(result.current.error).toBe(EXPORT_FAILED_MESSAGE);
    expect(click).not.toHaveBeenCalled();
  });

  it('reports a failure when the request itself throws', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => Promise.reject(new Error('offline'))),
    );
    const { result } = renderHook(() => useReportDownload());

    await act(async () => {
      await result.current.download('/export', 'fallback.csv');
    });

    expect(result.current.error).toBe(EXPORT_FAILED_MESSAGE);
    expect(click).not.toHaveBeenCalled();
  });

  it('clears a previous error when a new download starts', async () => {
    respondWith('nope', { status: 500 });
    const { result } = renderHook(() => useReportDownload());

    await act(async () => {
      await result.current.download('/export', 'fallback.csv');
    });
    expect(result.current.error).toBe(EXPORT_FAILED_MESSAGE);

    respondWith('csv');
    await act(async () => {
      await result.current.download('/export', 'fallback.csv');
    });

    expect(result.current.error).toBeNull();
  });

  it('is not downloading once the request settles, on success or failure', async () => {
    respondWith('csv');
    const { result } = renderHook(() => useReportDownload());
    expect(result.current.isDownloading).toBe(false);

    await act(async () => {
      await result.current.download('/export', 'fallback.csv');
    });
    expect(result.current.isDownloading).toBe(false);

    respondWith('nope', { status: 500 });
    await act(async () => {
      await result.current.download('/export', 'fallback.csv');
    });
    expect(result.current.isDownloading).toBe(false);
  });

  /**
   * The anchor is removed from the DOM before this runs, so the name is read from the spy's `this`.
   */
  function lastDownloadName(): string {
    const anchor = click.mock.instances.at(-1) as HTMLAnchorElement | undefined;

    return anchor?.download ?? '';
  }
});
