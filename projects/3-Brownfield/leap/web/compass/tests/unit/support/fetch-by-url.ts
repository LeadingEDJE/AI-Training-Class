import { vi } from 'vitest';

/**
 * A `fetch` stub that answers per URL and method, rather than returning one canned response to every
 * call.
 *
 * The existing convention (`App.test.tsx`'s `mockFetchOnce`) stubs the global with a single-shot
 * response for any call. That is exactly right for a page making one request, and it is why `main.tsx`
 * composes `CompassNav` as a SIBLING of the page rather than nesting it — two components calling
 * `apiFetch` would collide on that mock. A screen that reads two collections and writes to both needs
 * responses keyed by request instead.
 */
export interface StubbedResponse {
  status: number;
  body?: unknown;
}

export type ResponderKey = `${string} ${string}`;

export interface FetchByUrlStub {
  /** The underlying mock, for asserting what was requested. */
  mock: ReturnType<typeof vi.fn>;
  /** Every request made so far, in order, as `"METHOD url"`. */
  calls: () => string[];
  /** Replaces the response for a key after the stub is installed. */
  setResponse: (key: ResponderKey, response: StubbedResponse) => void;
  /** Requests matching a key, for asserting bodies. */
  bodiesFor: (key: ResponderKey) => unknown[];
}

/**
 * Installs a `fetch` stub over the given map.
 *
 * Keys are `"METHOD /path/prefix"`; the longest matching prefix wins, so a specific
 * `"PUT /api/compass/v1/admin/employee-types/7"` beats a general
 * `"PUT /api/compass/v1/admin/employee-types"`. An unmatched request rejects loudly rather than
 * resolving to something plausible — a silently-satisfied request is how a test ends up asserting
 * against a response the application never really gets.
 */
export function stubFetchByUrl(
  responders: Partial<Record<ResponderKey, StubbedResponse>>,
): FetchByUrlStub {
  const table = new Map<string, StubbedResponse>(
    Object.entries(responders) as [string, StubbedResponse][],
  );
  const seen: { key: string; body: unknown }[] = [];

  const mock = vi.fn((input: string | URL | Request, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input.toString();
    const method = (init?.method ?? 'GET').toUpperCase();
    const key = `${method} ${url}`;

    let matched: StubbedResponse | undefined;
    let matchedLength = -1;
    for (const [candidate, response] of table) {
      const [candidateMethod, candidatePath] = candidate.split(' ');
      if (
        candidateMethod === method &&
        url.startsWith(candidatePath) &&
        candidatePath.length > matchedLength
      ) {
        matched = response;
        matchedLength = candidatePath.length;
      }
    }

    seen.push({
      key,
      body: init?.body === undefined ? undefined : JSON.parse(String(init.body)),
    });

    if (!matched) {
      return Promise.reject(new Error(`No stubbed response for ${key}`));
    }

    return Promise.resolve({
      status: matched.status,
      ok: matched.status >= 200 && matched.status < 300,
      json: async () => matched.body,
    } as Response);
  });

  vi.stubGlobal('fetch', mock);

  return {
    mock,
    calls: () => seen.map((entry) => entry.key),
    setResponse: (key, response) => table.set(key, response),
    bodiesFor: (key) =>
      seen
        .filter((entry) => {
          const [method, path] = key.split(' ');
          return entry.key.startsWith(`${method} `) && entry.key.includes(path);
        })
        .map((entry) => entry.body),
  };
}
