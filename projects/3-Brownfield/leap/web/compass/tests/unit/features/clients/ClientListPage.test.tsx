import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { tableLinkClass } from '../../../../src/components/ui-classes';
import { stubFetchByUrl, type FetchByUrlStub } from '../../support/fetch-by-url';

// The router is stubbed so these assert the screen, not routing. Route resolution is router.test.ts's.
// The stub interpolates params and forwards aria-label, matching the real Link — see
// EdjerListPage.test.tsx for why a stub that did neither would hide real defects.
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    to,
    params,
    children,
    ...rest
  }: {
    to: string;
    params?: Record<string, string>;
    children: React.ReactNode;
  } & Record<string, unknown>) => {
    const href = Object.entries(params ?? {}).reduce(
      (path, [key, value]) => path.replace(`$${key}`, value),
      to,
    );
    return (
      <a href={href} {...rest}>
        {children}
      </a>
    );
  },
}));

const { ClientListPage } = await import('../../../../src/features/clients/ClientListPage');

const CLIENTS = '/api/compass/v1/admin/clients';

const ROWS = [
  {
    id: 1,
    clientName: 'Contoso',
    isInternal: false,
    invoiceFrequencyTypeName: 'Monthly',
    status: 'Active',
  },
  {
    id: 2,
    clientName: 'Fabrikam',
    isInternal: false,
    invoiceFrequencyTypeName: null,
    status: 'Inactive',
  },
  {
    id: 3,
    clientName: 'Leading EDJE Internal',
    isInternal: true,
    invoiceFrequencyTypeName: null,
    status: 'Active',
  },
  // Issue #274's third value. Present in the SHARED fixture rather than only in its own test, so
  // every assertion in this file — the row count, the search, the accessibility pass — sees a Former
  // row too.
  {
    id: 4,
    clientName: 'Northwind Traders',
    isInternal: false,
    invoiceFrequencyTypeName: 'Monthly',
    status: 'Former',
  },
];

