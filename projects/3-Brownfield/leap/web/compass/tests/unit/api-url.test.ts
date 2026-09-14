import { afterEach, describe, expect, it, vi } from 'vitest';
import { apiFetch, apiUrl, resetSessionExpired } from '../../src/lib/api-url';

describe('apiUrl', () => {
  afterEach(() => {
    delete window.__API_BASE_URL__;
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('returns the path unchanged when no runtime base URL is configured', () => {
    expect(apiUrl('/api/compass/v1/employees/x')).toBe('/api/compass/v1/employees/x');
  });

  it('prefixes the runtime-configured base URL when one is set', () => {
    window.__API_BASE_URL__ = 'https://api.example.test';

    expect(apiUrl('/api/compass/v1/employees/x')).toBe(
      'https://api.example.test/api/compass/v1/employees/x',
    );
  });
});

describe('apiFetch', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    resetSessionExpired();
  });

  it('sends the session cookie via credentials: include', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ status: 200 } as Response);
    vi.stubGlobal('fetch', fetchMock);

    await apiFetch('/api/compass/v1/employees/x');

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/compass/v1/employees/x',
      expect.objectContaining({ credentials: 'include' }),
    );
  });

  it('preserves caller-supplied init options', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ status: 200 } as Response);
    vi.stubGlobal('fetch', fetchMock);

    await apiFetch('/api/compass/v1/employees/x', { method: 'GET' });

    expect(fetchMock).toHaveBeenCalledWith(
      '/api/compass/v1/employees/x',
      expect.objectContaining({ credentials: 'include', method: 'GET' }),
    );
  });

  it('dispatches session-expired on a 401', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ status: 401 } as Response));
    const listener = vi.fn();
    window.addEventListener('session-expired', listener);

    await apiFetch('/api/compass/v1/employees/x');

    expect(listener).toHaveBeenCalled();
    window.removeEventListener('session-expired', listener);
  });

  it('does not dispatch session-expired on a non-401 response', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ status: 403 } as Response));
    const listener = vi.fn();
    window.addEventListener('session-expired', listener);

    await apiFetch('/api/compass/v1/employees/x');

    expect(listener).not.toHaveBeenCalled();
    window.removeEventListener('session-expired', listener);
  });

  it('dispatches session-expired only once for repeated 401s (one-shot guard)', async () => {
    resetSessionExpired();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ status: 401 } as Response));
    const listener = vi.fn();
    window.addEventListener('session-expired', listener);

    // Two concurrent callers (e.g. CompassNav's /api/me and App's employee fetch) can both hit a
    // 401 in the same render pass — the guard must fire the event once, not twice.
    await apiFetch('/api/compass/v1/employees/x');
    await apiFetch('/api/me');

    expect(listener).toHaveBeenCalledTimes(1);
    window.removeEventListener('session-expired', listener);
  });

  it('re-arms after resetSessionExpired (enables re-detection after re-login)', async () => {
    resetSessionExpired();
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ status: 401 } as Response));
    const listener = vi.fn();
    window.addEventListener('session-expired', listener);

    await apiFetch('/api/compass/v1/employees/x');
    expect(listener).toHaveBeenCalledTimes(1);

    resetSessionExpired();
    await apiFetch('/api/compass/v1/employees/x');
    expect(listener).toHaveBeenCalledTimes(2);

    window.removeEventListener('session-expired', listener);
  });
});
