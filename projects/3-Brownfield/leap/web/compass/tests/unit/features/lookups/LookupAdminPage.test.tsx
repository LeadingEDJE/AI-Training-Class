import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { tableLinkClass } from '../../../../src/components/ui-classes';
import { stubFetchByUrl, type FetchByUrlStub } from '../../support/fetch-by-url';

const EMPLOYEE_TYPES = '/api/compass/v1/admin/employee-types';
const INVOICE_FREQUENCY_TYPES = '/api/compass/v1/admin/invoice-frequency-types';

const { LookupAdminPage } = await import('../../../../src/features/lookups/LookupAdminPage');

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>
      <LookupAdminPage />
    </QueryClientProvider>,
  );
}

/** The two collections, populated, as the page's initial state. */
function stubBothLookups(): FetchByUrlStub {
  return stubFetchByUrl({
    [`GET ${EMPLOYEE_TYPES}`]: {
      status: 200,
      body: [
        { id: 1, typeName: 'Full Time', isActive: true },
        { id: 2, typeName: 'Contract', isActive: false },
      ],
    },
    [`GET ${INVOICE_FREQUENCY_TYPES}`]: {
      status: 200,
      body: [{ id: 5, typeName: 'Monthly', isActive: true }],
    },
  });
}

/** The section region for one lookup, so assertions cannot drift across sections. */
function section(name: RegExp) {
  return within(screen.getByRole('region', { name }));
}