function renderList(stub: FetchByUrlStub) {
  vi.stubGlobal('fetch', stub.mock);
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });

  return render(
    <QueryClientProvider client={queryClient}>
      <ClientListPage />
    </QueryClientProvider>,
  );
}

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('ClientListPage', () => {
  it('lists every client', async () => {
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } }));

    expect(await screen.findByText('Contoso')).toBeInTheDocument();
    expect(screen.getByText('Fabrikam')).toBeInTheDocument();
    expect(screen.getByText('Leading EDJE Internal')).toBeInTheDocument();
  });

  it('carries no subtitle explaining the obvious (issue #408)', async () => {
    // Shipped as a mockup `.note` reading "Every client. Status is derived from assignments — a
    // client with none reads Inactive, and is still fully editable and can still be assigned." — an
    // owner-requested removal, since it told an administrator nothing the screen itself did not
    // already.
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } }));

    await screen.findByText('Contoso');

    expect(screen.queryByText(/status is derived from assignments/i)).not.toBeInTheDocument();
  });

  it('marks the internal-EDJE clients in words, not colour alone', async () => {
    // AC-NFR-5: a cell that means something only by hue conveys nothing in greyscale and nothing to a
    // screen reader.
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } }));

    const internal = await screen.findByRole('row', { name: /leading edje internal/i });
    expect(within(internal).getByText('Internal')).toBeInTheDocument();

    const external = screen.getByRole('row', { name: /contoso/i });
    expect(within(external).getByText('External')).toBeInTheDocument();
  });

  it('shows a client with no invoice default without flagging it as a gap', async () => {
    // AC-22 makes the default optional, so "none" is an ordinary state rather than missing data.
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } }));

    const row = await screen.findByRole('row', { name: /fabrikam/i });
    expect(within(row).getByText('—')).toBeInTheDocument();
    expect(within(row).queryByText(/missing|required|none set/i)).not.toBeInTheDocument();
  });

  it('links each row to that client, with the client named in the accessible name', async () => {
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } }));

    const edit = await screen.findByRole('link', { name: 'Edit Contoso' });
    expect(edit).toHaveAttribute('href', '/admin/clients/1');
    // WCAG 2.5.3: the accessible name must CONTAIN the visible label, or voice control cannot reach it.
    expect(edit).toHaveTextContent('Edit');
  });

  it('wears the shared table-link style, with only wrapping composed on top', async () => {
    // Owner request 2026-08-21. `whitespace-nowrap` rides alongside because "Edit" must not break in a
    // narrow Actions column -- a CELL concern, which is why the shared style states no wrapping.
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } }));

    const edit = await screen.findByRole('link', { name: 'Edit Contoso' });
    expect(edit.className).toBe(`${tableLinkClass} whitespace-nowrap`);
  });

  it('offers an add link, plus-prefixed like every other Add control', async () => {
    // Owner request 2026-08-18: this one shipped without the `+` the lookup and EDJEr add buttons had.
    // The exact name is asserted here; the rule across every screen is `add-button-plus.test.ts`.
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } }));

    expect(await screen.findByRole('link', { name: '+ Add New Client' })).toHaveAttribute(
      'href',
      '/admin/clients/new',
    );
  });

  it('puts the list on the white card surface, not directly on the shell', async () => {
    // Owner report 2026-08-18. Asserted through the table's own ancestry rather than by counting divs,
    // so it survives any re-nesting that keeps the sheet in place.
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } }));

    const table = await screen.findByRole('table', { name: 'Clients' });

    expect(table.closest('.bg-white')).not.toBeNull();
  });

  it('filters in the browser, issuing no further request while typing', async () => {
    const stub = stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } });
    renderList(stub);

    await screen.findByText('Contoso');
    const before = stub.calls().length;

    await userEvent.type(screen.getByRole('searchbox'), 'fabr');

    expect(screen.getByText('Fabrikam')).toBeInTheDocument();
    expect(screen.queryByText('Contoso')).not.toBeInTheDocument();
    expect(stub.calls()).toHaveLength(before);
  });

  it('distinguishes a search that matched nothing from an empty client list', async () => {
    const stub = stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } });
    renderList(stub);

    await screen.findByText('Contoso');
    await userEvent.type(screen.getByRole('searchbox'), 'zzz');

    expect(screen.getByText(/no clients match/i)).toBeInTheDocument();
  });

  it('says so when there are no clients at all', async () => {
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: [] } }));

    expect(await screen.findByText(/no clients yet/i)).toBeInTheDocument();
  });

  it('reports a refusal as a refusal, never as an empty list', async () => {
    // Rendering "no clients yet" for a 403 would tell a Super Admin their client list had vanished.
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 403 } }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/permission/i);
    expect(screen.queryByText(/no clients yet/i)).not.toBeInTheDocument();
  });

  it('reports a failure as a failure', async () => {
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 500 } }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be loaded/i);
  });

  it('renders the derived status the server sent, as a word rather than a colour', async () => {
    // T107. This assertion was the opposite in US3 — "no status column" — and correctly so: the
    // payload carried none until US4 derived it. What has NOT changed is FR-029: the screen renders
    // what arrived and never computes its own, which is why the fixtures below set status
    // independently of whether a row looks like it should be active.
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } }));

    expect(await screen.findByRole('columnheader', { name: /^status$/i })).toBeInTheDocument();

    const active = screen.getByRole('row', { name: /contoso/i });
    expect(within(active).getByText('Active')).toBeInTheDocument();

    const inactive = screen.getByRole('row', { name: /fabrikam/i });
    expect(within(inactive).getByText('Inactive')).toBeInTheDocument();

    // Issue #274. `Former` and `Inactive` share the neutral pill tone by design (AC-NFR-5: the word
    // carries the state, never the colour), so the WORD is the only thing distinguishing them — which
    // makes this assertion the one that would catch a screen collapsing the two back together.
    const former = screen.getByRole('row', { name: /northwind traders/i });
    expect(within(former).getByText('Former')).toBeInTheDocument();
    expect(within(former).queryByText('Inactive')).not.toBeInTheDocument();
  });

  it('tells Former apart from Inactive by the word, on rows that look otherwise identical', async () => {
    // The distinction issue #274 asked for, isolated. Both rows carry the same cadence and the same
    // internal flag, so nothing on screen separates them except the status the server derived: one
    // client we set up and never engaged, one we worked with and stopped.
    renderList(
      stubFetchByUrl({
        [`GET ${CLIENTS}`]: {
          status: 200,
          body: [
            {
              id: 11,
              clientName: 'Never Engaged',
              isInternal: false,
              invoiceFrequencyTypeName: 'Monthly',
              status: 'Inactive',
            },
            {
              id: 12,
              clientName: 'Once Engaged',
              isInternal: false,
              invoiceFrequencyTypeName: 'Monthly',
              status: 'Former',
            },
          ],
        },
      }),
    );

    const neverEngaged = await screen.findByRole('row', { name: /never engaged/i });
    expect(within(neverEngaged).getByText('Inactive')).toBeInTheDocument();

    const onceEngaged = screen.getByRole('row', { name: /once engaged/i });
    expect(within(onceEngaged).getByText('Former')).toBeInTheDocument();
  });

  it('offers a status filter defaulting to All', async () => {
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } }));

    await screen.findByText('Contoso');

    expect(screen.getByRole('combobox', { name: /status/i })).toHaveValue('all');
    // Every row is visible until the administrator narrows it (issue #640).
    expect(screen.getByText('Fabrikam')).toBeInTheDocument();
    expect(screen.getByText('Northwind Traders')).toBeInTheDocument();
  });

  it('filters by status in the browser, issuing no further request', async () => {
    const stub = stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } });
    renderList(stub);

    await screen.findByText('Contoso');
    const before = stub.calls().length;

    await userEvent.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'Active');

    expect(screen.getByText('Contoso')).toBeInTheDocument();
    expect(screen.getByText('Leading EDJE Internal')).toBeInTheDocument();
    expect(screen.queryByText('Fabrikam')).not.toBeInTheDocument();
    expect(screen.queryByText('Northwind Traders')).not.toBeInTheDocument();
    expect(stub.calls()).toHaveLength(before);
  });

  it('combines the status filter with the name search (issue #640)', async () => {
    renderList(stubFetchByUrl({ [`GET ${CLIENTS}`]: { status: 200, body: ROWS } }));

    await screen.findByText('Contoso');

    await userEvent.selectOptions(screen.getByRole('combobox', { name: /status/i }), 'Former');
    await userEvent.type(screen.getByRole('searchbox'), 'contoso');

    expect(screen.getByText(/no clients match/i)).toBeInTheDocument();
    expect(screen.queryByText('Northwind Traders')).not.toBeInTheDocument();
  });

  it('renders whatever status the server sent, never a status of its own reckoning', async () => {
    // The load-bearing half of FR-029. A client with an invoice cadence and an internal flag "looks"
    // established, and a screen that decided for itself would say Active. The server said Inactive,
    // so Inactive is what must render — that disagreement is exactly what a second implementation
    // would introduce, on the boundary case nobody tests by hand.
    renderList(
      stubFetchByUrl({
        [`GET ${CLIENTS}`]: {
          status: 200,
          body: [
            {
              id: 9,
              clientName: 'Northwind',
              isInternal: true,
              invoiceFrequencyTypeName: 'Monthly',
              status: 'Inactive',
            },
          ],
        },
      }),
    );

    const row = await screen.findByRole('row', { name: /northwind/i });
    expect(within(row).getByText('Inactive')).toBeInTheDocument();
    expect(within(row).queryByText('Active')).not.toBeInTheDocument();
    expect(within(row).queryByText('Former')).not.toBeInTheDocument();
  });
});
