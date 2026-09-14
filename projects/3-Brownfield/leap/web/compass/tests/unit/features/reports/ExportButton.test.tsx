import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ExportButton } from '../../../../src/features/reports/export/ExportButton';
import { EXPORT_FAILED_MESSAGE } from '../../../../src/features/reports/export/useReportDownload';

/**
 * The report export control (issue #337).
 *
 * The accessible-name assertion is the one worth having: the Availability Report renders three of
 * these, and without the per-section suffix a screen-reader user tabbing between them hears "Export
 * CSV" three times with nothing to tell them apart (WCAG 2.4.6, AC-NFR-5).
 */
describe('ExportButton', () => {
  beforeEach(() => {
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: () => 'blob:stub',
      revokeObjectURL: vi.fn(),
    });
    // Intercept the synthetic click so jsdom does not attempt a navigation.
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  function stubFetch(status = 200): ReturnType<typeof vi.fn> {
    const spy = vi.fn(() => Promise.resolve(new Response('csv', { status })));
    vi.stubGlobal('fetch', spy);

    return spy;
  }

  it('requests its path when clicked', async () => {
    const fetchSpy = stubFetch();
    render(
      <ExportButton
        path="/api/compass/reports/assignment-duration/export"
        fallbackFileName="compass-assignment-duration.csv"
        label="Export CSV"
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Export CSV' }));

    await waitFor(() =>
      expect(fetchSpy).toHaveBeenCalledWith(
        '/api/compass/reports/assignment-duration/export',
        expect.objectContaining({ credentials: 'include' }),
      ),
    );
  });

  it('folds the accessible suffix into its name, so sibling exports are distinguishable', async () => {
    stubFetch();
    render(
      <ExportButton
        path="/api/compass/reports/availability/export?section=confirmed-rollouts"
        fallbackFileName="compass-availability-confirmed-rollouts.csv"
        label="Export CSV"
        accessibleSuffix="Confirmed rollouts"
      />,
    );

    // The visible text is still just "Export CSV"; the suffix is sr-only.
    expect(screen.getByRole('button', { name: 'Export CSV: Confirmed rollouts' })).toBeVisible();
  });

  it('surfaces a failure beside the button that caused it', async () => {
    stubFetch(500);
    render(
      <ExportButton path="/export" fallbackFileName="compass-report.csv" label="Export CSV" />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Export CSV' }));

    expect(await screen.findByRole('alert')).toHaveTextContent(EXPORT_FAILED_MESSAGE);
  });

  it('shows no error before anything has been clicked', () => {
    stubFetch();
    render(
      <ExportButton path="/export" fallbackFileName="compass-report.csv" label="Export CSV" />,
    );

    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('renders the primary variant for the report-level Export All', () => {
    stubFetch();
    render(
      <ExportButton
        path="/api/compass/reports/availability/export"
        fallbackFileName="compass-availability-report.zip"
        label="Export All (.zip)"
        variant="primary"
      />,
    );

    expect(screen.getByRole('button', { name: 'Export All (.zip)' })).toBeVisible();
  });
});
