import { apiFetch, apiUrl } from './api-url';

/**
 * The outcome of a gated read.
 *
 * **A refusal and a failure are distinct states.** The Compass read surfaces are gated by
 * `CompassReporting`, which excludes Compass Admin (FR-019), and a deep link or a bookmark reaches the
 * screen even though the nav never offers it. Rendering "could not be loaded" for a 403 tells that
 * viewer the screen is broken when it is working exactly as specified.
 */
export type ReportLoad<T> = { kind: 'loaded'; value: T } | { kind: 'refused' } | { kind: 'failed' };

/**
 * Reads a gated JSON endpoint into a {@link ReportLoad}.
 *
 * **A non-OK response RESOLVES rather than throws**, so a 403 reaches the screen as its own state
 * instead of an indistinguishable `Error`. Only an unparseable body still throws — a genuine failure of
 * the response rather than a verdict carried by it. Resolving also means react-query sees a success and
 * never retries, so a refusal is asked exactly once without a retry policy having to say so.
 *
 * 401 is handled globally by `apiFetch`'s session-expired flow; treating it as refused here as well
 * keeps an expiring session from rendering a hard error while the re-login prompt is opening.
 *
 * **Extracted at the third occurrence, not the second.** `useSalesDashboard` and
 * `useAvailabilityReport` each carried a copy, and the latter's own docstring named US3's
 * assignment-duration hook as the trigger: "lift both into `lib/` then, not now." This is that lift —
 * the Rule of Three firing on schedule rather than a speculative abstraction (Four Rules of
 * Simple Design).
 */
export async function readGated<T>(url: string): Promise<ReportLoad<T>> {
  // apiFetch + apiUrl live HERE rather than being passed in, so every gated read is structurally
  // incapable of using a bare `fetch` — that becomes a property of the helper instead of something
  // each call site has to remember.
  const response = await apiFetch(apiUrl(url));

  if (response.status === 401 || response.status === 403) {
    return { kind: 'refused' };
  }

  if (!response.ok) {
    return { kind: 'failed' };
  }

  return { kind: 'loaded', value: (await response.json()) as T };
}
