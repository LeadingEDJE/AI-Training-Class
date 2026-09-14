import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SowForm } from '../../../../src/features/assignments/SowForm';

describe('SowForm', () => {
  describe('create mode', () => {
    it('submits an Initial Contract with no rate-increase field', async () => {
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(<SowForm mode="create" onSubmit={onSubmit} onCancel={vi.fn()} />);

      await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
      await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-04-01');
      await userEvent.type(screen.getByLabelText(/sow end date/i), '2024-12-31');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({
          sowType: 'InitialContract',
          rateIncrease: false,
          sowStartDate: '2024-04-01',
          sowEndDate: '2024-12-31',
        }),
      );
    });

    it('submits a SOW Extension with the rate-increase choice', async () => {
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(<SowForm mode="create" onSubmit={onSubmit} onCancel={vi.fn()} />);

      await userEvent.click(screen.getByRole('radio', { name: /sow extension/i }));
      await userEvent.click(screen.getByRole('radio', { name: /^yes$/i }));
      await userEvent.type(screen.getByLabelText(/sow start date/i), '2025-01-01');
      await userEvent.type(screen.getByLabelText(/sow end date/i), '2025-12-31');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({ sowType: 'SowExtension', rateIncrease: true }),
      );
    });

    it('submits rateIncrease false when No is chosen for an Extension', async () => {
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(<SowForm mode="create" onSubmit={onSubmit} onCancel={vi.fn()} />);

      await userEvent.click(screen.getByRole('radio', { name: /sow extension/i }));
      await userEvent.click(screen.getByRole('radio', { name: /^yes$/i }));
      await userEvent.click(screen.getByRole('radio', { name: /^no$/i }));
      await userEvent.type(screen.getByLabelText(/sow start date/i), '2025-01-01');
      await userEvent.type(screen.getByLabelText(/sow end date/i), '2025-12-31');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ rateIncrease: false }));
    });

    // -------------------------------------------------------- SOW-2: conditional rate-increase

    it('shows the rate-increase control ONLY when Extension is selected', async () => {
      render(<SowForm mode="create" onSubmit={vi.fn()} onCancel={vi.fn()} />);

      expect(screen.queryByText(/rate increase/i)).not.toBeInTheDocument();

      await userEvent.click(screen.getByRole('radio', { name: /sow extension/i }));

      expect(screen.getByText(/rate increase/i)).toBeInTheDocument();
    });

    it('hides the rate-increase control again when switching back to Initial Contract', async () => {
      render(<SowForm mode="create" onSubmit={vi.fn()} onCancel={vi.fn()} />);

      await userEvent.click(screen.getByRole('radio', { name: /sow extension/i }));
      expect(screen.getByText(/rate increase/i)).toBeInTheDocument();

      await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));

      expect(screen.queryByText(/rate increase/i)).not.toBeInTheDocument();
    });

    it('submits a typed note, or null when left blank', async () => {
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(<SowForm mode="create" onSubmit={onSubmit} onCancel={vi.fn()} />);

      await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
      await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-04-01');
      await userEvent.type(screen.getByLabelText(/sow end date/i), '2024-12-31');
      await userEvent.type(screen.getByLabelText(/note/i), 'Initial 9-month SOW');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({ note: 'Initial 9-month SOW' }),
      );
    });
  });

  // ---------------------------------------------------------- issue #626: SOW Extension default

  describe('defaultSowType (create mode)', () => {
    it('pre-selects SOW Extension when told the assignment already has a contract period', () => {
      render(
        <SowForm
          mode="create"
          defaultSowType="SowExtension"
          onSubmit={vi.fn()}
          onCancel={vi.fn()}
        />,
      );

      expect(screen.getByRole('radio', { name: /sow extension/i })).toBeChecked();
      expect(screen.getByRole('radio', { name: /initial contract/i })).not.toBeChecked();
    });

    it('still pre-selects Initial Contract when no default is supplied', () => {
      render(<SowForm mode="create" onSubmit={vi.fn()} onCancel={vi.fn()} />);

      expect(screen.getByRole('radio', { name: /initial contract/i })).toBeChecked();
    });

    it('is only a starting point — the choice can still be switched back', async () => {
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(
        <SowForm
          mode="create"
          defaultSowType="SowExtension"
          onSubmit={onSubmit}
          onCancel={vi.fn()}
        />,
      );

      await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
      await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-04-01');
      await userEvent.type(screen.getByLabelText(/sow end date/i), '2024-12-31');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({ sowType: 'InitialContract' }),
      );
    });
  });

  describe('edit mode', () => {
    const initialValues = {
      sowType: 'SowExtension' as const,
      rateIncrease: true,
      sowStartDate: '2025-01-01',
      sowEndDate: '2025-12-31',
      note: 'FY25 renewal',
    };

    it('pre-populates from the existing period', () => {
      render(
        <SowForm mode="edit" initialValues={initialValues} onSubmit={vi.fn()} onCancel={vi.fn()} />,
      );

      expect(screen.getByRole('radio', { name: /sow extension/i })).toBeChecked();
      expect(screen.getByLabelText(/sow start date/i)).toHaveValue('2025-01-01');
      expect(screen.getByLabelText(/note/i)).toHaveValue('FY25 renewal');
    });
  });

  // ------------------------------------------- US8/#67 first-edit grandfathering (issue #404)

  describe('editing a LegacyMigrated period', () => {
    const legacyValues = {
      sowType: 'LegacyMigrated' as const,
      rateIncrease: false,
      sowStartDate: '2018-01-01',
      sowEndDate: '2018-12-31',
      note: 'Migrated from TPS.',
    };

    it('renders the type as read-only, with no way to change it', () => {
      // The server refuses a type change on a legacy row in BOTH directions -- provenance is not
      // editable -- so offering the radios would present a choice every save would reject.
      render(
        <SowForm mode="edit" initialValues={legacyValues} onSubmit={vi.fn()} onCancel={vi.fn()} />,
      );

      expect(screen.queryByRole('radio', { name: /initial contract/i })).not.toBeInTheDocument();
      expect(screen.queryByRole('radio', { name: /sow extension/i })).not.toBeInTheDocument();
      expect(screen.getByText(/legacy migrated/i)).toBeInTheDocument();
    });

    it('offers no rate-increase control, since only an extension may carry one', () => {
      render(
        <SowForm mode="edit" initialValues={legacyValues} onSubmit={vi.fn()} onCancel={vi.fn()} />,
      );

      expect(screen.queryByRole('radio', { name: /^yes$/i })).not.toBeInTheDocument();
    });

    it('lets the dates and note be corrected, and submits the type back unchanged', async () => {
      // The whole point of AC-41: the correction is what exits legacy state, and the type rides along
      // untouched so the server accepts it.
      const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
      render(
        <SowForm mode="edit" initialValues={legacyValues} onSubmit={onSubmit} onCancel={vi.fn()} />,
      );

      const note = screen.getByLabelText(/note/i);
      await userEvent.clear(note);
      await userEvent.type(note, 'Corrected during review.');
      await userEvent.click(screen.getByRole('button', { name: /save/i }));

      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({
          sowType: 'LegacyMigrated',
          rateIncrease: false,
          note: 'Corrected during review.',
        }),
      );
    });
  });

  // ---------------------------------------------------------------- FR-019 overlap rejection

  it('shows the overlap rejection identifying the conflicting period', async () => {
    const onSubmit = vi.fn().mockResolvedValue({
      kind: 'rejected',
      message: 'Dates overlap an existing SOW for this assignment (04/01/2024 – 12/31/2024).',
    });
    render(<SowForm mode="create" onSubmit={onSubmit} onCancel={vi.fn()} />);

    await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
    await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-10-01');
    await userEvent.type(screen.getByLabelText(/sow end date/i), '2025-06-30');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/dates overlap an existing sow/i);
  });

  // ---------------------------------------------------------------- FR-020 / research R-6, T085
  //
  // Date-order is caught CLIENT-SIDE as a courtesy (research R-6: "client checks are a courtesy,
  // the server's rejection is authoritative") and associated with the SOW End Date field itself —
  // not the whole-form alert, which R-6 reserves for cross-field failures that "belong to no one
  // field" (overlap, above). This is the reference implementation FR-051c/SC-010b ask for.

  it('associates the FR-020 rejection with the SOW End Date field, not the whole-form alert', async () => {
    const onSubmit = vi.fn().mockResolvedValue({ kind: 'saved' });
    render(<SowForm mode="create" onSubmit={onSubmit} onCancel={vi.fn()} />);

    await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
    await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-12-31');
    await userEvent.type(screen.getByLabelText(/sow end date/i), '2024-04-01');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(screen.getByLabelText(/sow end date/i)).toHaveAttribute('aria-invalid', 'true');
    });

    const endDateField = screen.getByLabelText(/sow end date/i);
    const describedBy = endDateField.getAttribute('aria-describedby') ?? '';
    const [errorElementId] = describedBy.split(' ');
    expect(errorElementId).toBeTruthy();
    const errorElement = document.getElementById(errorElementId ?? '');
    expect(errorElement).toHaveTextContent(/end date must be on or after the start date/i);

    // Caught client-side — no round trip for a rule this form can check itself (research R-6).
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('clears the FR-020 field-level error once the dates are corrected', async () => {
    render(
      <SowForm
        mode="create"
        onSubmit={vi.fn().mockResolvedValue({ kind: 'saved' })}
        onCancel={vi.fn()}
      />,
    );

    await userEvent.click(screen.getByRole('radio', { name: /initial contract/i }));
    await userEvent.type(screen.getByLabelText(/sow start date/i), '2024-12-31');
    await userEvent.type(screen.getByLabelText(/sow end date/i), '2024-04-01');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));
    await waitFor(() => {
      expect(screen.getByLabelText(/sow end date/i)).toHaveAttribute('aria-invalid', 'true');
    });

    await userEvent.clear(screen.getByLabelText(/sow end date/i));
    await userEvent.type(screen.getByLabelText(/sow end date/i), '2025-06-30');
    await userEvent.click(screen.getByRole('button', { name: /save/i }));

    await waitFor(() => {
      expect(screen.getByLabelText(/sow end date/i)).not.toHaveAttribute('aria-invalid');
    });
  });

  // ---------------------------------------------------------------- T037a-equivalent / FR-050

  it('renders NO audit-reason input — reasons are system-generated (FR-050)', () => {
    render(<SowForm mode="create" onSubmit={vi.fn()} onCancel={vi.fn()} />);

    expect(screen.queryByLabelText(/reason/i)).not.toBeInTheDocument();
  });

  // ---------------------------------------------------------------- modal shell (mockup screen 6)

  it('renders Cancel and Save actions, and Cancel invokes onCancel', async () => {
    const onCancel = vi.fn();
    render(<SowForm mode="create" onSubmit={vi.fn()} onCancel={onCancel} />);

    await userEvent.click(screen.getByRole('button', { name: /cancel/i }));

    expect(onCancel).toHaveBeenCalled();
  });
});
