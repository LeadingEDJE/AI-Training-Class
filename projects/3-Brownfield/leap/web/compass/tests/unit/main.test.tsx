import type { ReactElement } from 'react';
import { vi } from 'vitest';

// Mock react-dom/client so the entry point's mount is observable without a real DOM render.
const mockRender = vi.fn();
const mockCreateRoot = vi.fn(() => ({ render: mockRender }));

vi.mock('react-dom/client', () => ({
  createRoot: mockCreateRoot,
}));

vi.mock('../../src/index.css', () => ({}));

const mockInstallSessionExpiredRedirect = vi.fn();
vi.mock('../../src/lib/session-redirect', () => ({
  installSessionExpiredRedirect: mockInstallSessionExpiredRedirect,
}));

/**
 * Given a longer budget than vitest's flat 5000ms default, deliberately (issue `#235`).
 *
 * Every case here calls `vi.resetModules()` and then does a real, unmocked
 * `await import('../../src/main')` — a fresh transform of the ENTIRE application graph (the router,
 * every route, every feature module behind them) four times over. That is structurally heavier than
 * any other test in this suite, and it had no slack: `#235`/`#240` measured this file at **4558ms of
 * the 5000ms budget** on a WSL2 checkout under full-suite load, where it intermittently timed out.
 *
 * The graph only grows. Adding the Team Directory's pagination and date modules to it moved the
 * slowest case here from 222ms to 294ms on a warm macOS run — irrelevant against 5000ms locally, and
 * a ~32% increase applied to a budget already 91% spent elsewhere. So the timeout is raised where the
 * cost actually lives rather than globally, which would hide a genuine hang in some other file.
 *
 * **This is not the whole of `#235`.** That issue also records a router-identity assertion failing
 * under repeated dynamic import, which no timeout explains — it stays open for that.
 */
describe('main.tsx', { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.resetModules();
    document.getElementById('root')?.remove();
  });

  it('calls createRoot with the root element and renders', async () => {
    const rootEl = document.createElement('div');
    rootEl.id = 'root';
    document.body.appendChild(rootEl);

    await import('../../src/main');

    expect(mockCreateRoot).toHaveBeenCalledWith(rootEl);
    expect(mockRender).toHaveBeenCalled();
  });

  it('throws a named error when the root element is absent', async () => {
    // No #root in the document — the entry point must fail loudly rather than silently no-op.
    await expect(import('../../src/main')).rejects.toThrow('Root element not found');
  });

  it('mounts the Compass router rather than a single page', async () => {
    // Feature 004 replaced a direct <App> render with the routed tree. Asserting the router
    // instance actually reaches RouterProvider is the difference between "a router exists" and
    // "the application uses it" — the entry point is the only place that can be got wrong.
    const rootEl = document.createElement('div');
    rootEl.id = 'root';
    document.body.appendChild(rootEl);

    await import('../../src/main');
    // Imported after main so both resolve to the same module instance under resetModules().
    const { compassRouter } = await import('../../src/routes/router');

    const rendered = mockRender.mock.calls[0][0] as ReactElement<{ children: ReactElement }>;
    const queryProvider = rendered.props.children;
    const routerProvider = queryProvider.props.children as ReactElement<{ router: unknown }>;

    expect(routerProvider.props.router).toBe(compassRouter);
  });

  it('wires the session-expired redirect on startup (issue #217, FR-006/FR-008)', async () => {
    const rootEl = document.createElement('div');
    rootEl.id = 'root';
    document.body.appendChild(rootEl);

    await import('../../src/main');

    expect(mockInstallSessionExpiredRedirect).toHaveBeenCalledTimes(1);
  });
});
