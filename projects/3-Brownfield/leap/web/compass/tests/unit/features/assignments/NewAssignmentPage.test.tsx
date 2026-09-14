import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { NewAssignmentPage } from '../../../../src/features/assignments/NewAssignmentPage';
import {
  clientAssignmentTrail,
  employeeAssignmentTrail,
} from '../../../../src/features/assignments/assignment-trail';

function renderWithClient(ui: React.ReactElement) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={queryClient}>{ui}</QueryClientProvider>);
}

describe('NewAssignmentPage', () => {
  beforeEach(() => fetchSpy.mockReset());

  describe('reached from an EDJEr record', () => {
    it('shows the EDJEr as fixed text, not an input, and offers a client picker', async () => {
      fetchSpy.mockResolvedValue({
        status: 200,
        json: async () => [{ id: 1, clientName: 'Buckeye Mutual', derivedStatus: 'Active' }],
      });
      renderWithClient(
        <NewAssignmentPage
          viaEmployee
          fixedEmployee={{ id: 9, label: 'Maya Alvarez' }}
          trail={employeeAssignmentTrail('directory', { id: 9, label: 'Maya Alvarez' })}
          onSubmit={vi.fn().mockResolvedValue({ kind: 'saved' })}
        />,
      );

      expect(screen.getByRole('heading', { name: 'New Assignment' })).toBeInTheDocument();
      expect(screen.getAllByText('Maya Alvarez').length).toBeGreaterThan(0);
      expect(screen.queryByLabelText(/edjer/i)).not.toBeInTheDocument();
      expect(await screen.findByRole('option', { name: /buckeye mutual/i })).toBeInTheDocument();
    });

    it('breadcrumbs Team Directory / {edjer} / New Assignment', () => {
      renderWithClient(
        <NewAssignmentPage
          viaEmployee
          fixedEmployee={{ id: 9, label: 'Maya Alvarez' }}
          trail={employeeAssignmentTrail('directory', { id: 9, label: 'Maya Alvarez' })}
          onSubmit={vi.fn()}
        />,
      );

      const breadcrumb = screen.getByRole('navigation', { name: /breadcrumb/i });
      expect(breadcrumb).toHaveTextContent('Team Directory');
      expect(breadcrumb).toHaveTextContent('Maya Alvarez');
      expect(breadcrumb).toHaveTextContent('New Assignment');
      expect(screen.getByRole('link', { name: 'Maya Alvarez' })).toHaveAttribute(
        'href',
        '/compass/team-directory/9',
      );
    });

    it('submits the fixed EDJEr id and the picked client id', async () => {
      fetchSpy.mockResolvedValue({
        status: 200,
        json: async () => [{ id: 1, clientName: 'Buckeye Mutual', derivedStatus: 'Active' }],
      });
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      renderWithClient(
        <NewAssignmentPage
          viaEmployee
          fixedEmployee={{ id: 9, label: 'Maya Alvarez' }}
          trail={employeeAssignmentTrail('directory', { id: 9, label: 'Maya Alvarez' })}
          onSubmit={onSubmit}
        />,
      );

      await screen.findByRole('option', { name: /buckeye mutual/i });
      await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Client' }), '1');
      await userEvent.type(screen.getByLabelText(/start date/i), '2026-01-01');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({ employeeId: 9, clientId: 1 }),
      );
    });
  });

  describe('reached from a Client record', () => {
    it('shows the client as fixed text, not an input, and offers an EDJEr picker', async () => {
      fetchSpy.mockResolvedValue({
        status: 200,
        json: async () => [{ id: 9, displayName: 'Maya Alvarez' }],
      });
      renderWithClient(
        <NewAssignmentPage
          viaEmployee={false}
          fixedClient={{ id: 1, label: 'Buckeye Mutual' }}
          trail={clientAssignmentTrail({ id: 1, label: 'Buckeye Mutual' })}
          onSubmit={vi.fn().mockResolvedValue({ kind: 'saved' })}
        />,
      );

      expect(screen.getAllByText('Buckeye Mutual').length).toBeGreaterThan(0);
      expect(screen.queryByLabelText(/^client$/i)).not.toBeInTheDocument();
      expect(await screen.findByRole('option', { name: 'Maya Alvarez' })).toBeInTheDocument();
    });

    it('breadcrumbs Client Directory / {client} / New Assignment', () => {
      renderWithClient(
        <NewAssignmentPage
          viaEmployee={false}
          fixedClient={{ id: 1, label: 'Buckeye Mutual' }}
          trail={clientAssignmentTrail({ id: 1, label: 'Buckeye Mutual' })}
          onSubmit={vi.fn()}
        />,
      );

      const breadcrumb = screen.getByRole('navigation', { name: /breadcrumb/i });
      expect(breadcrumb).toHaveTextContent('Client Directory');
      expect(breadcrumb).toHaveTextContent('Buckeye Mutual');
      expect(breadcrumb).toHaveTextContent('New Assignment');
      expect(screen.getByRole('link', { name: 'Buckeye Mutual' })).toHaveAttribute(
        'href',
        '/compass/client-directory/1',
      );
    });

    it('submits the fixed client id and the picked EDJEr id', async () => {
      fetchSpy.mockResolvedValue({
        status: 200,
        json: async () => [{ id: 9, displayName: 'Maya Alvarez' }],
      });
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      renderWithClient(
        <NewAssignmentPage
          viaEmployee={false}
          fixedClient={{ id: 1, label: 'Buckeye Mutual' }}
          trail={clientAssignmentTrail({ id: 1, label: 'Buckeye Mutual' })}
          onSubmit={onSubmit}
        />,
      );

      await screen.findByRole('option', { name: 'Maya Alvarez' });
      await userEvent.selectOptions(screen.getByRole('combobox', { name: 'EDJEr' }), '9');
      await userEvent.type(screen.getByLabelText(/start date/i), '2026-01-01');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({ employeeId: 9, clientId: 1 }),
      );
    });
  });
});
