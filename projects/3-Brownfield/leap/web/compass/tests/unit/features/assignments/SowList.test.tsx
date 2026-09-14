import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { tableLinkClass } from '../../../../src/components/ui-classes';
import { SowList } from '../../../../src/features/assignments/SowList';

const rows = [
  {
    id: 1,
    sowType: 'InitialContract' as const,
    sowStartDate: '2024-04-01',
    sowEndDate: '2024-12-31',
    rateIncrease: false,
    note: 'Initial 9-month SOW',
  },
  {
    id: 2,
    sowType: 'SowExtension' as const,
    sowStartDate: '2025-01-01',
    sowEndDate: '2025-12-31',
    rateIncrease: true,
    note: 'FY25 renewal',
  },
];

describe('SowList', () => {
  it('renders a row for each contract period', () => {
    render(<SowList rows={rows} elevated onEdit={vi.fn()} />);

    expect(screen.getByText('Initial 9-month SOW')).toBeInTheDocument();
    expect(screen.getByText('FY25 renewal')).toBeInTheDocument();
  });

  it('renders both dates as mm/dd/yyyy, not the raw wire format (#234, #306)', () => {
    // These rows always carried ISO dates as fixture data, but nothing asserted the OUTPUT -- so the
    // component shipped rendering `2024-04-01` with a fully green suite. The repo-wide gate in
    // no-raw-dates.test.ts stops the next one; this asserts the format for this component itself.
    render(<SowList rows={rows} elevated onEdit={vi.fn()} />);

    expect(screen.getByText('04/01/2024')).toBeInTheDocument();
    expect(screen.getByText('12/31/2024')).toBeInTheDocument();
    expect(screen.getByText('01/01/2025')).toBeInTheDocument();
    expect(screen.getByText('12/31/2025')).toBeInTheDocument();
    expect(screen.queryByText('2024-04-01')).not.toBeInTheDocument();
  });

  it('gives its Edit control the shared table-link style, size composed on top', () => {
    // A `<button>` because it opens an inline editor, but read as the same affordance as the admin
    // screens' Edit; it shipped as brand-green-700 with a hover-only underline (owner request
    // 2026-08-21). `text-sm` matches this table's other cells.
    render(<SowList rows={rows} elevated onEdit={vi.fn()} />);

    expect(screen.getAllByRole('button', { name: 'Edit' })[0].className).toBe(
      `${tableLinkClass} text-sm`,
    );
  });

  it('formats the type as readable text', () => {
    render(<SowList rows={rows} elevated onEdit={vi.fn()} />);

    expect(screen.getByText('Initial Contract')).toBeInTheDocument();
    expect(screen.getByText('SOW Extension')).toBeInTheDocument();
  });

  describe('elevated viewer', () => {
    it('shows the Rate Increase and Note columns', () => {
      render(<SowList rows={rows} elevated onEdit={vi.fn()} />);

      expect(screen.getByRole('columnheader', { name: /rate increase/i })).toBeInTheDocument();
      expect(screen.getByRole('columnheader', { name: /note/i })).toBeInTheDocument();
      expect(screen.getByText('Yes')).toBeInTheDocument();
      expect(screen.getByText('No')).toBeInTheDocument();
    });

    it('renders a dash for Rate Increase and a blank cell for Note when neither was recorded', () => {
      const legacyRow = {
        id: 3,
        sowType: 'InitialContract' as const,
        sowStartDate: '2020-01-01',
        sowEndDate: '2020-12-31',
        rateIncrease: null,
        note: null,
      };
      render(<SowList rows={[legacyRow]} elevated onEdit={vi.fn()} />);

      expect(screen.getByText('—')).toBeInTheDocument();
    });
  });

  describe('non-elevated viewer (AC-16/FR-025-style)', () => {
    it('omits the Rate Increase and Note columns entirely', () => {
      render(<SowList rows={rows} elevated={false} onEdit={vi.fn()} />);

      expect(
        screen.queryByRole('columnheader', { name: /rate increase/i }),
      ).not.toBeInTheDocument();
      expect(screen.queryByRole('columnheader', { name: /note/i })).not.toBeInTheDocument();
      expect(screen.queryByText('Initial 9-month SOW')).not.toBeInTheDocument();
    });

    it('still shows the type and dates', () => {
      render(<SowList rows={rows} elevated={false} onEdit={vi.fn()} />);

      expect(screen.getByText('Initial Contract')).toBeInTheDocument();
    });
  });

  it('renders an honest empty state when there are no periods', () => {
    render(<SowList rows={[]} elevated onEdit={vi.fn()} />);

    expect(screen.queryByRole('table')).not.toBeInTheDocument();
    expect(screen.getByText(/no sows or contracts/i)).toBeInTheDocument();
  });

  it('calls onEdit with the row id when Edit is activated', async () => {
    const onEdit = vi.fn();
    render(<SowList rows={rows} elevated onEdit={onEdit} />);

    const firstRow = screen.getByText('Initial 9-month SOW').closest('tr');
    expect(firstRow).not.toBeNull();
    await userEvent.click(within(firstRow as HTMLElement).getByRole('button', { name: /edit/i }));

    expect(onEdit).toHaveBeenCalledWith(1);
  });

  // ---------------------------------------------------------------- LegacyMigrated (US8 boundary)

  describe('a LegacyMigrated period', () => {
    const legacyRow = {
      id: 9,
      sowType: 'LegacyMigrated' as const,
      sowStartDate: '2018-01-01',
      sowEndDate: '2018-12-31',
      rateIncrease: null,
      note: null,
    };

    it('renders a readable type label', () => {
      render(<SowList rows={[legacyRow]} elevated onEdit={vi.fn()} />);

      expect(screen.getByText(/legacy/i)).toBeInTheDocument();
    });

    it('renders an Edit action — the first-edit grandfathering exit (US8/#67, issue #404)', async () => {
      // Inverted deliberately. This surface used to omit Edit here, which made AC-41's "full
      // validation on the first application edit" unreachable: there was no first edit. The server
      // accepts the edit and keeps the row's type immutable, so the control belongs here now.
      const onEdit = vi.fn();
      render(<SowList rows={[legacyRow]} elevated onEdit={onEdit} />);

      await userEvent.click(screen.getByRole('button', { name: /edit/i }));

      expect(onEdit).toHaveBeenCalledWith(9);
    });

    it('shows Edit for every row, legacy and not, in the same list', () => {
      render(<SowList rows={[legacyRow, ...rows]} elevated onEdit={vi.fn()} />);

      expect(screen.getAllByRole('button', { name: /edit/i })).toHaveLength(rows.length + 1);
    });
  });

  // ---------------------------------------------------------------- Delete (issue #593)

  describe('canDelete', () => {
    it('omits the Delete action when canDelete is not set (the default)', () => {
      render(<SowList rows={rows} elevated onEdit={vi.fn()} />);

      expect(screen.queryByRole('button', { name: /delete/i })).not.toBeInTheDocument();
    });

    it('omits the Delete action when canDelete is explicitly false', () => {
      render(
        <SowList rows={rows} elevated onEdit={vi.fn()} canDelete={false} onDelete={vi.fn()} />,
      );

      expect(screen.queryByRole('button', { name: /delete/i })).not.toBeInTheDocument();
    });

    it('renders a Delete action per row when canDelete is true', () => {
      render(<SowList rows={rows} elevated onEdit={vi.fn()} canDelete onDelete={vi.fn()} />);

      expect(screen.getAllByRole('button', { name: /delete/i })).toHaveLength(rows.length);
    });

    it('calls onDelete with the row id when Delete is activated', async () => {
      const onDelete = vi.fn();
      render(<SowList rows={rows} elevated onEdit={vi.fn()} canDelete onDelete={onDelete} />);

      const firstRow = screen.getByText('Initial 9-month SOW').closest('tr');
      expect(firstRow).not.toBeNull();
      await userEvent.click(
        within(firstRow as HTMLElement).getByRole('button', { name: /delete/i }),
      );

      expect(onDelete).toHaveBeenCalledWith(1);
    });
  });
});
