/** Application entry point — mounts the Compass SPA into `#root`. There is no router: Compass is one page. */
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
