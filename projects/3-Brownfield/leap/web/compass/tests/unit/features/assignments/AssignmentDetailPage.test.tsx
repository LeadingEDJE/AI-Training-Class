import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { checkAccessibility } from '../../../../src/lib/accessibility-check';
import { AssignmentDetailPage } from '../../../../src/features/assignments/AssignmentDetailPage';
import type { SowListRow } from '../../../../src/features/assignments/SowList';
import {
  clientAssignmentTrail,
  employeeAssignmentTrail,
} from '../../../../src/features/assignments/assignment-trail';
import type { AssignmentRowDto } from '../../../../src/features/assignments/assignments-api';

const ASSIGNMENT: AssignmentRowDto = {
  id: 3,
  employeeId: 9,
  employeeName: 'Maya Alvarez',
  clientId: 1,
  clientName: 'Buckeye Mutual',
  startDate: '2024-04-01',
  endDate: null,
  isCurrent: true,
  // Issue #518 — the CLIENT's internal-EDJE ("beach") flag, denormalised onto the row. False here so
  // every existing case keeps describing an ordinary client; the internal cases override it.
  isInternal: false,
  note: 'Platform modernization team — squad 2.',
};

function renderPage(overrides: Partial<Parameters<typeof AssignmentDetailPage>[0]> = {}) {
  return render(
    <AssignmentDetailPage
      assignment={ASSIGNMENT}
      isPending={false}
      isError={false}
      viaEmployee={false}
      trail={clientAssignmentTrail({ id: ASSIGNMENT.clientId, label: ASSIGNMENT.clientName })}
      onSubmit={vi.fn().mockResolvedValue({ kind: 'saved' })}
      {...overrides}
    />,
  );
}

