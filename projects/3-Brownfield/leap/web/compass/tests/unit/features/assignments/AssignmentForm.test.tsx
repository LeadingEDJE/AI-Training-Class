import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AssignmentForm } from '../../../../src/features/assignments/AssignmentForm';

describe('AssignmentForm', () => {
  describe('create mode', () => {
    it('submits the entered values', async () => {
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(<AssignmentForm mode="create" onSubmit={onSubmit} />);

      await userEvent.type(screen.getByLabelText(/edjer/i), '10');
      await userEvent.type(screen.getByLabelText(/client/i), '100');
      await userEvent.type(screen.getByLabelText(/start date/i), '2026-01-01');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({
          employeeId: 10,
          clientId: 100,
          startDate: '2026-01-01',
          endDate: null,
          note: null,
        }),
      );
    });

    it('renders no end date by default — open-ended is the norm', () => {
      render(<AssignmentForm mode="create" onSubmit={vi.fn()} />);

      expect(screen.getByLabelText(/end date/i)).toHaveValue('');
    });

    it('submits a typed note, or null when left blank', async () => {
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(<AssignmentForm mode="create" onSubmit={onSubmit} />);

      await userEvent.type(screen.getByLabelText(/edjer/i), '10');
      await userEvent.type(screen.getByLabelText(/client/i), '100');
      await userEvent.type(screen.getByLabelText(/start date/i), '2026-01-01');
      await userEvent.type(screen.getByLabelText(/note/i), 'A note');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ note: 'A note' }));
    });
  });

  describe('edit mode', () => {
    const initialValues = {
      employeeId: 10,
      clientId: 100,
      startDate: '2026-01-01',
      endDate: null as string | null,
      note: 'Existing note',
      invoiceFrequencyTypeId: null as number | null,
    };

    it('pre-populates from the existing assignment', () => {
      render(
        <AssignmentForm mode="edit" canEdit initialValues={initialValues} onSubmit={vi.fn()} />,
      );

      expect(screen.getByLabelText(/start date/i)).toHaveValue('2026-01-01');
      expect(screen.getByLabelText(/note/i)).toHaveValue('Existing note');
    });

    it('does not let the EDJEr or client be changed (contract §2)', () => {
      render(
        <AssignmentForm mode="edit" canEdit initialValues={initialValues} onSubmit={vi.fn()} />,
      );

      expect(screen.queryByLabelText(/edjer/i)).not.toBeInTheDocument();
      expect(screen.queryByLabelText(/client/i)).not.toBeInTheDocument();
    });

    it('submits an end date, ending the assignment', async () => {
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(
        <AssignmentForm mode="edit" canEdit initialValues={initialValues} onSubmit={onSubmit} />,
      );

      await userEvent.type(screen.getByLabelText(/end date/i), '2026-06-30');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ endDate: '2026-06-30' }));
    });

    describe('canEdit=false (AC-16/FR-025 — Compass Admin/Sales can view, not write)', () => {
      it('renders the dates and note as plain text', () => {
        render(
          <AssignmentForm
            mode="edit"
            canEdit={false}
            initialValues={initialValues}
            onSubmit={vi.fn()}
          />,
        );

        expect(screen.getByText('01/01/2026')).toBeInTheDocument();
        expect(screen.getByText('Existing note')).toBeInTheDocument();
      });

      it('shows an open-ended assignment as "Current" rather than a blank end date', () => {
        render(
          <AssignmentForm
            mode="edit"
            canEdit={false}
            initialValues={initialValues}
            onSubmit={vi.fn()}
          />,
        );

        expect(screen.getByText(/current/i)).toBeInTheDocument();
      });

      it('shows a placeholder rather than nothing when no note was recorded', () => {
        render(
          <AssignmentForm
            mode="edit"
            canEdit={false}
            initialValues={{ ...initialValues, note: null }}
            onSubmit={vi.fn()}
          />,
        );

        expect(screen.getByText('—')).toBeInTheDocument();
      });

      it('renders no Save button and no editable input', () => {
        render(
          <AssignmentForm
            mode="edit"
            canEdit={false}
            initialValues={initialValues}
            onSubmit={vi.fn()}
          />,
        );

        expect(screen.queryByRole('button', { name: /save/i })).not.toBeInTheDocument();
        expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
      });
    });
  });

  // ---------------------------------------------------------------- FR-008 rejection display

  it('shows the FR-008 rejection when the end date precedes the start date', async () => {
    const onSubmit = vi.fn().mockResolvedValue({
      kind: 'rejected',
      message: 'The end date must be on or after the start date.',
    });
    render(<AssignmentForm mode="create" onSubmit={onSubmit} />);

    await userEvent.type(screen.getByLabelText(/edjer/i), '10');
    await userEvent.type(screen.getByLabelText(/client/i), '100');
    await userEvent.type(screen.getByLabelText(/start date/i), '2026-06-01');
    await userEvent.type(screen.getByLabelText(/end date/i), '2026-01-01');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The end date must be on or after the start date.',
    );
  });

  // ---------------------------------------------------------------- T037a / FR-050

  it('renders NO audit-reason input — reasons are system-generated (FR-050)', () => {
    render(<AssignmentForm mode="create" onSubmit={vi.fn()} />);

    expect(screen.queryByLabelText(/reason/i)).not.toBeInTheDocument();
  });

  const CADENCES = [
    { id: 1, typeName: 'Weekly' },
    { id: 2, typeName: 'Monthly' },
  ];

  /** Its own fixture: `edit mode`'s lives inside that describe and is not in scope here. */
  const EDIT_VALUES = {
    employeeId: 10,
    clientId: 100,
    startDate: '2026-01-01',
    endDate: null as string | null,
    note: 'Existing note',
    invoiceFrequencyTypeId: null as number | null,
  };

  describe('the FIXED side is read-only text, not a required input (#84)', () => {
    it('does not put aria-required on the fixed EDJEr value', () => {
      // `FormField` attaches aria-required to whatever child it is given, and the fixed side is a
      // `<p>`. aria-required on a paragraph is invalid ARIA -- axe rates `aria-allowed-attr` CRITICAL
      // -- and it also tells the reader to supply something the screen has already decided for them.
      // Shipped until the #84 WCAG sweep found it at 390x844.
      render(
        <AssignmentForm
          mode="create"
          fixedEmployee={{ id: 9, label: 'Maya Alvarez' }}
          renderClientPicker={(value, onChange, fieldId) => (
            <select id={fieldId} value={value} onChange={(e) => onChange(e.target.value)}>
              <option value="">Select a client</option>
            </select>
          )}
          onSubmit={vi.fn()}
        />,
      );

      expect(screen.getByText('Maya Alvarez')).not.toHaveAttribute('aria-required');
    });
  });

  describe('invoice frequency override (US6, #64)', () => {
    describe('the read-only view names the stored override (AC-16/FR-025 viewers)', () => {
      it('names the cadence when the stored override is still active', () => {
        render(
          <AssignmentForm
            mode="edit"
            canEdit={false}
            initialValues={{ ...EDIT_VALUES, invoiceFrequencyTypeId: 1 }}
            invoiceFrequencyTypes={CADENCES}
            onSubmit={vi.fn()}
          />,
        );

        expect(screen.getByText('Weekly')).toBeInTheDocument();
      });

      it('still says something when the stored override has since been retired', () => {
        // The id is absent from the ACTIVE list, but it is still the cadence this assignment bills
        // on. Rendering an empty cell would say the opposite -- that no override is set -- which is a
        // different and wrong answer, so the fallback names the state rather than showing nothing.
        render(
          <AssignmentForm
            mode="edit"
            canEdit={false}
            initialValues={{ ...EDIT_VALUES, invoiceFrequencyTypeId: 99 }}
            invoiceFrequencyTypes={CADENCES}
            onSubmit={vi.fn()}
          />,
        );

        expect(screen.getByText('Retired cadence')).toBeInTheDocument();
      });

      it('says "Client default" when no override is stored', () => {
        render(
          <AssignmentForm
            mode="edit"
            canEdit={false}
            initialValues={EDIT_VALUES}
            invoiceFrequencyTypes={CADENCES}
            onSubmit={vi.fn()}
          />,
        );

        expect(screen.getByText('Client default')).toBeInTheDocument();
      });
    });

    it('offers an explicit no-override option alongside the active cadences', () => {
      // "Bill the way this client does" is a CHOICE an engagement makes, so the select has to be able
      // to express it. Without that option every assignment would be forced to carry a cadence of its
      // own, which is the opposite of FR-037's default-applies rule.
      render(<AssignmentForm mode="create" invoiceFrequencyTypes={CADENCES} onSubmit={vi.fn()} />);

      const select = screen.getByLabelText(/invoice frequency/i);
      expect(
        within(select)
          .getAllByRole('option')
          .map((o) => o.textContent),
      ).toEqual(['Use the client default', 'Weekly', 'Monthly']);
    });

    it('submits null when no override is chosen — not zero', () => {
      // `Number('')` is 0, an id no cadence has. Sending it would be a 400 on a field the user
      // deliberately left alone.
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(<AssignmentForm mode="create" invoiceFrequencyTypes={CADENCES} onSubmit={onSubmit} />);

      return (async () => {
        await userEvent.type(screen.getByLabelText(/edjer/i), '10');
        await userEvent.type(screen.getByLabelText(/client/i), '100');
        await userEvent.type(screen.getByLabelText(/start date/i), '2026-01-01');
        await userEvent.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledWith(
          expect.objectContaining({ invoiceFrequencyTypeId: null }),
        );
      })();
    });

    it('submits the chosen cadence as a number', async () => {
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(<AssignmentForm mode="create" invoiceFrequencyTypes={CADENCES} onSubmit={onSubmit} />);

      await userEvent.type(screen.getByLabelText(/edjer/i), '10');
      await userEvent.type(screen.getByLabelText(/client/i), '100');
      await userEvent.type(screen.getByLabelText(/start date/i), '2026-01-01');
      await userEvent.selectOptions(screen.getByLabelText(/invoice frequency/i), '2');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ invoiceFrequencyTypeId: 2 }));
    });

    it('keeps a stored cadence that has since been retired, rather than silently clearing it', async () => {
      // The grandfathering rule, on the screen. The stored override (99) is not in the active list, so
      // an active-only select would drop it — and the next save of an unrelated field would clear a
      // cadence nobody touched. The server applies the same rule (FR-038, matching 004 US3).
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(
        <AssignmentForm
          mode="edit"
          canEdit
          initialValues={{ ...EDIT_VALUES, invoiceFrequencyTypeId: 99 }}
          invoiceFrequencyTypes={CADENCES}
          onSubmit={onSubmit}
        />,
      );

      const select = screen.getByLabelText(/invoice frequency/i);
      expect(select).toHaveValue('99');
      expect(within(select).getByRole('option', { name: /retired/i })).toBeInTheDocument();

      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({ invoiceFrequencyTypeId: 99 }),
      );
    });

    describe('an internal client is never invoiced, so the override is not offered (issue #518)', () => {
      it('omits the select from the editable form', () => {
        render(
          <AssignmentForm
            mode="edit"
            canEdit
            clientIsInternal
            initialValues={{ ...EDIT_VALUES, invoiceFrequencyTypeId: 2 }}
            invoiceFrequencyTypes={CADENCES}
            onSubmit={vi.fn()}
          />,
        );

        expect(screen.queryByLabelText(/invoice frequency/i)).not.toBeInTheDocument();
      });

      it('still offers the select for an ordinary client (the control)', () => {
        render(
          <AssignmentForm
            mode="edit"
            canEdit
            initialValues={{ ...EDIT_VALUES, invoiceFrequencyTypeId: 2 }}
            invoiceFrequencyTypes={CADENCES}
            onSubmit={vi.fn()}
          />,
        );

        expect(screen.getByLabelText(/invoice frequency/i)).toBeInTheDocument();
      });

      it('omits the row from the read-only view too', () => {
        render(
          <AssignmentForm
            mode="edit"
            canEdit={false}
            clientIsInternal
            initialValues={{ ...EDIT_VALUES, invoiceFrequencyTypeId: 1 }}
            invoiceFrequencyTypes={CADENCES}
            onSubmit={vi.fn()}
          />,
        );

        expect(screen.queryByText(/^invoice frequency$/i)).not.toBeInTheDocument();
        expect(screen.queryByText('Weekly')).not.toBeInTheDocument();
      });

      it('omits the select from a create too', () => {
        render(
          <AssignmentForm
            mode="create"
            clientIsInternal
            fixedClient={{ id: 100, label: 'EDJE (beach)' }}
            invoiceFrequencyTypes={CADENCES}
            onSubmit={vi.fn()}
          />,
        );

        expect(screen.queryByLabelText(/invoice frequency/i)).not.toBeInTheDocument();
      });

      it('STILL SUBMITS the stored override — a hidden field is not a cleared field', async () => {
        // The load-bearing half of this change. Hiding a control is a DISPLAY decision; if it also
        // dropped `invoiceFrequencyTypeId` from the payload, then saving an unrelated edit to a date
        // or a note would silently null a stored override the user never touched, and the server
        // would faithfully persist that. A data bug arriving as a side effect of a layout rule.
        const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
        render(
          <AssignmentForm
            mode="edit"
            canEdit
            clientIsInternal
            initialValues={{ ...EDIT_VALUES, invoiceFrequencyTypeId: 2 }}
            invoiceFrequencyTypes={CADENCES}
            onSubmit={onSubmit}
          />,
        );

        await userEvent.type(screen.getByLabelText(/end date/i), '2026-06-30');
        await userEvent.click(screen.getByRole('button', { name: /save/i }));

        expect(onSubmit).toHaveBeenCalledWith(
          expect.objectContaining({ endDate: '2026-06-30', invoiceFrequencyTypeId: 2 }),
        );
      });
    });

    it('pre-populates an existing override and can clear it back to the client default', async () => {
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(
        <AssignmentForm
          mode="edit"
          canEdit
          initialValues={{ ...EDIT_VALUES, invoiceFrequencyTypeId: 2 }}
          invoiceFrequencyTypes={CADENCES}
          onSubmit={onSubmit}
        />,
      );

      expect(screen.getByLabelText(/invoice frequency/i)).toHaveValue('2');

      await userEvent.selectOptions(screen.getByLabelText(/invoice frequency/i), '');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({ invoiceFrequencyTypeId: null }),
      );
    });
  });
});