describe('LookupAdminPage', () => {
  // Rows are addressed as `getByRole('cell', { name })` rather than `getByText(name)`. Since the Active
  // column became a switch, a value's name appears TWICE in its row — once in the Type cell and once as
  // the switch's visually-hidden label, which is what makes twenty otherwise identical switches
  // distinguishable. A bare text query is therefore ambiguous, and the cell is what these assertions were
  // always reaching for.

  beforeEach(() => {
    stubBothLookups();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('names the page as the design source names it', async () => {
    // `Lookup Administration`, from mockup screen `#s-lookups` (feature 008 FR-011). It shipped as
    // "Lookups", which is the ADMIN SUB-NAV's label — that stays as it is; only the page's own h1
    // takes the design source's wording.
    renderPage();

    expect(
      await screen.findByRole('heading', { level: 1, name: 'Lookup Administration' }),
    ).toBeInTheDocument();
  });

  it('names each lookup section in Title Case, as every other heading is', async () => {
    // Owner request 2026-08-18. These two shipped as "Employee types" / "Invoice frequency types" —
    // sentence case, one click from "Lookup Administration". Asserted by EXACT name rather than the
    // case-insensitive regex the rest of this file uses to locate sections, because a /i match is
    // precisely what let the two casings coexist.
    renderPage();

    expect(
      await screen.findByRole('heading', { level: 2, name: 'Employee Types' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('heading', { level: 2, name: 'Invoice Frequency Types' }),
    ).toBeInTheDocument();
  });

  it('labels both add controls exactly as the design source does', async () => {
    // `+ Add Type`, from mockup screen `#s-lookups`, which uses that same label in BOTH cards (owner
    // request 2026-08-21, superseding the Title Case `+ Add {singular}` of 2026-08-18). The `+` itself is
    // guarded across every screen by `add-button-plus.test.ts`; this pins the wording.
    //
    // So the two are no longer distinguishable by name, and each is located THROUGH ITS REGION — which
    // is also how a screen-reader user tells them apart, the labelled region being the context the
    // mockup's own unlabelled cards do not provide. The count is asserted because a name shared by two
    // controls is exactly the shape where a scoped query silently starts matching the wrong one.
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    expect(screen.getAllByRole('button', { name: '+ Add Type' })).toHaveLength(2);
    expect(
      section(/employee types/i).getByRole('button', { name: '+ Add Type' }),
    ).toBeInTheDocument();
    expect(
      section(/invoice frequency types/i).getByRole('button', { name: '+ Add Type' }),
    ).toBeInTheDocument();
  });

  it('lists both lookups, each in its own labelled region', async () => {
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );
    expect(section(/employee types/i).getByRole('cell', { name: 'Contract' })).toBeInTheDocument();
    expect(
      section(/invoice frequency types/i).getByRole('cell', { name: 'Monthly' }),
    ).toBeInTheDocument();
  });

  it("states each row's own offered/retired state, without a section footnote (issue #408)", async () => {
    // Until 2026-08-26 the section carried a footnote — "Only active types appear when {usedFor}.
    // Retiring one leaves records already using it unchanged." — attached to the region as its
    // description. Owner request #408 removed it as an explanatory note nobody needed on screen; the
    // row-level state below is untouched and is now the only text describing a value's own state.
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    const employeeTypes = section(/employee types/i);
    expect(employeeTypes.getByText('Full Time is offered on new records.')).toBeInTheDocument();
    expect(
      employeeTypes.getByText('Contract is retired and no longer offered.'),
    ).toBeInTheDocument();

    expect(screen.queryByText(/only active types appear when/i)).not.toBeInTheDocument();
    expect(
      screen.queryByText(/retiring one leaves records already using it unchanged/i),
    ).not.toBeInTheDocument();
  });

  it('carries no aria-describedby on either region, now that neither has a footnote (issue #408)', async () => {
    // The footnote used to be what `aria-describedby` pointed at (`Card`'s `description` slot is
    // unused here). Removing it should leave the attribute absent rather than pointing at nothing.
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    expect(screen.getByRole('region', { name: 'Employee Types' })).not.toHaveAttribute(
      'aria-describedby',
    );
    expect(screen.getByRole('region', { name: 'Invoice Frequency Types' })).not.toHaveAttribute(
      'aria-describedby',
    );
  });

  it('renders no doubled article anywhere on the screen', async () => {
    // A general guard rather than one assertion about one sentence: any future description built by
    // concatenation gets caught here, which is the class of defect review found.
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    expect(document.body.textContent).not.toMatch(/\b(a an|a a|an a|an an)\b/);
  });

  it("reports each value's state in WORDS, with the word off-screen", async () => {
    // The Active column is the mockup's bare `.tswitch` — no word beside it (owner request 2026-08-21).
    //
    // What that changes and what it does not. For a screen-reader user, nothing: "Active"/"Retired" is
    // still in the switch's accessible name, asserted here, and `aria-checked` still carries the state.
    // For a sighted viewer the remaining signals are the knob's POSITION and the track's colour —
    // position is not colour, so AC-NFR-5's "not by colour alone" still holds, but the redundant word
    // that made it obvious is gone by request.
    //
    // The visible-absence half is asserted through the CLASS, not through `getByText`: `sr-only` clips
    // to 1x1 rather than setting `display:none`, so the element is still in the DOM and every
    // text query still finds it. A test written the obvious way would pass whether the word were shown
    // or hidden, which is the fail-open shape this repository keeps getting bitten by.
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );
    const employeeTypes = section(/employee types/i);

    expect(employeeTypes.getByRole('switch', { name: /full time.*active/i })).toBeChecked();
    expect(employeeTypes.getByRole('switch', { name: /contract.*retired/i })).not.toBeChecked();

    // Scoped to each switch, not to the section: `getAllByText(/^active$/i)` also matches the ACTIVE
    // column HEADER, which is uppercased by CSS rather than by content and is not going anywhere.
    for (const toggle of employeeTypes.getAllByRole('switch')) {
      expect(within(toggle).getByText(/^(active|retired)$/i)).toHaveClass('sr-only');
    }
  });

  it('renders the Active column as a switch that names its own value', async () => {
    // Twenty bare switches in a column are indistinguishable to a screen-reader user, who hears
    // "switch, on" once per row. The accessible name carries the lookup value; the visible text is the
    // state word, which is what WCAG 2.5.3 requires the name to contain.
    renderPage();

    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );
    const employeeTypes = section(/employee types/i);

    expect(employeeTypes.getByRole('switch', { name: /full time.*active/i })).toBeChecked();
    expect(employeeTypes.getByRole('switch', { name: /contract.*retired/i })).not.toBeChecked();
  });

  it('adds a value and shows it in the list', async () => {
    const stub = stubBothLookups();
    stub.setResponse(`POST ${EMPLOYEE_TYPES}`, {
      status: 201,
      body: { id: 9, typeName: 'Consultant', isActive: true },
    });
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    const employeeTypes = section(/employee types/i);
    // The mockup puts Add in the card heading, so the field does not exist until it is pressed.
    await userEvent.click(employeeTypes.getByRole('button', { name: '+ Add Type' }));
    await userEvent.type(employeeTypes.getByLabelText(/new employee type name/i), 'Consultant');

    stub.setResponse(`GET ${EMPLOYEE_TYPES}`, {
      status: 200,
      body: [
        { id: 1, typeName: 'Full Time', isActive: true },
        { id: 2, typeName: 'Contract', isActive: false },
        { id: 9, typeName: 'Consultant', isActive: true },
      ],
    });
    await userEvent.click(employeeTypes.getByRole('button', { name: /^save new employee type$/i }));

    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Consultant' })).toBeInTheDocument(),
    );
    expect(stub.bodiesFor(`POST ${EMPLOYEE_TYPES}`)).toContainEqual({ typeName: 'Consultant' });
  });

  it('surfaces a duplicate-name rejection inline and keeps the list intact', async () => {
    // FR-004 through the UI: the 409's message is shown where the user typed, not swallowed.
    const stub = stubBothLookups();
    stub.setResponse(`POST ${EMPLOYEE_TYPES}`, {
      status: 409,
      body: { message: "A value named 'Full Time' already exists." },
    });
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    const employeeTypes = section(/employee types/i);
    await userEvent.click(employeeTypes.getByRole('button', { name: '+ Add Type' }));
    await userEvent.type(employeeTypes.getByLabelText(/new employee type name/i), 'Full Time');
    await userEvent.click(employeeTypes.getByRole('button', { name: /^save new employee type$/i }));

    await waitFor(() =>
      expect(employeeTypes.getByRole('alert')).toHaveTextContent(/already exists/i),
    );
    expect(screen.getByRole('cell', { name: 'Contract' })).toBeInTheDocument();
  });

  it('surfaces a blank-name rejection inline', async () => {
    const stub = stubBothLookups();
    stub.setResponse(`POST ${EMPLOYEE_TYPES}`, {
      status: 400,
      body: { message: 'A name is required.' },
    });
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    const employeeTypes = section(/employee types/i);
    await userEvent.click(employeeTypes.getByRole('button', { name: '+ Add Type' }));
    await userEvent.type(employeeTypes.getByLabelText(/new employee type name/i), 'x');
    await userEvent.click(employeeTypes.getByRole('button', { name: /^save new employee type$/i }));

    await waitFor(() => expect(employeeTypes.getByRole('alert')).toHaveTextContent(/required/i));
  });

  it('renames a value', async () => {
    const stub = stubBothLookups();
    stub.setResponse(`PUT ${EMPLOYEE_TYPES}`, {
      status: 200,
      body: { id: 1, typeName: 'Salaried', isActive: true },
    });
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    const employeeTypes = section(/employee types/i);
    await userEvent.click(employeeTypes.getByRole('button', { name: /edit full time/i }));
    const nameField = employeeTypes.getByLabelText(/name for full time/i);
    await userEvent.clear(nameField);
    await userEvent.type(nameField, 'Salaried');
    await userEvent.click(employeeTypes.getByRole('button', { name: /save full time/i }));

    await waitFor(() =>
      expect(stub.bodiesFor(`PUT ${EMPLOYEE_TYPES}/1`)).toContainEqual({
        typeName: 'Salaried',
        isActive: true,
      }),
    );
  });

  it('retires a value without renaming it', async () => {
    // Quickstart 3.1 step 4. The PUT must carry the unchanged name — retiring is not a rename.
    const stub = stubBothLookups();
    stub.setResponse(`PUT ${EMPLOYEE_TYPES}`, {
      status: 200,
      body: { id: 1, typeName: 'Full Time', isActive: false },
    });
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    const employeeTypes = section(/employee types/i);
    await userEvent.click(employeeTypes.getByRole('switch', { name: /full time/i }));

    await waitFor(() =>
      expect(stub.bodiesFor(`PUT ${EMPLOYEE_TYPES}/1`)).toContainEqual({
        typeName: 'Full Time',
        isActive: false,
      }),
    );
  });

  it('reinstates a retired value', async () => {
    const stub = stubBothLookups();
    stub.setResponse(`PUT ${EMPLOYEE_TYPES}`, {
      status: 200,
      body: { id: 2, typeName: 'Contract', isActive: true },
    });
    renderPage();
    await waitFor(() => expect(screen.getByRole('cell', { name: 'Contract' })).toBeInTheDocument());

    const employeeTypes = section(/employee types/i);
    await userEvent.click(employeeTypes.getByRole('switch', { name: /contract/i }));

    await waitFor(() =>
      expect(stub.bodiesFor(`PUT ${EMPLOYEE_TYPES}/2`)).toContainEqual({
        typeName: 'Contract',
        isActive: true,
      }),
    );
  });

  it('adds an invoice frequency type through its own section', async () => {
    // The two sections must not be wired to the same endpoint.
    const stub = stubBothLookups();
    stub.setResponse(`POST ${INVOICE_FREQUENCY_TYPES}`, {
      status: 201,
      body: { id: 7, typeName: 'Fortnightly', isActive: true },
    });
    renderPage();
    await waitFor(() => expect(screen.getByRole('cell', { name: 'Monthly' })).toBeInTheDocument());

    const frequencies = section(/invoice frequency types/i);
    await userEvent.click(frequencies.getByRole('button', { name: '+ Add Type' }));
    await userEvent.type(
      frequencies.getByLabelText(/new invoice frequency type name/i),
      'Fortnightly',
    );
    await userEvent.click(
      frequencies.getByRole('button', { name: /^save new invoice frequency type$/i }),
    );

    await waitFor(() =>
      expect(stub.bodiesFor(`POST ${INVOICE_FREQUENCY_TYPES}`)).toContainEqual({
        typeName: 'Fortnightly',
      }),
    );
    expect(stub.bodiesFor(`POST ${EMPLOYEE_TYPES}`)).toEqual([]);
  });

  it('reports a failed load rather than rendering an empty list as if it were the truth', async () => {
    stubFetchByUrl({
      [`GET ${EMPLOYEE_TYPES}`]: { status: 500 },
      [`GET ${INVOICE_FREQUENCY_TYPES}`]: { status: 200, body: [] },
    });
    renderPage();

    await waitFor(() =>
      expect(section(/employee types/i).getByRole('alert')).toHaveTextContent(
        /could not be loaded/i,
      ),
    );
  });

  it('reports a refusal when the caller lacks the Compass root role', async () => {
    // The nav hides this screen from lesser roles, but a deep link does not. 403 must read as a
    // refusal, not as an empty lookup list.
    stubFetchByUrl({
      [`GET ${EMPLOYEE_TYPES}`]: { status: 403 },
      [`GET ${INVOICE_FREQUENCY_TYPES}`]: { status: 403 },
    });
    renderPage();

    await waitFor(() =>
      expect(section(/employee types/i).getByRole('alert')).toHaveTextContent(
        /permission|not allowed/i,
      ),
    );
  });

  it('keeps the editor open when a rename is rejected, so the typed name is not lost', async () => {
    // Renaming onto another value's name is a 409. Closing the editor would discard what the
    // administrator typed and leave them re-reading the list to work out what happened.
    const stub = stubBothLookups();
    stub.setResponse(`PUT ${EMPLOYEE_TYPES}`, {
      status: 409,
      body: { message: "A value named 'Contract' already exists." },
    });
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    const employeeTypes = section(/employee types/i);
    await userEvent.click(employeeTypes.getByRole('button', { name: /edit full time/i }));
    const nameField = employeeTypes.getByLabelText(/name for full time/i);
    await userEvent.clear(nameField);
    await userEvent.type(nameField, 'Contract');
    await userEvent.click(employeeTypes.getByRole('button', { name: /save full time/i }));

    await waitFor(() =>
      expect(employeeTypes.getByRole('alert')).toHaveTextContent(/already exists/i),
    );
    expect(employeeTypes.getByLabelText(/name for full time/i)).toHaveValue('Contract');
  });

  it('abandons an edit on cancel, leaving the value as it was', async () => {
    const stub = stubBothLookups();
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    const employeeTypes = section(/employee types/i);
    await userEvent.click(employeeTypes.getByRole('button', { name: /edit full time/i }));
    const nameField = employeeTypes.getByLabelText(/name for full time/i);
    await userEvent.clear(nameField);
    await userEvent.type(nameField, 'Abandoned');
    await userEvent.click(employeeTypes.getByRole('button', { name: /cancel editing full time/i }));

    expect(employeeTypes.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument();
    expect(stub.calls().filter((call) => call.startsWith('PUT'))).toEqual([]);
  });

  it('reopening an abandoned edit shows the stored name, not the discarded draft', async () => {
    stubBothLookups();
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    const employeeTypes = section(/employee types/i);
    await userEvent.click(employeeTypes.getByRole('button', { name: /edit full time/i }));
    await userEvent.type(employeeTypes.getByLabelText(/name for full time/i), 'Discarded');
    await userEvent.click(employeeTypes.getByRole('button', { name: /cancel editing full time/i }));
    await userEvent.click(employeeTypes.getByRole('button', { name: /edit full time/i }));

    expect(employeeTypes.getByLabelText(/name for full time/i)).toHaveValue('Full Time');
  });

  it('reports that it is loading before the values arrive', async () => {
    stubFetchByUrl({
      [`GET ${EMPLOYEE_TYPES}`]: { status: 200, body: [] },
      [`GET ${INVOICE_FREQUENCY_TYPES}`]: { status: 200, body: [] },
    });
    renderPage();

    expect(section(/employee types/i).getByRole('status')).toHaveTextContent(/loading/i);
  });

  it('says so plainly when a lookup has no values yet', async () => {
    stubFetchByUrl({
      [`GET ${EMPLOYEE_TYPES}`]: { status: 200, body: [] },
      [`GET ${INVOICE_FREQUENCY_TYPES}`]: { status: 200, body: [] },
    });
    renderPage();

    await waitFor(() =>
      expect(section(/employee types/i).getByText(/no values yet/i)).toBeInTheDocument(),
    );
  });
  it('offers no add field until Add is pressed, and hides it again on cancel', async () => {
    // The mockup puts Add in the card heading, which means the field is revealed rather than permanent.
    // A row that cannot be dismissed would leave a half-finished entry sitting in the table.
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    const employeeTypes = section(/employee types/i);
    expect(employeeTypes.queryByLabelText(/new employee type name/i)).not.toBeInTheDocument();

    await userEvent.click(employeeTypes.getByRole('button', { name: '+ Add Type' }));
    expect(employeeTypes.getByLabelText(/new employee type name/i)).toBeInTheDocument();

    await userEvent.click(
      employeeTypes.getByRole('button', { name: /^cancel adding an employee type$/i }),
    );
    expect(employeeTypes.queryByLabelText(/new employee type name/i)).not.toBeInTheDocument();
  });

  it('shows short button text while naming the value it acts on', async () => {
    // Review of PR #326: "No test asserts visible button text (all match by accessible name), so
    // neither change would be caught by the suite." Correct — nothing pinned the split, so shortening
    // an accessible name to match its visible word, or lengthening the visible text back out, was free.
    //
    // Both halves are asserted per control: the ACCESSIBLE NAME carries the value (a screen-reader user
    // hears which one), and the VISIBLE text is the mockup's short word. `textContent` is not usable
    // here — `ActionLabel` renders both spans, so it reads "SaveSave New Employee Type"; the visible
    // half is the `aria-hidden` span specifically.
    const visibleTextOf = (control: HTMLElement) =>
      control.querySelector('[aria-hidden="true"]')?.textContent;

    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );
    const employeeTypes = section(/employee types/i);

    const edit = employeeTypes.getByRole('button', { name: 'Edit Full Time' });
    expect(visibleTextOf(edit)).toBe('Edit');
    // Owner request 2026-08-21: it shipped as `brand-blue-accent`. `rounded` stays for the focus ring.
    expect(edit.className).toBe(`${tableLinkClass} rounded`);

    await userEvent.click(employeeTypes.getByRole('button', { name: '+ Add Type' }));
    expect(
      visibleTextOf(employeeTypes.getByRole('button', { name: 'Save New Employee Type' })),
    ).toBe('Save');
    expect(
      visibleTextOf(employeeTypes.getByRole('button', { name: 'Cancel Adding an Employee Type' })),
    ).toBe('Cancel');

    // WCAG 2.5.3: the visible word must be a leading, contiguous part of the accessible name, or a
    // voice-control user saying "click Save" matches nothing.
    for (const control of employeeTypes.getAllByRole('button')) {
      const visible = visibleTextOf(control as HTMLElement);
      if (visible !== undefined && visible !== null) {
        expect(control.getAttribute('aria-label') ?? control.textContent ?? '').toContain(visible);
      }
    }
  });

  it('keeps both add forms distinguishable when both are open at once', async () => {
    // The ambiguity the PR #326 review thought did not exist. `isAdding` is state INSIDE
    // `LookupSection` and this page renders two of them, so nothing stops both forms being open
    // together — which puts two "Save" and two "Cancel" buttons on screen with identical visible text.
    // What tells them apart is the accessible name, so that is what this pins.
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    await userEvent.click(section(/employee types/i).getByRole('button', { name: '+ Add Type' }));
    await userEvent.click(
      section(/invoice frequency types/i).getByRole('button', { name: '+ Add Type' }),
    );

    // Both open — neither reveal closed the other.
    expect(screen.getAllByRole('button', { name: /^save new/i })).toHaveLength(2);

    // And each of the four is reachable by a name no other control shares.
    for (const name of [
      'Save New Employee Type',
      'Cancel Adding an Employee Type',
      'Save New Invoice Frequency Type',
      'Cancel Adding an Invoice Frequency Type',
    ]) {
      expect(screen.getAllByRole('button', { name })).toHaveLength(1);
    }
  });

  it('keeps the add row open when the save is rejected, so the typed name is not lost', async () => {
    // The same rule the rename path already follows. Closing the row on rejection would discard what was
    // typed at the exact moment the user needs to correct it.
    const stub = stubBothLookups();
    stub.setResponse(`POST ${EMPLOYEE_TYPES}`, {
      status: 409,
      body: { message: "A value named 'Full Time' already exists." },
    });
    renderPage();
    await waitFor(() =>
      expect(screen.getByRole('cell', { name: 'Full Time' })).toBeInTheDocument(),
    );

    const employeeTypes = section(/employee types/i);
    await userEvent.click(employeeTypes.getByRole('button', { name: '+ Add Type' }));
    await userEvent.type(employeeTypes.getByLabelText(/new employee type name/i), 'Full Time');
    await userEvent.click(employeeTypes.getByRole('button', { name: /^save new employee type$/i }));

    await waitFor(() =>
      expect(employeeTypes.getByRole('alert')).toHaveTextContent(/already exists/i),
    );
    expect(employeeTypes.getByLabelText(/new employee type name/i)).toHaveValue('Full Time');
  });
});