describe('AssignmentDetailPage', () => {
  it('shows a loading state', () => {
    renderPage({ isPending: true });

    expect(screen.getByRole('status')).toHaveTextContent(/loading/i);
  });

  it('shows an error state', () => {
    renderPage({ isPending: false, isError: true });

    expect(screen.getByRole('alert')).toBeInTheDocument();
  });

  it('shows a not-found state when the assignment is absent', () => {
    renderPage({ assignment: null });

    expect(screen.getByText(/not found/i)).toBeInTheDocument();
  });

  it('renders the title and status pill', () => {
    renderPage();

    expect(screen.getByRole('heading', { name: 'Client Assignment' })).toBeInTheDocument();
    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('renders "Ended" as a word, not colour alone, for a closed assignment (FR-051d)', () => {
    renderPage({ assignment: { ...ASSIGNMENT, endDate: '2024-12-31', isCurrent: false } });

    expect(screen.getByText('Ended')).toBeInTheDocument();
  });

  it('shows the EDJEr and client as read-only context', () => {
    renderPage();

    // Each name also appears in the breadcrumb — getAllByText, not getByText, for that reason.
    expect(screen.getAllByText('Maya Alvarez').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Buckeye Mutual').length).toBeGreaterThan(0);
    // Read-only: no input for either, since neither is movable (contract §2).
    expect(screen.queryByLabelText(/edjer/i)).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/^client$/i)).not.toBeInTheDocument();
  });

  describe('breadcrumb (AC-1, AC-2 — reachable from either record)', () => {
    // The trail is injected, so these pass what a container would build; `viaEmployee` now decides
    // only which party names the current screen.
    it('reads "Client Directory / {client} / Assignment — {edjer}" when reached from the client', () => {
      renderPage({ viaEmployee: false });

      const breadcrumb = screen.getByRole('navigation', { name: /breadcrumb/i });
      expect(breadcrumb).toHaveTextContent('Client Directory');
      expect(breadcrumb).toHaveTextContent('Buckeye Mutual');
      expect(breadcrumb).toHaveTextContent('Assignment — Maya Alvarez');
      expect(screen.getByRole('link', { name: 'Buckeye Mutual' })).toHaveAttribute(
        'href',
        '/compass/client-directory/1',
      );
    });

    it('reads "Team Directory / {edjer} / Assignment — {client}" when reached from the EDJEr', () => {
      renderPage({
        viaEmployee: true,
        trail: employeeAssignmentTrail('directory', {
          id: ASSIGNMENT.employeeId,
          label: ASSIGNMENT.employeeName,
        }),
      });

      const breadcrumb = screen.getByRole('navigation', { name: /breadcrumb/i });
      expect(breadcrumb).toHaveTextContent('Team Directory');
      expect(breadcrumb).toHaveTextContent('Maya Alvarez');
      expect(breadcrumb).toHaveTextContent('Assignment — Buckeye Mutual');
      expect(screen.getByRole('link', { name: 'Maya Alvarez' })).toHaveAttribute(
        'href',
        '/compass/team-directory/9',
      );
    });
  });

  it('pre-populates the form from the assignment and submits an edit', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
    renderPage({ onSubmit, canManageAssignments: true });

    expect(screen.getByLabelText(/start date/i)).toHaveValue('2024-04-01');
    expect(screen.getByLabelText(/note/i)).toHaveValue(ASSIGNMENT.note);

    await userEvent.type(screen.getByLabelText(/end date/i), '2024-12-31');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    expect(onSubmit).toHaveBeenCalledWith(
      expect.objectContaining({ startDate: '2024-04-01', endDate: '2024-12-31' }),
    );
  });

  describe('viewers who cannot edit (AC-16/FR-025 — Compass Admin/Sales can VIEW, not write)', () => {
    it('defaults to read-only when canManageAssignments is not passed', () => {
      renderPage();

      expect(screen.getByText('Platform modernization team — squad 2.')).toBeInTheDocument();
      expect(screen.queryByRole('button', { name: /save/i })).not.toBeInTheDocument();
      expect(screen.queryByLabelText(/start date/i)).not.toBeInTheDocument();
      expect(screen.queryByLabelText(/note/i)).not.toBeInTheDocument();
    });

    it('shows an open-ended assignment\'s end date as "Current" rather than blank', () => {
      renderPage({ canManageAssignments: false });

      expect(screen.getAllByText(/current/i).length).toBeGreaterThan(0);
    });
  });

  describe('SOWs / Contracts (US3, #66)', () => {
    it('does not render the card at all for a viewer who cannot manage assignments', () => {
      // Contract §1: the SOW route grants no read exception the way the assignment surface does
      // for Admin/Sales — a non-elevated viewer would only ever see a 403 here.
      renderPage({ canManageAssignments: false });

      expect(screen.queryByRole('heading', { name: /sows/i })).not.toBeInTheDocument();
    });

    it('shows a loading state', () => {
      renderPage({ canManageAssignments: true, sowsIsPending: true });

      expect(screen.getByRole('status')).toHaveTextContent(/contract periods/i);
    });

    it('shows an error state', () => {
      renderPage({ canManageAssignments: true, sowsIsError: true });

      expect(screen.getByRole('alert')).toHaveTextContent(/contract periods/i);
    });

    it('renders an honest empty state with an Add action', () => {
      renderPage({ canManageAssignments: true, sows: [] });

      expect(screen.getByText(/no sows or contracts/i)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /add sow/i })).toBeInTheDocument();
    });

    it('renders the recorded periods', () => {
      renderPage({
        canManageAssignments: true,
        sows: [
          {
            id: 1,
            sowType: 'InitialContract',
            sowStartDate: '2024-04-01',
            sowEndDate: '2024-12-31',
            rateIncrease: false,
            note: 'Initial 9-month SOW',
          },
        ],
      });

      expect(screen.getByText('Initial 9-month SOW')).toBeInTheDocument();
    });

    it('falls back to a generic rejection if a caller renders the modal with no onCreateSow', async () => {
      // Not a reachable production path — the card that opens this modal only renders for
      // canManageAssignments, and a real route always supplies both handlers — but the default
      // exists to be a safe fallback rather than a crash if a future caller forgets one.
      renderPage({ canManageAssignments: true, sows: [] });

      await userEvent.click(screen.getByRole('button', { name: /add sow/i }));
      await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
      await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-04-01');
      await userEvent.type(screen.getByLabelText(/sow end date/i), '2024-12-31');
      await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

      expect(await screen.findByRole('alert')).toHaveTextContent(/could not be saved/i);
    });

    it('falls back to a generic rejection if a caller renders the modal with no onUpdateSow', async () => {
      // Same reasoning as the onCreateSow fallback above — not reachable in production.
      renderPage({
        canManageAssignments: true,
        sows: [
          {
            id: 7,
            sowType: 'SowExtension',
            sowStartDate: '2025-01-01',
            sowEndDate: '2025-12-31',
            rateIncrease: true,
            note: 'FY25 renewal',
          },
        ],
      });

      await userEvent.click(screen.getByRole('button', { name: /edit/i }));
      await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

      expect(await screen.findByRole('alert')).toHaveTextContent(/could not be saved/i);
    });

    it('opens the create modal on Add, and submits through onCreateSow', async () => {
      const onCreateSow = vi.fn().mockResolvedValue({ kind: 'saved' });
      renderPage({ canManageAssignments: true, sows: [], onCreateSow });

      await userEvent.click(screen.getByRole('button', { name: /add sow/i }));
      expect(screen.getByRole('dialog')).toBeInTheDocument();

      await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
      await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-04-01');
      await userEvent.type(screen.getByLabelText(/sow end date/i), '2024-12-31');
      await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

      expect(onCreateSow).toHaveBeenCalledWith(
        expect.objectContaining({ sowType: 'InitialContract' }),
      );
    });

    it('closes the modal after a successful create', async () => {
      const onCreateSow = vi.fn().mockResolvedValue({ kind: 'saved' });
      renderPage({ canManageAssignments: true, sows: [], onCreateSow });

      await userEvent.click(screen.getByRole('button', { name: /add sow/i }));
      await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
      await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-04-01');
      await userEvent.type(screen.getByLabelText(/sow end date/i), '2024-12-31');
      await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    });

    it('opens the edit modal pre-populated, and submits through onUpdateSow addressed by id', async () => {
      const onUpdateSow = vi.fn().mockResolvedValue({ kind: 'saved' });
      renderPage({
        canManageAssignments: true,
        sows: [
          {
            id: 7,
            sowType: 'SowExtension',
            sowStartDate: '2025-01-01',
            sowEndDate: '2025-12-31',
            rateIncrease: true,
            note: 'FY25 renewal',
          },
        ],
        onUpdateSow,
      });

      await userEvent.click(screen.getByRole('button', { name: /edit/i }));

      const dialog = screen.getByRole('dialog');
      expect(within(dialog).getByRole('radio', { name: /sow extension/i })).toBeChecked();
      expect(within(dialog).getByLabelText(/note/i)).toHaveValue('FY25 renewal');

      await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

      expect(onUpdateSow).toHaveBeenCalledWith(
        7,
        expect.objectContaining({ note: 'FY25 renewal' }),
      );
    });

    it('opens the editor for a LegacyMigrated period, type read-only (US8/#67, issue #404)', async () => {
      // Inverted: this used to assert NO Edit action, which made AC-41's first-edit validation
      // unreachable. The type rides back unchanged because the server holds it immutable.
      const onUpdateSow = vi.fn().mockResolvedValue({ kind: 'saved' });
      renderPage({
        canManageAssignments: true,
        onUpdateSow,
        sows: [
          {
            id: 8,
            sowType: 'LegacyMigrated',
            sowStartDate: '2018-01-01',
            sowEndDate: '2018-12-31',
            rateIncrease: null,
            note: null,
          },
        ],
      });

      await userEvent.click(screen.getByRole('button', { name: /edit/i }));

      const dialog = screen.getByRole('dialog');
      expect(within(dialog).getByText(/legacy migrated/i)).toBeInTheDocument();
      expect(
        within(dialog).queryByRole('radio', { name: /initial contract/i }),
      ).not.toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

      expect(onUpdateSow).toHaveBeenCalledWith(
        8,
        expect.objectContaining({ sowType: 'LegacyMigrated', rateIncrease: false }),
      );
    });

    // ------------------------------------------------------------------------- issue #626

    it('defaults a new contract period to SOW Extension when one already exists', async () => {
      const onCreateSow = vi.fn().mockResolvedValue({ kind: 'saved' });
      renderPage({
        canManageAssignments: true,
        sows: [
          {
            id: 1,
            sowType: 'InitialContract',
            sowStartDate: '2024-01-01',
            sowEndDate: '2024-12-31',
            rateIncrease: false,
            note: null,
          },
        ],
        onCreateSow,
      });

      await userEvent.click(screen.getByRole('button', { name: /add sow/i }));

      expect(screen.getByRole('radio', { name: /sow extension/i })).toBeChecked();

      // Still just a starting point — the person adding it can change it back.
      await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
      await userEvent.type(screen.getByLabelText(/sow start date/i), '2025-01-01');
      await userEvent.type(screen.getByLabelText(/sow end date/i), '2025-06-30');
      await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

      expect(onCreateSow).toHaveBeenCalledWith(
        expect.objectContaining({ sowType: 'InitialContract' }),
      );
    });

    it('still defaults to Initial Contract when the assignment has no contract periods yet', async () => {
      renderPage({ canManageAssignments: true, sows: [] });

      await userEvent.click(screen.getByRole('button', { name: /add sow/i }));

      expect(screen.getByRole('radio', { name: /initial contract/i })).toBeChecked();
    });

    it('defaults to Initial Contract if Add is clicked before the SOWs list has loaded', async () => {
      // `sows` is `undefined` while `sowsIsPending` — the Add action is not gated on the load
      // finishing, so the default must not assume the list has arrived yet.
      renderPage({ canManageAssignments: true, sows: undefined, sowsIsPending: true });

      await userEvent.click(screen.getByRole('button', { name: /add sow/i }));

      expect(screen.getByRole('radio', { name: /initial contract/i })).toBeChecked();
    });

    it('closes the modal on Cancel without calling onCreateSow', async () => {
      const onCreateSow = vi.fn();
      renderPage({ canManageAssignments: true, sows: [], onCreateSow });

      await userEvent.click(screen.getByRole('button', { name: /add sow/i }));
      await userEvent.click(screen.getByRole('button', { name: /cancel/i }));

      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(onCreateSow).not.toHaveBeenCalled();
    });

    describe('an internal client has no contracts and nothing to invoice (issue #518)', () => {
      const INTERNAL = { ...ASSIGNMENT, isInternal: true };

      it('hides the SOWs / Contracts card entirely, Add action and all', () => {
        // Internal work is EDJE on EDJE: there is no counterparty to sign a statement of work with,
        // so the section is meaningless rather than merely empty — and the server now refuses the
        // create as well, so an Add button here could only ever produce a rejection.
        renderPage({ canManageAssignments: true, assignment: INTERNAL, sows: [] });

        expect(screen.queryByRole('heading', { name: /sows/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('button', { name: /add sow/i })).not.toBeInTheDocument();
      });

      it('hides Invoice frequency on BOTH surfaces — internal work is never invoiced', () => {
        // Two places carry it on this screen: the read-only `dl` above the form (the EFFECTIVE
        // cadence the server resolved) and the form's own override select. `queryAllByText` rather
        // than `queryByText` precisely because there are normally two matches.
        renderPage({ canManageAssignments: true, assignment: INTERNAL, sows: [] });

        expect(screen.queryAllByText(/^invoice frequency$/i)).toHaveLength(0);
        expect(screen.queryByLabelText(/invoice frequency/i)).not.toBeInTheDocument();
      });

      it('shows all three for an ordinary client (the control)', () => {
        // Without this the assertions above pass equally well against a screen that renders neither
        // section for anybody.
        renderPage({ canManageAssignments: true, assignment: ASSIGNMENT, sows: [] });

        expect(screen.getByRole('heading', { name: /sows/i })).toBeInTheDocument();
        expect(screen.getByRole('button', { name: /add sow/i })).toBeInTheDocument();
        expect(screen.queryAllByText(/^invoice frequency$/i).length).toBeGreaterThan(0);
        expect(screen.getByLabelText(/invoice frequency/i)).toBeInTheDocument();
      });
    });

    it('closes the edit modal on Cancel without calling onUpdateSow', async () => {
      const onUpdateSow = vi.fn();
      renderPage({
        canManageAssignments: true,
        sows: [
          {
            id: 7,
            sowType: 'SowExtension',
            sowStartDate: '2025-01-01',
            sowEndDate: '2025-12-31',
            rateIncrease: true,
            note: 'FY25 renewal',
          },
        ],
        onUpdateSow,
      });

      await userEvent.click(screen.getByRole('button', { name: /edit/i }));
      await userEvent.click(screen.getByRole('button', { name: /cancel/i }));

      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(onUpdateSow).not.toHaveBeenCalled();
    });
  });
});

