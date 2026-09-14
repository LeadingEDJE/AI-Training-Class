/**
 * Application entry point — mounts the Compass SPA into `#root` inside `<StrictMode>`, wrapped in a
 * query provider and the router. No public exports; the side effect of importing this module is app
 * startup.
 *
 * **Routing is TanStack Router with CODE-BASED routes** (feature 004, research D-4). This block
 * previously read "There is no router: Compass is one page", and gave the reason: a router brings a
 * generated route tree, which would need a coverage exclusion, and Compass is at the 98% tier with
 * zero exclusions. That objection was correct and is still binding — but it is an objection to the
 * *generated file*, not to routing. `src/routes/` defines the tree in ordinary TypeScript modules
 * that are importable, unit-testable, and counted toward the tier like any other file, so no
 * exclusion is needed. Do not add `@tanstack/router-plugin`; see `src/routes/router.ts`.
 *
 * The premise changed as well as the reasoning: Compass is no longer one page.
 *
 * `CompassNav` still renders plain `<a href>` links, and is still composed as a SIBLING of the page
 * rather than wrapping it — that composition moved into `src/routes/RootLayout.tsx`, which documents
 * why it is load-bearing for the components' tests.
 *
 * `installSessionExpiredRedirect()` (issue #217, FR-006/FR-008) wires the `session-expired` event
 * dispatched by `apiFetch` on any 401 to a redirect to `/auth/login`. Without it, an unauthenticated
 * visit to a deep Compass route rendered a static "not authorised" state instead of reaching sign-in.
 */
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import './index.css';
import { compassRouter } from './routes/router';
import { installSessionExpiredRedirect } from './lib/session-redirect';

installSessionExpiredRedirect();

const queryClient = new QueryClient();

const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('Root element not found');
}

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={compassRouter} />
    </QueryClientProvider>
  </StrictMode>,
);
