import { apiFetch, apiUrl } from './api-url';

export type ReportLoad<T> = { kind: 'loaded'; value: T } | { kind: 'refused' } | { kind: 'failed' };

/** Reads a gated JSON endpoint, throwing on any non-2xx response so react-query's retry policy handles it. */
export async function readGated<T>(url: string): Promise<ReportLoad<T>> {
  const response = await apiFetch(apiUrl(url));

  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }

  if (!response.ok) {
    return { kind: 'failed' };
  }

  return { kind: 'loaded', value: (await response.json()) as T };
}