describe('Delete Assignment (issue #593)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('omits the Delete Assignment action when canDeleteAssignment is not passed', () => {
    renderPage();

    expect(screen.queryByRole('button', { name: /delete assignment/i })).not.toBeInTheDocument();
  });

  it('omits the Delete Assignment action for a viewer who can manage but not delete (e.g. Ops)', () => {
    renderPage({ canManageAssignments: true, canDeleteAssignment: false });

    expect(screen.queryByRole('button', { name: /delete assignment/i })).not.toBeInTheDocument();
  });

  it('renders Delete Assignment for a Compass Super Admin', () => {
    renderPage({ canDeleteAssignment: true });

    expect(screen.getByRole('button', { name: /delete assignment/i })).toBeInTheDocument();
  });

  it('asks for confirmation and calls onDeleteAssignment when confirmed', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const onDeleteAssignment = vi.fn().mockResolvedValue({ kind: 'deleted' });
    renderPage({ canDeleteAssignment: true, onDeleteAssignment });

    await userEvent.click(screen.getByRole('button', { name: /delete assignment/i }));

    expect(window.confirm).toHaveBeenCalledWith(expect.stringMatching(/cannot be undone/i));
    expect(onDeleteAssignment).toHaveBeenCalled();
  });

  it('does NOT call onDeleteAssignment when the confirmation is dismissed', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const onDeleteAssignment = vi.fn().mockResolvedValue({ kind: 'deleted' });
    renderPage({ canDeleteAssignment: true, onDeleteAssignment });

    await userEvent.click(screen.getByRole('button', { name: /delete assignment/i }));

    expect(onDeleteAssignment).not.toHaveBeenCalled();
  });

  it('surfaces a rejected assignment delete, rather than appearing to do nothing', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const onDeleteAssignment = vi.fn().mockResolvedValue({
      kind: 'rejected',
      message: 'You do not have permission to delete assignments.',
    });
    renderPage({ canDeleteAssignment: true, onDeleteAssignment });

    await userEvent.click(screen.getByRole('button', { name: /delete assignment/i }));

    expect(
      await screen.findByText('You do not have permission to delete assignments.'),
    ).toBeInTheDocument();
  });

  it('clears a stale rejection message once a later delete attempt succeeds', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const onDeleteAssignment = vi
      .fn()
      .mockResolvedValueOnce({ kind: 'rejected', message: 'The assignment could not be deleted.' })
      .mockResolvedValueOnce({ kind: 'deleted' });
    renderPage({ canDeleteAssignment: true, onDeleteAssignment });

    await userEvent.click(screen.getByRole('button', { name: /delete assignment/i }));
    expect(await screen.findByText('The assignment could not be deleted.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /delete assignment/i }));

    await waitFor(() =>
      expect(screen.queryByText('The assignment could not be deleted.')).not.toBeInTheDocument(),
    );
  });

  it('surfaces a generic message when the delete call itself rejects, and handles the rejection', async () => {
    // `apiFetch` resolves for a 4xx, so `settleDelete` covers a REFUSAL. A transport failure —
    // offline, DNS, CORS, an abort — rejects instead, and an uncaught rejection here leaves the user
    // having confirmed the popup and seen nothing at all: the exact defect this slot exists to close,
    // for a different input (PR #604 review, finding 1).
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    // Silenced AND asserted: the friendly sentence alone would hide a plain programming bug caught
    // by this same catch, so the console trace is part of the behaviour (PR #604 review, finding 3).
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const unhandled: unknown[] = [];
    const collect = (reason: unknown) => unhandled.push(reason);
    process.on('unhandledRejection', collect);

    try {
      renderPage({
        canDeleteAssignment: true,
        onDeleteAssignment: vi.fn().mockRejectedValue(new TypeError('Failed to fetch')),
      });

      await userEvent.click(screen.getByRole('button', { name: /delete assignment/i }));

      expect(await screen.findByText('The assignment could not be deleted.')).toBeInTheDocument();
      expect(consoleError).toHaveBeenCalledWith(
        expect.stringContaining('delete request failed'),
        expect.any(TypeError),
      );
      // A macrotask turn is what Node needs before it reports a rejection nobody handled.
      await new Promise((resolve) => setTimeout(resolve, 0));
      expect(unhandled).toEqual([]);
    } finally {
      process.off('unhandledRejection', collect);
    }
  });

  it('clears a stale delete rejection once an edit in the same section succeeds', async () => {
    // The slot belongs to the assignment section, so a successful write in that section is what
    // retires its message — `LookupSection`'s per-section rule (PR #604 review, finding 2).
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderPage({
      canManageAssignments: true,
      canDeleteAssignment: true,
      onDeleteAssignment: vi
        .fn()
        .mockResolvedValue({ kind: 'rejected', message: 'The assignment could not be deleted.' }),
      onSubmit: vi.fn().mockResolvedValue({ kind: 'saved' }),
    });

    await userEvent.click(screen.getByRole('button', { name: /delete assignment/i }));
    expect(await screen.findByText('The assignment could not be deleted.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /^save$/i }));

    await waitFor(() =>
      expect(screen.queryByText('The assignment could not be deleted.')).not.toBeInTheDocument(),
    );
  });

  it('KEEPS the delete rejection when the edit in that section is itself refused', async () => {
    // The other arm of the rule above, and the reason clearing is conditional rather than
    // unconditional: a write that did not happen changed nothing, so the delete message still
    // describes the current state and retiring it would be a lie.
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderPage({
      canManageAssignments: true,
      canDeleteAssignment: true,
      onDeleteAssignment: vi
        .fn()
        .mockResolvedValue({ kind: 'rejected', message: 'The assignment could not be deleted.' }),
      onSubmit: vi.fn().mockResolvedValue({ kind: 'rejected', message: 'Nope.' }),
    });

    await userEvent.click(screen.getByRole('button', { name: /delete assignment/i }));
    expect(await screen.findByText('The assignment could not be deleted.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /^save$/i }));

    expect(await screen.findByText('Nope.')).toBeInTheDocument();
    expect(screen.getByText('The assignment could not be deleted.')).toBeInTheDocument();
  });
});

describe('Delete SOW (issue #593)', () => {
  const SOWS: SowListRow[] = [
    {
      id: 5,
      sowType: 'InitialContract',
      sowStartDate: '2024-04-01',
      sowEndDate: '2024-12-31',
      rateIncrease: false,
      note: 'Signed',
    },
  ];

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('omits the per-row Delete action when canDeleteAssignment is false', async () => {
    renderPage({ canManageAssignments: true, sows: SOWS, canDeleteAssignment: false });

    await screen.findByRole('table', { name: /contract periods/i });

    expect(screen.queryByRole('button', { name: /^delete$/i })).not.toBeInTheDocument();
  });

  it('asks for confirmation and calls onDeleteSow with the row id when confirmed', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const onDeleteSow = vi.fn().mockResolvedValue({ kind: 'deleted' });
    renderPage({
      canManageAssignments: true,
      canDeleteAssignment: true,
      sows: SOWS,
      onDeleteSow,
    });

    await userEvent.click(await screen.findByRole('button', { name: /^delete$/i }));

    expect(window.confirm).toHaveBeenCalledWith(expect.stringMatching(/cannot be undone/i));
    expect(onDeleteSow).toHaveBeenCalledWith(5);
  });

  it('does NOT call onDeleteSow when the confirmation is dismissed', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false);
    const onDeleteSow = vi.fn().mockResolvedValue({ kind: 'deleted' });
    renderPage({
      canManageAssignments: true,
      canDeleteAssignment: true,
      sows: SOWS,
      onDeleteSow,
    });

    await userEvent.click(await screen.findByRole('button', { name: /^delete$/i }));

    expect(onDeleteSow).not.toHaveBeenCalled();
  });

  it('surfaces a rejected SOW delete, rather than appearing to do nothing', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const onDeleteSow = vi.fn().mockResolvedValue({
      kind: 'rejected',
      message: 'You do not have permission to delete SOWs.',
    });
    renderPage({
      canManageAssignments: true,
      canDeleteAssignment: true,
      sows: SOWS,
      onDeleteSow,
    });

    await userEvent.click(await screen.findByRole('button', { name: /^delete$/i }));

    expect(
      await screen.findByText('You do not have permission to delete SOWs.'),
    ).toBeInTheDocument();
  });

  it('keeps the SOW rejection in the SOWs card, not in the assignment slot', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const onDeleteSow = vi
      .fn()
      .mockResolvedValueOnce({ kind: 'rejected', message: 'The SOW could not be deleted.' })
      .mockResolvedValueOnce({ kind: 'deleted' });
    renderPage({
      canManageAssignments: true,
      canDeleteAssignment: true,
      sows: SOWS,
      onDeleteSow,
    });

    await userEvent.click(await screen.findByRole('button', { name: /^delete$/i }));
    // The card owns the slot, so the message sits inside the SOWs region rather than at the top of
    // the page — the whole reason there are two slots (the card is well below the fold).
    const card = await screen.findByRole('region', { name: 'SOWs / Contracts' });
    expect(within(card).getByText('The SOW could not be deleted.')).toBeInTheDocument();
    // `within(card)` alone is satisfied by one of two duplicates, so rendering this message in BOTH
    // slots left this test passing — proven by mutation (PR #604 review, finding 4). The count is
    // what enforces the second half of the name.
    expect(screen.getAllByText('The SOW could not be deleted.')).toHaveLength(1);

    await userEvent.click(screen.getByRole('button', { name: /^delete$/i }));

    await waitFor(() =>
      expect(screen.queryByText('The SOW could not be deleted.')).not.toBeInTheDocument(),
    );
  });

  it('surfaces a generic message when the SOW delete call itself rejects, and handles the rejection', async () => {
    // The transport-failure counterpart of the assignment case above (PR #604 review, finding 1).
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const unhandled: unknown[] = [];
    const collect = (reason: unknown) => unhandled.push(reason);
    process.on('unhandledRejection', collect);

    try {
      renderPage({
        canManageAssignments: true,
        canDeleteAssignment: true,
        sows: SOWS,
        onDeleteSow: vi.fn().mockRejectedValue(new TypeError('Failed to fetch')),
      });

      await userEvent.click(await screen.findByRole('button', { name: /^delete$/i }));

      expect(await screen.findByText('The SOW could not be deleted.')).toBeInTheDocument();
      expect(consoleError).toHaveBeenCalledWith(
        expect.stringContaining('delete request failed'),
        expect.any(TypeError),
      );
      await new Promise((resolve) => setTimeout(resolve, 0));
      expect(unhandled).toEqual([]);
    } finally {
      process.off('unhandledRejection', collect);
    }
  });

  it('clears a stale SOW delete rejection once a create in the same card succeeds', async () => {
    // Measured before the fix: refuse a delete, then successfully add a SOW, and the banner was
    // still sitting above a card that now listed the new row (PR #604 review, finding 2).
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderPage({
      canManageAssignments: true,
      canDeleteAssignment: true,
      sows: SOWS,
      onDeleteSow: vi
        .fn()
        .mockResolvedValue({ kind: 'rejected', message: 'The SOW could not be deleted.' }),
      onCreateSow: vi.fn().mockResolvedValue({ kind: 'saved' }),
    });

    await userEvent.click(await screen.findByRole('button', { name: /^delete$/i }));
    expect(await screen.findByText('The SOW could not be deleted.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /add sow/i }));
    await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
    await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-04-01');
    await userEvent.type(screen.getByLabelText(/sow end date/i), '2024-12-31');
    await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

    await waitFor(() =>
      expect(screen.queryByText('The SOW could not be deleted.')).not.toBeInTheDocument(),
    );
  });

  it('clears a stale SOW delete rejection once an EDIT in the same card succeeds', async () => {
    // The slot's docstring claims "create OR edit"; only create was tested, and deleting the
    // clear-on-edit line left the whole suite green (PR #604 review, finding 2).
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    renderPage({
      canManageAssignments: true,
      canDeleteAssignment: true,
      sows: SOWS,
      onDeleteSow: vi
        .fn()
        .mockResolvedValue({ kind: 'rejected', message: 'The SOW could not be deleted.' }),
      onUpdateSow: vi.fn().mockResolvedValue({ kind: 'saved' }),
    });

    await userEvent.click(await screen.findByRole('button', { name: /^delete$/i }));
    expect(await screen.findByText('The SOW could not be deleted.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /^edit$/i }));
    await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

    await waitFor(() =>
      expect(screen.queryByText('The SOW could not be deleted.')).not.toBeInTheDocument(),
    );
  });

  /**
   * The clearing direction of the per-section rule, which nothing fenced: a successful write in one
   * section clearing the OTHER section's slot survived the whole suite (PR #604 review, finding 2).
   * Per-section ownership is the entire justification for having two slots.
   *
   * Each case fills BOTH slots, then waits for the WRITTEN section's message to go — a positive
   * transition proving the `kind === 'saved'` branch has run — before asserting the other survived.
   * Waiting on the mock's call alone would let a cross-section clear slip in after the assertion.
   */
  describe('a successful write clears only its own section (PR #604 review)', () => {
    function fillBothSlots(overrides: Parameters<typeof renderPage>[0]) {
      vi.spyOn(window, 'confirm').mockReturnValue(true);
      return renderPage({
        canManageAssignments: true,
        canDeleteAssignment: true,
        sows: SOWS,
        onDeleteAssignment: vi
          .fn()
          .mockResolvedValue({ kind: 'rejected', message: 'The assignment could not be deleted.' }),
        onDeleteSow: vi
          .fn()
          .mockResolvedValue({ kind: 'rejected', message: 'The SOW could not be deleted.' }),
        ...overrides,
      });
    }

    async function refuseBothDeletes() {
      await userEvent.click(await screen.findByRole('button', { name: /^delete$/i }));
      expect(await screen.findByText('The SOW could not be deleted.')).toBeInTheDocument();
      await userEvent.click(screen.getByRole('button', { name: /delete assignment/i }));
      expect(await screen.findByText('The assignment could not be deleted.')).toBeInTheDocument();
    }

    it('keeps the SOW slot when an assignment edit succeeds', async () => {
      fillBothSlots({ onSubmit: vi.fn().mockResolvedValue({ kind: 'saved' }) });
      await refuseBothDeletes();

      await userEvent.click(screen.getByRole('button', { name: /^save$/i }));

      await waitFor(() =>
        expect(screen.queryByText('The assignment could not be deleted.')).not.toBeInTheDocument(),
      );
      expect(screen.getByText('The SOW could not be deleted.')).toBeInTheDocument();
    });

    it('keeps the assignment slot when a SOW create succeeds', async () => {
      fillBothSlots({ onCreateSow: vi.fn().mockResolvedValue({ kind: 'saved' }) });
      await refuseBothDeletes();

      await userEvent.click(screen.getByRole('button', { name: /add sow/i }));
      await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
      await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-04-01');
      await userEvent.type(screen.getByLabelText(/sow end date/i), '2024-12-31');
      await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

      await waitFor(() =>
        expect(screen.queryByText('The SOW could not be deleted.')).not.toBeInTheDocument(),
      );
      expect(screen.getByText('The assignment could not be deleted.')).toBeInTheDocument();
    });

    it('keeps the assignment slot when a SOW edit succeeds', async () => {
      fillBothSlots({ onUpdateSow: vi.fn().mockResolvedValue({ kind: 'saved' }) });
      await refuseBothDeletes();

      await userEvent.click(screen.getByRole('button', { name: /^edit$/i }));
      await userEvent.click(screen.getByRole('button', { name: /save sow/i }));

      await waitFor(() =>
        expect(screen.queryByText('The SOW could not be deleted.')).not.toBeInTheDocument(),
      );
      expect(screen.getByText('The assignment could not be deleted.')).toBeInTheDocument();
    });
  });
});

describe('accessibility of the SOWs / Contracts card (#448)', () => {
  /**
   * Keeps one specific fix gated: `SowList`'s final column header used to be `''`, which axe reports
   * as `empty-table-header`. This page is its only caller and had no accessibility test. When this
   * was written there was also no screenshot baseline of an assignment screen; since #574 there are
   * no screenshot baselines of ANY screen, so this test is even more squarely the only thing that
   * would notice a revert.
   */
  const SOWS: SowListRow[] = [
    {
      id: 5,
      sowType: 'InitialContract',
      sowStartDate: '2024-04-01',
      sowEndDate: '2024-12-31',
      rateIncrease: false,
      note: 'Signed',
    },
  ];

  it('has no violations with the contract periods listed', async () => {
    const { container } = renderPage({ canManageAssignments: true, sows: SOWS });

    await screen.findByRole('table', { name: /contract periods/i });

    const result = await checkAccessibility(container);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });

  it('has no violations with the SOW dialog open', async () => {
    // The modal portals to `document.body`, so the render container would scan the page BEHIND it and
    // report zero for the wrong reason.
    renderPage({ canManageAssignments: true, sows: SOWS });

    await userEvent.click(screen.getByRole('button', { name: '+ Add SOW / Contract' }));
    await screen.findByRole('dialog', { name: 'Add SOW / Contract' });

    const result = await checkAccessibility(document.body);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });

  // This describe now spies on `window.confirm`, so it restores like the delete describes do.
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('has no violations with both delete-rejection slots filled (PR #604 review)', async () => {
    // The `toHaveLength(2)` below is the ONLY assertion in the suite that the two slots are
    // independently visible, and that is the real reason to keep this case. Measured by mutation:
    // rendering the SOW slot only while the assignment slot is empty fails this test and NOTHING
    // else in 1436. (Two `role="alert"` nodes at once is not the rare part — the cross-section
    // cases above reach that too, as does any refused write next to `AssignmentForm`'s own alert.)
    // The axe sweep over that state rides along for free.
    //
    // What this does NOT check is contrast: `checkAccessibility` disables axe's `color-contrast`
    // rule, and its own walk reads `getComputedStyle`, which under jsdom resolves inline styles
    // only. `setup.ts` loads no stylesheet, so every Tailwind class here computes to nothing: the
    // walk measures jsdom's default black over the harness's own `PAGE_DEFAULT_BACKGROUND`
    // fallback, never `bg-brand-taupe/10` under `text-brand-gray`. That real pairing is measured by
    // `tests/unit/brand-contrast.test.ts`, and in a real browser by the Playwright axe sweep.
    vi.spyOn(window, 'confirm').mockReturnValue(true);
    const rejected = { kind: 'rejected', message: 'That could not be deleted.' };
    const { container } = renderPage({
      canManageAssignments: true,
      canDeleteAssignment: true,
      sows: SOWS,
      onDeleteAssignment: vi.fn().mockResolvedValue(rejected),
      onDeleteSow: vi.fn().mockResolvedValue(rejected),
    });

    await userEvent.click(await screen.findByRole('button', { name: /^delete$/i }));
    await userEvent.click(screen.getByRole('button', { name: /delete assignment/i }));
    expect(await screen.findAllByText('That could not be deleted.')).toHaveLength(2);

    const result = await checkAccessibility(container);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });

  it('has no violations with the Delete Assignment action and per-row Delete visible (issue #593)', async () => {
    const { container } = renderPage({
      canManageAssignments: true,
      canDeleteAssignment: true,
      sows: SOWS,
    });

    await screen.findByRole('table', { name: /contract periods/i });
    await screen.findByRole('button', { name: /delete assignment/i });

    const result = await checkAccessibility(container);

    expect(result.axeViolations).toEqual([]);
    expect(result.contrastViolations).toEqual([]);
  });
});
