import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { vi } from 'vitest';

const fetchSpy = vi.fn();
vi.mock('../../../../src/lib/api-url', () => ({
  apiUrl: (path: string) => path,
  apiFetch: (url: string) => fetchSpy(url),
}));

import { EdjerPicker } from '../../../../src/features/assignments/EdjerPicker';

function renderPicker(onChange = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={queryClient}>
      <label htmlFor="edjer-picker">EDJEr</label>
      <EdjerPicker id="edjer-picker" value="" onChange={onChange} />
    </QueryClientProvider>,
  );
  return onChange;
}

const ROWS = [
  { id: 9, displayName: 'Maya Alvarez' },
  { id: 10, displayName: 'Chris Doyle' },
];

describe('EdjerPicker', () => {
  beforeEach(() => fetchSpy.mockReset());

  it('requests the EDJEr pickers route', async () => {
    fetchSpy.mockResolvedValue({ status: 200, json: async () => ROWS });
    renderPicker();

    await waitFor(() =>
      expect(fetchSpy).toHaveBeenCalledWith('/api/compass/assignments/pickers/edjers'),
    );
  });

  it('lists every active EDJEr returned by the server', async () => {
    fetchSpy.mockResolvedValue({ status: 200, json: async () => ROWS });
    renderPicker();

    expect(await screen.findByRole('option', { name: 'Maya Alvarez' })).toBeInTheDocument();
    expect(await screen.findByRole('option', { name: 'Chris Doyle' })).toBeInTheDocument();
  });

  it('calls onChange with the selected EDJEr id', async () => {
    fetchSpy.mockResolvedValue({ status: 200, json: async () => ROWS });
    const onChange = renderPicker();

    await screen.findByRole('option', { name: 'Maya Alvarez' });
    await userEvent.selectOptions(screen.getByLabelText('EDJEr'), '9');

    expect(onChange).toHaveBeenCalledWith('9');
  });

  it('disables the select while loading', () => {
    // Resolves eventually (not a permanently-pending promise) so cleanup doesn't hang waiting on
    // an in-flight query when the test ends — only the SYNCHRONOUS initial-render state matters here.
    fetchSpy.mockImplementation(
      () =>
        new Promise((resolve) =>
          setTimeout(() => resolve({ status: 200, json: async () => ROWS }), 20),
        ),
    );
    renderPicker();

    expect(screen.getByLabelText('EDJEr')).toBeDisabled();
  });
});
