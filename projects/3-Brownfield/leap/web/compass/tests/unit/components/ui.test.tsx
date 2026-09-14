import { StrictMode, useState } from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import {
  Alert,
  Button,
  Card,
  FormField,
  FormGrid,
  Modal,
  PageHeader,
  Panel,
  ScrollableRegion,
  StackedRows,
  StatusPill,
  Table,
  Toggle,
  type TableSortableColumn,
} from '../../../src/components/ui';
import { buttonClassName, tableLinkClass } from '../../../src/components/ui-classes';

/**
 * The shared presentational primitives the configuration screens are built from.
 *
 * They exist because US2 adds the third and fourth screens to this SPA, which is the point at which the
 * Rule of Three stops arguing against extracting them — and because the layout they express comes from
 * `docs/design/edje-compass-mockups.html` while the colour comes from
 * `docs/design/leading-edje-style-guide.html` and the ratios come from measurement. Encoding that in one
 * place per element is what keeps the next screen from re-deciding it.
 *
 * These tests assert the behaviour and the accessible structure. The COLOUR is
 * `brand-contrast.test.ts`'s (arithmetic) and the real axe-core run in
 * `web/timesheet/tests/e2e/compass-accessibility.critical.spec.ts`'s (compiled Tailwind, which jsdom
 * cannot see).
 */
describe('PageHeader', () => {
  it('names the page as a heading, so it is reachable by role', () => {
    render(<PageHeader title="EDJEr Configuration" />);

    expect(
      screen.getByRole('heading', { name: 'EDJEr Configuration', level: 1 }),
    ).toBeInTheDocument();
  });

  it('renders the status beside the title when one is given', () => {
    render(
      <PageHeader
        title="EDJEr Configuration"
        status={<StatusPill tone="affirmative" label="Active" />}
      />,
    );

    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it('renders actions, and none when there are none', () => {
    const { rerender } = render(
      <PageHeader title="EDJErs" actions={<Button>Add New EDJEr</Button>} />,
    );
    expect(screen.getByRole('button', { name: 'Add New EDJEr' })).toBeInTheDocument();

    rerender(<PageHeader title="EDJErs" />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('carries a breadcrumb trail when given one', () => {
    render(
      <PageHeader title="EDJEr Configuration" breadcrumb={<a href="/admin/edjers">EDJErs</a>} />,
    );

    // A labelled navigation landmark rather than a bare row of links: the mockups' `.crumb` is a plain
    // div, which gives a screen-reader user no way to skip or identify it.
    const trail = screen.getByRole('navigation', { name: /breadcrumb/i });
    expect(within(trail).getByRole('link', { name: 'EDJErs' })).toBeInTheDocument();
  });
});

describe('Card', () => {
  it('is a region named by its own heading', () => {
    render(<Card title="Profile">fields</Card>);

    // aria-labelledby onto the rendered heading, so `getByRole('region', { name })` works — the locator
    // every other Compass test and Playwright spec already uses.
    const region = screen.getByRole('region', { name: 'Profile' });
    expect(within(region).getByRole('heading', { name: 'Profile' })).toBeInTheDocument();
  });

  it('renders its children inside the region', () => {
    render(<Card title="Time Tracking Settings">the toggles</Card>);

    expect(
      within(screen.getByRole('region', { name: 'Time Tracking Settings' })).getByText(
        'the toggles',
      ),
    ).toBeInTheDocument();
  });

  it('renders a heading-level action when given one', () => {
    render(
      <Card title="Employee Types" action={<Button size="small">Add Type</Button>}>
        rows
      </Card>,
    );

    expect(screen.getByRole('button', { name: 'Add Type' })).toBeInTheDocument();
  });

  it('describes the card when given a description, and links it to the region', () => {
    render(
      <Card title="Profile" description="Everything the rest of Compass selects from.">
        fields
      </Card>,
    );

    const region = screen.getByRole('region', { name: 'Profile' });
    expect(region).toHaveAccessibleDescription('Everything the rest of Compass selects from.');
  });
});

describe('Panel', () => {
  it('renders the white card surface the design source puts screen content on', () => {
    // Five screens shipped with their content directly on the #f4f5f6 shell (owner request
    // 2026-08-18). Asserted on the CLASS RUN rather than a computed colour because Tailwind v4
    // compiles @theme at build time and jsdom never resolves these utilities.
    const { container } = render(<Panel>content</Panel>);
    const panel = container.firstElementChild;

    expect(panel).not.toBeNull();
    expect(panel?.className).toContain('bg-white');
    expect(panel?.className).toContain('border-brand-taupe/40');
    expect(panel?.className).toContain('rounded-lg');
  });

  it('adds NOTHING to the accessibility tree', () => {
    // The deliberate exception to this module's own by-role rule. A `region` named the same as the
    // page's h1 is noise in a landmark list, so the sheet is presentation and the content inside it
    // brings its own structure. If this ever became a landmark, every screen using it would gain a
    // duplicate one at once.
    render(
      <Panel>
        <h2>Assignment History</h2>
      </Panel>,
    );

    expect(screen.queryByRole('region')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Assignment History' })).toBeInTheDocument();
  });

  it('is the same surface Card paints, so the two sheets cannot drift apart', () => {
    const { container: panel } = render(<Panel>a</Panel>);
    const { container: card } = render(<Card title="Titled">b</Card>);

    for (const surfaceClass of ['rounded-lg', 'border-brand-taupe/40', 'bg-white', 'p-5']) {
      expect(panel.firstElementChild?.className, surfaceClass).toContain(surfaceClass);
      expect(card.firstElementChild?.className, surfaceClass).toContain(surfaceClass);
    }
  });
});

describe('Button', () => {
  it('is a real button with an accessible name', () => {
    render(<Button>Save EDJEr</Button>);

    expect(screen.getByRole('button', { name: 'Save EDJEr' })).toBeInTheDocument();
  });

  it('defaults to type="button", so it cannot submit a form by accident', () => {
    // The HTML default is "submit". A Cancel button inside a form that quietly submits it is the bug this
    // default prevents; a real submit must ask for it.
    render(<Button>Cancel</Button>);

    expect(screen.getByRole('button', { name: 'Cancel' })).toHaveAttribute('type', 'button');
  });

  it('can be a submit button when asked', () => {
    render(<Button type="submit">Save EDJEr</Button>);

    expect(screen.getByRole('button', { name: 'Save EDJEr' })).toHaveAttribute('type', 'submit');
  });

  it('calls its handler when clicked', async () => {
    const onClick = vi.fn();
    render(<Button onClick={onClick}>Add</Button>);

    await userEvent.click(screen.getByRole('button', { name: 'Add' }));

    expect(onClick).toHaveBeenCalledOnce();
  });

  it('does not call its handler when disabled', async () => {
    const onClick = vi.fn();
    render(
      <Button onClick={onClick} disabled>
        Add
      </Button>,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Add' }));

    expect(onClick).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled();
  });

  it('renders each variant as the same accessible control', () => {
    // Variant is a visual distinction only. A secondary button is not a lesser control, and nothing about
    // it may change how it is reached or announced.
    render(
      <>
        <Button variant="primary">Save</Button>
        <Button variant="secondary">Cancel</Button>
      </>,
    );

    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeEnabled();
  });
});

describe('StatusPill', () => {
  it('states the status as a WORD, never as colour alone', () => {
    // AC-NFR-5. The mockups convey status by pill colour; a colour-only signal fails for a colour-blind
    // user and disappears entirely in a screen reader.
    render(<StatusPill tone="affirmative" label="Active" />);

    expect(screen.getByText('Active')).toBeInTheDocument();
  });

  it.each(['affirmative', 'neutral'] as const)(
    'renders the %s tone with its label intact',
    (tone) => {
      render(<StatusPill tone={tone} label="Retired" />);

      expect(screen.getByText('Retired')).toBeInTheDocument();
    },
  );

  it('gives the affirmative tone green text on the green tint, not brand gray', () => {
    // Owner report 2026-08-18: the count pill read as a grey chip on a green field. The mockups' two
    // pills are two colourways — green-on-green and grey-on-grey — not one tint with a shared label
    // colour. `--color-brand-green-accent` is their hue darkened to 5.80:1 on the tint; the ratio is
    // measured in `brand-contrast.test.ts`, and this asserts the pairing actually reaches the DOM.
    render(<StatusPill tone="affirmative" label="87 active EDJErs" />);

    const pill = screen.getByText('87 active EDJErs');

    expect(pill.className).toContain('bg-brand-green-tint');
    expect(pill.className).toContain('text-brand-green-accent');
    expect(pill.className).not.toContain('text-brand-gray');
  });

  it('leaves the neutral tone grey on grey', () => {
    // The neutral pill IS grey in the mockups, so the fix above must not have swept it along.
    render(<StatusPill tone="neutral" label="Former" />);

    const pill = screen.getByText('Former');

    expect(pill.className).toContain('bg-brand-gray-tint');
    expect(pill.className).toContain('text-brand-gray');
    expect(pill.className).not.toContain('text-brand-green-accent');
  });
});

describe('Card description and footnote', () => {
  it('describes the region by BOTH slots when both are present, in reading order', async () => {
    // The branch nothing else reaches. Every call site passes one slot or the other — `LookupSection`
    // only `footnote`, the assignment histories only `description` — so each ternary in the id list is
    // exercised in both directions across separate tests and BRANCH COVERAGE REPORTS 100% while the
    // two-id string is never once produced. That is the shape this repo's test docs call fail-open: a
    // regression emitting one id, or joining with a comma, would ship green.
    render(
      <Card title="Employee Types" description="Introduces the section." footnote="Qualifies it.">
        <p>Body</p>
      </Card>,
    );

    const region = screen.getByRole('region', { name: 'Employee Types' });
    const ids = (region.getAttribute('aria-describedby') ?? '').split(' ').filter(Boolean);

    expect(ids).toHaveLength(2);
    // Order is the reading order, not the declaration order of the props — description introduces the
    // content and the footnote qualifies it, so a reader must hear them that way round.
    expect(document.getElementById(ids[0])).toHaveTextContent('Introduces the section.');
    expect(document.getElementById(ids[1])).toHaveTextContent('Qualifies it.');
  });

  it('names no description at all when neither slot is given', () => {
    // `aria-describedby=""` is not the same as absent: an empty IDREF list is a broken reference.
    render(
      <Card title="Employee Types">
        <p>Body</p>
      </Card>,
    );

    expect(screen.getByRole('region', { name: 'Employee Types' })).not.toHaveAttribute(
      'aria-describedby',
    );
  });

  it('renders the footnote after the content, not before it', () => {
    // The placement IS the requirement — as a description above the table it pushed two side-by-side
    // cards' tables to different heights, which is why the slot exists at all.
    render(
      <Card title="Employee Types" footnote="Only active types appear.">
        <p>Body</p>
      </Card>,
    );

    const body = screen.getByText('Body');
    const note = screen.getByText('Only active types appear.');

    // DOCUMENT_POSITION_FOLLOWING: the note comes after the body in document order.
    expect(body.compareDocumentPosition(note) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('keeps the heading and its action on one line, letting the heading text give way', () => {
    // The classes are asserted because the failure they prevent is invisible to jsdom, which has no
    // layout engine: with `flex-wrap` the action was pushed to a second row, and without `break-words`
    // a heading shrunk past its longest word overflows its box and lands UNDER the action. Neither a
    // screenshot at a tested viewport nor an axe scan catches that, so the contract is pinned here and
    // the reasoning lives on the component.
    render(
      <Card title="Invoice Frequency Types" action={<button type="button">+ Add Type</button>}>
        <p>Body</p>
      </Card>,
    );

    const heading = screen.getByRole('heading', { name: 'Invoice Frequency Types' });
    const row = heading.parentElement;

    expect(row?.className).not.toContain('flex-wrap');
    expect(heading.className).toContain('flex-1');
    expect(heading.className).toContain('min-w-0');
    expect(heading.className).toContain('break-words');
    expect(screen.getByRole('button', { name: '+ Add Type' }).parentElement?.className).toContain(
      'flex-none',
    );
  });
});

describe('Toggle', () => {
  it('is a real switch, so its state is announced', () => {
    // The mockups draw a `<div class="tswitch">`, which is unreachable by keyboard and silent to assistive
    // technology. A switch role with aria-checked is the same picture and an actual control.
    render(<Toggle label="Timesheet Required" checked onChange={vi.fn()} />);

    const toggle = screen.getByRole('switch', { name: /timesheet required/i });
    expect(toggle).toBeChecked();
  });

  it('reports the unchecked state as unchecked rather than as absent', () => {
    render(<Toggle label="Can Submit Under 40 Hours" checked={false} onChange={vi.fn()} />);

    expect(screen.getByRole('switch', { name: /can submit under 40 hours/i })).not.toBeChecked();
  });

  it('toggles on click, reporting the NEW value', async () => {
    const onChange = vi.fn();
    render(<Toggle label="Include in Payroll" checked={false} onChange={onChange} />);

    await userEvent.click(screen.getByRole('switch', { name: /include in payroll/i }));

    expect(onChange).toHaveBeenCalledWith(true);
  });

  it('toggles from the keyboard', async () => {
    // A div cannot do this, which is the whole reason it is a button.
    const onChange = vi.fn();
    render(<Toggle label="Include in Payroll" checked onChange={onChange} />);

    await userEvent.tab();
    expect(screen.getByRole('switch', { name: /include in payroll/i })).toHaveFocus();

    await userEvent.keyboard(' ');
    expect(onChange).toHaveBeenCalledWith(false);
  });

  it('states the current state in text as well as by position', async () => {
    // Belt and braces for AC-NFR-5: the knob's position is the visual signal, and the word next to it is
    // the one that survives greyscale and a screen reader.
    render(<Toggle label="Timesheet Required" checked onChange={vi.fn()} />);

    const toggle = screen.getByRole('switch', { name: /timesheet required/i });
    expect(toggle).toHaveAccessibleName(/on|yes|required/i);
  });

  it('can hide the state word from sight while keeping it in the accessible name', async () => {
    // `stateLabelHidden`, for the lookup screen's bare `.tswitch` (owner request 2026-08-21). It hides,
    // it does not drop: the name and aria-checked are untouched, so the ONLY thing a screen-reader user
    // loses is nothing at all.
    render(
      <Toggle
        label="Full Time"
        checked
        onChange={vi.fn()}
        stateLabels={{ on: 'Active', off: 'Retired' }}
        stateLabelHidden
      />,
    );

    const toggle = screen.getByRole('switch', { name: /full time/i });
    expect(toggle).toHaveAccessibleName('Full Time Active');
    expect(toggle).toBeChecked();
    // `sr-only` clips rather than removing, so the word is still queryable — the class is what says
    // whether it is on screen, and asserting anything else here would pass either way.
    expect(screen.getByText('Active')).toHaveClass('sr-only');
  });

  it('shows the state word by default, so hiding it stays an explicit choice', async () => {
    // The counterpart to the test above. Without it, `stateLabelHidden` defaulting to `true` — or the
    // class run being applied unconditionally — would pass every remaining assertion in this describe.
    render(
      <Toggle
        label="Full Time"
        checked
        onChange={vi.fn()}
        stateLabels={{ on: 'Active', off: 'Retired' }}
      />,
    );

    expect(screen.getByText('Active')).not.toHaveClass('sr-only');
  });

  it('does not fire when disabled', async () => {
    const onChange = vi.fn();
    render(<Toggle label="Include in Payroll" checked onChange={onChange} disabled />);

    await userEvent.click(screen.getByRole('switch', { name: /include in payroll/i }));

    expect(onChange).not.toHaveBeenCalled();
  });

  it('can hide its label visually while keeping it in the accessible name', () => {
    // For a switch in a table cell, the row already says which value it belongs to — repeating it beside
    // the track would read as a duplicate. Hiding it VISUALLY rather than dropping it keeps the control
    // self-describing: twenty bare toggles in a column are indistinguishable to a screen-reader user.
    render(<Toggle label="Full Time" checked onChange={vi.fn()} labelHidden />);

    const toggle = screen.getByRole('switch', { name: /full time/i });
    expect(toggle).toBeInTheDocument();

    // Asserted via the class, deliberately. jsdom computes nothing from a compiled Tailwind utility, so
    // `sr-only` is the only signal available here — and without this the test passes whether the label is
    // hidden or not, since an unknown prop is simply ignored. The real visual check is the axe-core run.
    const label = screen.getByText('Full Time');
    expect(label).toHaveClass('sr-only');
  });

  it('shows its label when labelHidden is not set', () => {
    // The positive control for the assertion above: it must be able to report the label as visible too, or
    // it is just checking that a class name exists somewhere.
    render(<Toggle label="Timesheet Required" checked onChange={vi.fn()} />);

    expect(screen.getByText('Timesheet Required')).not.toHaveClass('sr-only');
  });

  it('can state its state in domain words rather than On and Off', () => {
    // "Active"/"Retired" is what a lookup value's state is called everywhere else in Compass, and the
    // word is the part that survives greyscale and a screen reader (AC-NFR-5).
    const { rerender } = render(
      <Toggle
        label="Full Time"
        checked
        onChange={vi.fn()}
        labelHidden
        stateLabels={{ on: 'Active', off: 'Retired' }}
      />,
    );

    expect(screen.getByText('Active')).toBeInTheDocument();
    expect(screen.queryByText('On')).not.toBeInTheDocument();

    rerender(
      <Toggle
        label="Full Time"
        checked={false}
        onChange={vi.fn()}
        labelHidden
        stateLabels={{ on: 'Active', off: 'Retired' }}
      />,
    );

    expect(screen.getByText('Retired')).toBeInTheDocument();
  });

  it('keeps the accessible name containing the visible state word (WCAG 2.5.3)', () => {
    // The visible text in a hidden-label toggle is the state word, so the accessible name has to contain
    // it — otherwise a voice-control user saying what they can see cannot reach the control.
    render(
      <Toggle
        label="Full Time"
        checked
        onChange={vi.fn()}
        labelHidden
        stateLabels={{ on: 'Active', off: 'Retired' }}
      />,
    );

    expect(screen.getByRole('switch', { name: /full time.*active/i })).toBeInTheDocument();
  });

  it('still toggles when its label is hidden', () => {
    // The visual change must not cost it its behaviour.
    const onChange = vi.fn();
    render(<Toggle label="Full Time" checked onChange={onChange} labelHidden />);

    screen.getByRole('switch', { name: /full time/i }).click();

    expect(onChange).toHaveBeenCalledWith(false);
  });

  it('describes itself with its hint when given one', () => {
    render(
      <Toggle
        label="Status"
        checked
        onChange={vi.fn()}
        hint="Termination dates are managed in HiBob, not here."
      />,
    );

    expect(screen.getByRole('switch', { name: /status/i })).toHaveAccessibleDescription(/hibob/i);
  });
});

describe('FormField', () => {
  it('labels its control, so clicking the label focuses the input', async () => {
    render(<FormField label="First Name">{(id) => <input id={id} />}</FormField>);

    await userEvent.click(screen.getByText('First Name'));

    expect(screen.getByLabelText(/first name/i)).toHaveFocus();
  });

  it('marks a required field for assistive technology, not only with an asterisk', () => {
    // The mockups mark required with a red `*`. A visual glyph alone is not a signal, so the control also
    // carries aria-required.
    render(
      <FormField label="Email Address" required>
        {(id) => <input id={id} />}
      </FormField>,
    );

    expect(screen.getByLabelText(/email address/i)).toHaveAttribute('aria-required', 'true');
  });

  it('does not mark an optional field required — the coach is optional (AC-17)', () => {
    render(<FormField label="Coach">{(id) => <select id={id} />}</FormField>);

    expect(screen.getByLabelText(/coach/i)).not.toHaveAttribute('aria-required', 'true');
  });

  it('attaches a hint as the control description', () => {
    render(
      <FormField label="Employee Type" hint="Active types only.">
        {(id) => <select id={id} />}
      </FormField>,
    );

    expect(screen.getByLabelText(/employee type/i)).toHaveAccessibleDescription(
      'Active types only.',
    );
  });

  it('announces an error, marks the control invalid, and keeps the hint reachable', () => {
    render(
      <FormField label="Email Address" hint="Must be unique." error="That address is already used.">
        {(id) => <input id={id} />}
      </FormField>,
    );

    const input = screen.getByLabelText(/email address/i);
    expect(input).toHaveAttribute('aria-invalid', 'true');
    expect(input).toHaveAccessibleDescription(/must be unique.*already used/is);
    expect(screen.getByText('That address is already used.')).toBeInTheDocument();
  });

  it('is not invalid when there is no error', () => {
    render(<FormField label="First Name">{(id) => <input id={id} />}</FormField>);

    expect(screen.getByLabelText(/first name/i)).not.toHaveAttribute('aria-invalid', 'true');
  });

  it('gives each field a distinct id, so two fields never share a label', () => {
    // useId per instance. Two inputs with one id is the defect that makes a form's second field
    // unreachable by its own label, and it is invisible until a test clicks the label.
    render(
      <>
        <FormField label="First Name">{(id) => <input id={id} />}</FormField>
        <FormField label="Last Name">{(id) => <input id={id} />}</FormField>
      </>,
    );

    const first = screen.getByLabelText(/first name/i);
    const last = screen.getByLabelText(/last name/i);
    expect(first.id).not.toBe(last.id);
  });
});

describe('FormGrid', () => {
  it('renders its fields', () => {
    render(
      <FormGrid>
        <FormField label="First Name">{(id) => <input id={id} />}</FormField>
        <FormField label="Last Name">{(id) => <input id={id} />}</FormField>
      </FormGrid>,
    );

    expect(screen.getByLabelText(/first name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/last name/i)).toBeInTheDocument();
  });
});

describe('Table', () => {
  const COLUMNS = ['Last Name', 'First Name', 'Email'];

  it('is a table with a header row, so it is navigable as one', () => {
    render(
      <Table caption="EDJErs" columns={COLUMNS}>
        <tr>
          <td>Alvarez</td>
          <td>Maya</td>
          <td>maya@example.test</td>
        </tr>
      </Table>,
    );

    const table = screen.getByRole('table', { name: 'EDJErs' });
    for (const column of COLUMNS) {
      expect(within(table).getByRole('columnheader', { name: column })).toBeInTheDocument();
    }
    expect(within(table).getByRole('cell', { name: 'Alvarez' })).toBeInTheDocument();
  });

  it('always has an accessible name, because an unnamed table is unnavigable', () => {
    render(
      <Table caption="Employee types" columns={['Type']}>
        <tr>
          <td>Full Time</td>
        </tr>
      </Table>,
    );

    expect(screen.getByRole('table', { name: 'Employee types' })).toBeInTheDocument();
  });

  it('renders an empty message instead of a bare header when there are no rows', () => {
    // An empty table with headers reads as "loaded, nothing here" to a sighted user and as nothing at all
    // to a screen-reader user. The message is the content.
    render(<Table caption="EDJErs" columns={COLUMNS} emptyMessage="No EDJErs yet." />);

    expect(screen.getByText('No EDJErs yet.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('prefers rows over the empty message when it has both', () => {
    render(
      <Table caption="EDJErs" columns={COLUMNS} emptyMessage="No EDJErs yet.">
        <tr>
          <td>Alvarez</td>
          <td>Maya</td>
          <td>maya@example.test</td>
        </tr>
      </Table>,
    );

    expect(screen.queryByText('No EDJErs yet.')).not.toBeInTheDocument();
    expect(screen.getByRole('table', { name: 'EDJErs' })).toBeInTheDocument();
  });

  // The four shapes a caller actually produces. `emptyMessage` fires for all of the empty ones, and the
  // EMPTY ARRAY is the one that matters: `rows.map(...)` over a zero-length list yields `[]`, which is
  // not `undefined`, `null` or `false`, so a check written as a chain of `===` comparisons called it
  // non-empty and rendered a bare header row. Every call site in `src/features/` had independently
  // worked around that with `list.length > 0 ? ... : undefined`.
  it('renders the empty message when rows are computed from an EMPTY LIST', () => {
    const rows: { id: number; name: string }[] = [];

    render(
      <Table caption="EDJErs" columns={COLUMNS} emptyMessage="No EDJErs yet.">
        {rows.map((row) => (
          <tr key={row.id}>
            <td>{row.name}</td>
          </tr>
        ))}
      </Table>,
    );

    expect(screen.getByText('No EDJErs yet.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).not.toBeInTheDocument();
  });

  it('renders the table when rows are computed from a NON-empty list', () => {
    // The other direction, so the fix above cannot be "always empty".
    const rows = [{ id: 1, name: 'Alvarez' }];

    render(
      <Table caption="EDJErs" columns={COLUMNS} emptyMessage="No EDJErs yet.">
        {rows.map((row) => (
          <tr key={row.id}>
            <td>{row.name}</td>
          </tr>
        ))}
      </Table>,
    );

    expect(screen.queryByText('No EDJErs yet.')).not.toBeInTheDocument();
    expect(
      within(screen.getByRole('table')).getByRole('cell', { name: 'Alvarez' }),
    ).toBeInTheDocument();
  });

  it.each([
    ['null', null],
    ['false from a short-circuit', false],
    ['undefined', undefined],
  ])('renders the empty message for %s children', (_label, children) => {
    render(
      <Table caption="EDJErs" columns={COLUMNS} emptyMessage="No EDJErs yet.">
        {children}
      </Table>,
    );

    expect(screen.getByText('No EDJErs yet.')).toBeInTheDocument();
  });

  it('renders a bare header rather than nothing when there are no rows and NO message', () => {
    // `emptyMessage` is optional, and the guard is `isEmpty && emptyMessage !== undefined`. A caller
    // that supplies neither rows nor a message still gets a table, which is the documented behaviour.
    render(<Table caption="EDJErs" columns={COLUMNS} />);

    expect(screen.getByRole('table', { name: 'EDJErs' })).toBeInTheDocument();
  });

  describe('sortable columns', () => {
    function renderSortable(column: Partial<TableSortableColumn> = {}) {
      const onSort = vi.fn();
      render(
        <Table
          caption="Team Directory"
          columns={[{ label: 'Hire Date', onSort, ...column }, 'Current Client(s)']}
        >
          <tr>
            <td>03/14/2016</td>
            <td>Buckeye Mutual</td>
          </tr>
        </Table>,
      );
      return onSort;
    }

    it('makes a sortable header a real button, not a clickable cell', async () => {
      // The mockups put `cursor:pointer` on every `th`. A div or a cell with a click handler cannot be
      // tabbed to and announces nothing, so the affordance has to be an actual control.
      const onSort = renderSortable();

      await userEvent.click(screen.getByRole('button', { name: 'Sort by hire date' }));

      expect(onSort).toHaveBeenCalledOnce();
    });

    it('reports no sort direction on a column that is not the sorted one', () => {
      renderSortable();

      // Named by its visible label — the button's aria-label addresses the CONTROL, and the header cell
      // keeps announcing the column.
      expect(screen.getByRole('columnheader', { name: 'Hire Date' })).toHaveAttribute(
        'aria-sort',
        'none',
      );
    });

    it('announces the direction through aria-sort, not only with a caret', () => {
      // The caret is the picture. aria-sort is the statement, and it is the only one a screen-reader
      // user gets — a glyph in a span tells them nothing about which column is ordering the table.
      renderSortable({ sortDirection: 'descending' });

      expect(screen.getByRole('columnheader', { name: 'Hire Date' })).toHaveAttribute(
        'aria-sort',
        'descending',
      );
    });

    it('leaves a plain string column with no sort affordance at all', () => {
      renderSortable();

      const header = screen.getByRole('columnheader', { name: 'Current Client(s)' });
      expect(header).not.toHaveAttribute('aria-sort');
      expect(within(header).queryByRole('button')).not.toBeInTheDocument();
    });

    it('keeps the visible label inside the accessible name (WCAG 2.5.3)', () => {
      // "Sort by hire date" CONTAINS "Hire Date", so a voice-control user saying what they can see
      // reaches the control. Replacing the label rather than extending it is what breaks that.
      renderSortable();

      const button = screen.getByRole('button', { name: 'Sort by hire date' });
      expect(button.textContent?.toLowerCase()).toContain('hire date');
    });
  });
});

/**
 * The one-appearance alert slot (feature 008, T004/T005).
 *
 * Extracted because the same class run had been copied into FIVE places — `EdjerListPage` twice,
 * `EdjerFormPage` twice, and a local `Alert` inside `LookupSection`. Rule of Three was met twice over.
 *
 * It takes **no tone prop**, deliberately. All five call sites render the identical appearance, so a
 * variant axis would be a second value nothing asks for — Principle II forbids adding structure
 * speculatively, and Principle VI's simplicity ordering says build what is needed now. When a second
 * appearance is genuinely required, the prop can be added against a real caller.
 */
describe('Alert', () => {
  it('announces itself to assistive technology', () => {
    render(<Alert>EDJErs could not be loaded. Try again.</Alert>);

    // `role="alert"` is the whole point: a write failure that renders silently is a failure the user
    // never learns about.
    expect(screen.getByRole('alert')).toHaveTextContent('EDJErs could not be loaded. Try again.');
  });

  it('renders its children rather than flattening them to text', () => {
    render(
      <Alert>
        <span>End-date these assignments first:</span>
      </Alert>,
    );

    expect(
      within(screen.getByRole('alert')).getByText('End-date these assignments first:'),
    ).toBeInTheDocument();
  });
});

/**
 * `PageHeader`'s description slot (feature 008, T006/T007).
 *
 * Its absence is why `LookupAdminPage` and `EdjerListPage` hand-rolled their headers, which is how two
 * different `h1` sizes came to ship — `text-xl` from this component, `text-2xl` from the copies.
 */
describe('PageHeader description', () => {
  it('renders a description under the title when given one', () => {
    render(<PageHeader title="Lookups" description="Reference values the rest of Compass uses." />);

    expect(screen.getByRole('heading', { level: 1, name: 'Lookups' })).toBeInTheDocument();
    expect(screen.getByText('Reference values the rest of Compass uses.')).toBeInTheDocument();
  });

  it('renders no description element when none is given', () => {
    const { container } = render(<PageHeader title="EDJErs" />);

    // Asserting absence rather than emptiness: an empty <p> is still an element a screen reader steps
    // through, and the header is read on every screen.
    expect(container.querySelectorAll('p')).toHaveLength(0);
  });
});

/**
 * `buttonClassName` (feature 008, T008/T009).
 *
 * `Button` renders a `<button>`, so a navigation control could not use it — which is why
 * `EdjerListPage` copied its primary tone class run verbatim onto a router `<Link>`. Exporting the
 * computation rather than adding an `href` prop is deliberate: this SPA routes through TanStack
 * `<Link to>`, and a `Button` that rendered a plain `<a href>` would full-page reload inside the SPA.
 *
 * The invariant worth protecting is that there is ONE definition of a button's appearance. These tests
 * assert the helper and the component cannot drift apart.
 */
describe('buttonClassName', () => {
  it('carries the primary tone, including the border §1.4.11 needs', () => {
    const classes = buttonClassName({ variant: 'primary' });

    expect(classes).toContain('bg-brand-green');
    // The border is not decoration: brand green on white is 1.95:1, so without it the control has no
    // boundary meeting §1.4.11's 3:1 — the trap the mockups cannot show, since they draw no border.
    expect(classes).toContain('border-brand-green-700');
    expect(classes).toContain('text-brand-ink');
  });

  it('carries the neutral tone for secondary controls', () => {
    expect(buttonClassName({ variant: 'secondary' })).toContain('bg-white');
  });

  it('honours the small size', () => {
    expect(buttonClassName({ variant: 'secondary', size: 'small' })).toContain('text-xs');
    expect(buttonClassName({ variant: 'secondary' })).toContain('text-sm');
  });

  it('is the same string Button renders, so the two cannot drift', () => {
    render(<Button variant="primary">Add New EDJEr</Button>);

    const rendered = screen.getByRole('button', { name: 'Add New EDJEr' }).className;
    expect(rendered).toBe(buttonClassName({ variant: 'primary' }));
  });

  it.each(['primary', 'secondary'] as const)('sets a %s label in semibold', (variant) => {
    // Owner request 2026-08-18: a button's label was rendering at font-medium, the same weight as the
    // body copy around it. Asserted for BOTH variants — a secondary button is not a lesser control,
    // and the weight is the one thing that must not distinguish them.
    const classes = buttonClassName({ variant });

    expect(classes).toContain('font-semibold');
    expect(classes).not.toContain('font-medium');
  });
});

/**
 * `tableLinkClass` (owner request 2026-08-21) — the Team Directory's name link, with one token changed
 * for a measured reason. `table-link-consistency.test.ts` keeps it the only definition.
 */
describe('tableLinkClass', () => {
  it('carries the Team Directory baseline: brand-text under a 2px green underline', () => {
    expect(tableLinkClass).toContain('text-brand-text');
    expect(tableLinkClass).toContain('underline');
    expect(tableLinkClass).toContain('decoration-brand-green-700');
    expect(tableLinkClass).toContain('decoration-2');
    expect(tableLinkClass).toContain('underline-offset-2');
  });

  it('hovers to a green that clears AA as normal text', () => {
    // The one deviation from the baseline. `brand-green-700` is a BORDER token at 3.37:1, so the
    // baseline's hover failed §1.4.3 while the pointer was on the link -- the same defect
    // `buttonClassName` records fixing. The accent step is the same hue at 6.80:1.
    expect(tableLinkClass).toContain('hover:text-brand-green-accent');
    expect(tableLinkClass).not.toContain('hover:text-brand-green-700');
  });

  it('states colour and decoration only, leaving layout to the cell', () => {
    // A weight or wrapping rule here would bake a per-site decision into a shared value. Those
    // compose alongside instead.
    expect(tableLinkClass).not.toContain('font-');
    expect(tableLinkClass).not.toContain('whitespace-');
    expect(tableLinkClass).not.toContain('text-sm');
    expect(tableLinkClass).not.toContain('text-xs');
  });
});

// ---------------------------------------------------------------- Modal (mockup screen 6)

describe('Modal', () => {
  /**
   * Records every time an element GAINS focus, so a test can assert focus never touched it — not
   * merely that it does not hold it once the dust settles.
   *
   * **Two tests assert this comes back EMPTY, so one asserts it does not.** A helper that records
   * nothing satisfies every "length 0" assertion forever — a familiar shape in this repo, where a
   * check over zero items passes. `pulls focus back when a control outside the dialog takes it`
   * focuses the outside control on purpose and asserts the touch was seen, which is the positive
   * control for the two negative ones.
   */
  function trackFocus(element: HTMLElement): string[] {
    const touches: string[] = [];
    element.addEventListener('focusin', () => touches.push(element.textContent ?? ''));
    return touches;
  }

  it('renders as a labelled dialog reachable by role', () => {
    render(
      <Modal title="Add SOW / Contract" onClose={vi.fn()}>
        <p>Body</p>
      </Modal>,
    );

    expect(screen.getByRole('dialog', { name: 'Add SOW / Contract' })).toBeInTheDocument();
  });

  it('renders the children in the body', () => {
    render(
      <Modal title="Add SOW / Contract" onClose={vi.fn()}>
        <p>Contract details go here</p>
      </Modal>,
    );

    expect(screen.getByText('Contract details go here')).toBeInTheDocument();
  });

  it('renders the footer actions when given', () => {
    render(
      <Modal
        title="Add SOW / Contract"
        onClose={vi.fn()}
        footer={<Button variant="primary">Save SOW</Button>}
      >
        <p>Body</p>
      </Modal>,
    );

    expect(screen.getByRole('button', { name: 'Save SOW' })).toBeInTheDocument();
  });

  it('calls onClose when the close affordance is activated', async () => {
    const onClose = vi.fn();
    render(
      <Modal title="Add SOW / Contract" onClose={onClose}>
        <p>Body</p>
      </Modal>,
    );

    await userEvent.click(screen.getByRole('button', { name: /close/i }));

    expect(onClose).toHaveBeenCalled();
  });

  it('calls onClose on Escape', async () => {
    const onClose = vi.fn();
    render(
      <Modal title="Add SOW / Contract" onClose={onClose}>
        <p>Body</p>
      </Modal>,
    );

    await userEvent.keyboard('{Escape}');

    expect(onClose).toHaveBeenCalled();
  });

  /**
   * Focus management — the two defects an adversarial review of #448 found in this primitive.
   *
   * `aria-modal="true"` *tells* assistive technology that the rest of the page is inert. It enforces
   * nothing: the browser's Tab order is unchanged, so a keyboard-only or screen-reader user could tab
   * straight out of the dialog into the controls it is supposed to be covering, and on close was
   * returned to the top of the document rather than to whatever opened it.
   *
   * These are asserted in jsdom rather than a browser deliberately. Per `docs/TEST-STRATEGY.md`, a
   * browser test earns its cost with computed style, real layout and scroll geometry; focus ORDER is
   * none of those, and `userEvent.tab()` honours `preventDefault` on the keydown the trap raises.
   */
  it('moves focus into the dialog when it opens', () => {
    render(
      <Modal title="Add SOW / Contract" onClose={vi.fn()}>
        <p>Body</p>
      </Modal>,
    );

    // The dialog itself, not its first control: the accessible name is then what a screen reader
    // announces on open, and the ✕ is not pre-armed under the space bar.
    expect(screen.getByRole('dialog', { name: 'Add SOW / Contract' })).toHaveFocus();
  });

  it('wraps Tab from the last control back to the first', async () => {
    render(
      <Modal
        title="Add SOW / Contract"
        onClose={vi.fn()}
        footer={<Button variant="primary">Save SOW</Button>}
      >
        <input aria-label="Note" />
      </Modal>,
    );

    // Focused directly rather than tabbed to. The ring's ORDER is the browser's own and is not this
    // component's to assert; where Tab goes off the END of it is, and starting there says so.
    screen.getByRole('button', { name: 'Save SOW' }).focus();

    await userEvent.tab();

    expect(screen.getByRole('button', { name: /close/i })).toHaveFocus();
  });

  it('wraps Shift-Tab from the first control back to the last', async () => {
    render(
      <Modal
        title="Add SOW / Contract"
        onClose={vi.fn()}
        footer={<Button variant="primary">Save SOW</Button>}
      >
        <input aria-label="Note" />
      </Modal>,
    );

    screen.getByRole('button', { name: /close/i }).focus();

    await userEvent.tab({ shift: true });

    expect(screen.getByRole('button', { name: 'Save SOW' })).toHaveFocus();
  });

  it('never lets Tab reach a control on the page behind it', async () => {
    // The defect exactly: `aria-modal` claims the button behind is inert, and nothing made it so.
    render(
      <>
        <button type="button">Behind the dialog</button>
        <Modal title="Add SOW / Contract" onClose={vi.fn()}>
          <input aria-label="Note" />
        </Modal>
      </>,
    );
    const behind = screen.getByRole('button', { name: 'Behind the dialog' });
    // Counted, not inspected at the end. A trap that lets focus out and then pulls it back is still
    // a trap that let it out — and this dialog has two mechanisms, so asserting only the final
    // resting place lets the second one cover for the first being broken.
    const touched = trackFocus(behind);

    for (let step = 0; step < 4; step += 1) {
      await userEvent.tab();
      expect(touched, `Tab ${step + 1} put focus on the page behind`).toHaveLength(0);
    }
  });

  it('pulls focus back in when it has fallen out of the dialog entirely', async () => {
    // Reachable without focus ever leaving the dialog by hand, and this is how: `SowForm`'s Rate
    // Increase radios UNMOUNT when the contract type changes, and a browser drops focus to `body`
    // when the element holding it is removed. `body` is outside the ring, so the wrap arithmetic has
    // no edge to compare it against — with no case for it, the next Tab walks into the page behind.
    function Harness() {
      const [showConditional, setShowConditional] = useState(true);
      return (
        <>
          <button type="button">Behind the dialog</button>
          <Modal title="Add SOW / Contract" onClose={vi.fn()}>
            {showConditional && (
              <button type="button" onClick={() => setShowConditional(false)}>
                Removes itself
              </button>
            )}
          </Modal>
        </>
      );
    }
    render(<Harness />);
    // The `focusin` net cannot see this one: focus is on `body`, and `body` gaining focus fires no
    // `focusin`. So this is the test that holds `handleTab`'s own out-of-ring branch, and it has to
    // count touches rather than check the destination — with only the destination asserted, deleting
    // that branch leaves every one of these tests green (verified by mutation).
    const touched = trackFocus(screen.getByRole('button', { name: 'Behind the dialog' }));

    await userEvent.click(screen.getByRole('button', { name: 'Removes itself' }));
    expect(document.body).toHaveFocus();

    await userEvent.tab();

    expect(touched, 'Tab from `body` went through the page behind').toHaveLength(0);
    expect(screen.getByRole('button', { name: /close/i })).toHaveFocus();
  });

  it('pulls focus back when a control outside the dialog takes it', async () => {
    // The invariant `aria-modal="true"` promises, stated directly rather than through Tab: while this
    // dialog is open, nothing outside it can hold focus. Edge-detection on a flat list of controls
    // cannot deliver that on its own — a dialog whose last control is a radio group defeats it,
    // measured in all three engines — so this is the assertion that has to hold for every shape.
    render(
      <>
        <button type="button">Behind the dialog</button>
        <Modal title="Add SOW / Contract" onClose={vi.fn()}>
          <input aria-label="Note" />
        </Modal>
      </>,
    );
    const behind = screen.getByRole('button', { name: 'Behind the dialog' });
    const touched = trackFocus(behind);

    behind.focus();

    // The positive control for `trackFocus`: this touch is deliberate and MUST be recorded, which is
    // what stops the two tests asserting an empty list from passing on a helper that records nothing.
    expect(touched).toHaveLength(1);
    expect(behind).not.toHaveFocus();
    expect(screen.getByRole('button', { name: /close/i })).toHaveFocus();
  });

  it('puts an escape back at the END of the ring when the last Tab went backwards', async () => {
    render(
      <>
        <button type="button">Behind the dialog</button>
        <Modal
          title="Add SOW / Contract"
          onClose={vi.fn()}
          footer={<Button variant="primary">Save SOW</Button>}
        >
          <input aria-label="Note" />
        </Modal>
      </>,
    );
    // A Shift-Tab from mid-ring, which the browser resolves itself — its only effect here is to
    // record the direction, so that focus leaving afterwards is put back where it left from rather
    // than at the top of the dialog.
    screen.getByLabelText('Note').focus();
    await userEvent.tab({ shift: true });
    expect(screen.getByRole('button', { name: /close/i })).toHaveFocus();

    screen.getByRole('button', { name: 'Behind the dialog' }).focus();

    expect(screen.getByRole('button', { name: 'Save SOW' })).toHaveFocus();
  });

  it('never chooses a hidden input as the place to put focus back', async () => {
    render(
      <>
        <button type="button">Behind the dialog</button>
        <Modal title="Add SOW / Contract" onClose={vi.fn()}>
          <input aria-label="Note" />
          <input type="hidden" name="assignmentId" defaultValue="7" />
        </Modal>
      </>,
    );
    // Backwards, so the net aims at the ring's LAST control. A hidden input is in the DOM and is not
    // `disabled`, so a selector that admits it puts it there — and `focus()` on a hidden input is a
    // silent no-op, which would strand focus on the page behind with nothing to fire again.
    screen.getByLabelText('Note').focus();
    await userEvent.tab({ shift: true });
    const behind = screen.getByRole('button', { name: 'Behind the dialog' });

    behind.focus();

    expect(behind).not.toHaveFocus();
    expect(screen.getByLabelText('Note')).toHaveFocus();
  });

  it('captures the opener exactly once per open, StrictMode included', async () => {
    // `main.tsx` renders the whole SPA inside `<StrictMode>`, and the thing to know is that it does
    // NOT double-invoke the run that matters here. StrictMode double-invokes MOUNT effects; holding
    // the dialog element in state makes `null -> node` an update, so the run that reads
    // `document.activeElement` executes once and the double-invoked run is the `dialog === null`
    // early return, which reads nothing. Instrumented to confirm: `effect:null, effect:null,
    // effect:node` — one capture per open.
    //
    // **No mutation kills this test alone**: it dies with the two restore tests below. It is kept
    // because it is the only place that exercises the component the way the app renders it, and a
    // future change that moves the capture back into a mount-position effect would be caught here.
    function Harness() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Add SOW
          </button>
          {open && (
            <Modal title="Add SOW / Contract" onClose={() => setOpen(false)}>
              <p>Body</p>
            </Modal>
          )}
        </>
      );
    }

    render(<Harness />, { wrapper: StrictMode });
    const opener = screen.getByRole('button', { name: 'Add SOW' });
    await userEvent.click(opener);
    expect(screen.getByRole('dialog')).toHaveFocus();

    await userEvent.keyboard('{Escape}');

    expect(opener).toHaveFocus();
  });

  it('leaves no document listener behind when it closes', () => {
    // Invisible to every other test here: a leaked handler holds the DETACHED dialog, so its
    // pull-back is a silent no-op. It is still one listener per open, so it accumulates, and
    // mutating the cleanup to skip `focusin` left all of these green.
    const added = vi.spyOn(document, 'addEventListener');
    const removed = vi.spyOn(document, 'removeEventListener');
    try {
      const { unmount } = render(
        <Modal title="Add SOW / Contract" onClose={vi.fn()}>
          <p>Body</p>
        </Modal>,
      );
      // The allow-list is load-bearing and also bounds what this proves: React 19 attaches a
      // document `selectionchange` it never removes, so an unfiltered comparison would fail on
      // React's own listener — and a future leak of a type not named here would be filtered out and
      // pass. Add the type here in the same change that adds the listener.
      const ours = (calls: [string, ...unknown[]][]) =>
        calls.map(([type]) => type).filter((type) => type === 'keydown' || type === 'focusin');
      const attached = ours(added.mock.calls as [string, ...unknown[]][]);

      unmount();

      // Asserted non-empty FIRST: comparing two lists that are both empty is the shape that passes
      // by measuring nothing, which this repository has shipped eight times.
      expect(attached.length).toBeGreaterThan(0);
      expect(ours(removed.mock.calls as [string, ...unknown[]][]).sort()).toEqual(attached.sort());
    } finally {
      added.mockRestore();
      removed.mockRestore();
    }
  });

  it('restores focus to the control that opened it when it closes', async () => {
    function Harness() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Add SOW
          </button>
          {open && (
            <Modal title="Add SOW / Contract" onClose={() => setOpen(false)}>
              <p>Body</p>
            </Modal>
          )}
        </>
      );
    }

    render(<Harness />);
    const opener = screen.getByRole('button', { name: 'Add SOW' });
    await userEvent.click(opener);
    expect(screen.getByRole('dialog')).toHaveFocus();

    await userEvent.click(screen.getByRole('button', { name: /close/i }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(opener).toHaveFocus();
  });

  it('restores focus to the opener after Escape, not only after the close affordance', async () => {
    // All three ways out share one unmount path, and Escape is the one a keyboard user actually uses.
    function Harness() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <button type="button" onClick={() => setOpen(true)}>
            Add SOW
          </button>
          {open && (
            <Modal title="Add SOW / Contract" onClose={() => setOpen(false)}>
              <p>Body</p>
            </Modal>
          )}
        </>
      );
    }

    render(<Harness />);
    const opener = screen.getByRole('button', { name: 'Add SOW' });
    await userEvent.click(opener);
    // Focus demonstrably inside the dialog BEFORE closing. Without it this test passes on a Modal
    // that manages no focus at all: the opener never lost it, so it needs no restoring.
    await userEvent.tab();
    expect(screen.getByRole('button', { name: /close/i })).toHaveFocus();

    await userEvent.keyboard('{Escape}');

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(opener).toHaveFocus();
  });

  it('does not leak a submit into a form the page behind it owns (#448)', async () => {
    // React portals propagate synthetic events through the React tree, not the DOM tree, so a form
    // inside a modal is a React descendant of whatever form rendered it. No caller does this today;
    // the test stays because the defect is in `Modal` and is silent when it fires.
    const outer = vi.fn((event: React.FormEvent) => event.preventDefault());
    const inner = vi.fn((event: React.FormEvent) => event.preventDefault());

    render(
      <form onSubmit={outer}>
        <Modal title="New Assignment" onClose={vi.fn()}>
          <form onSubmit={inner}>
            <button type="submit">Save assignment</button>
          </form>
        </Modal>
      </form>,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Save assignment' }));

    expect(inner).toHaveBeenCalledTimes(1);
    expect(outer).not.toHaveBeenCalled();
  });
});

describe('ScrollableRegion', () => {
  /**
   * Feature 011 (issue #83). Two properties, and they fail independently — which is the whole reason
   * this primitive exists rather than a bare `overflow-x-auto` div:
   *
   * - **Reachability**: a bare scroll container takes no focus, so off-screen columns were reachable
   *   only with a pointer. axe reports `scrollable-region-focusable`, impact `serious`.
   * - **Legibility**: a clipped value looked complete. On an internal client the assignment history was
   *   cut 20px short and `09/09/2026` rendered as `09/09/202` — verified by construction: reverting
   *   this primitive on that screen reproduces exactly 20px of hidden content.
   */
  it('is a group, not a landmark — a region per table would pollute landmark navigation', () => {
    render(
      <ScrollableRegion label="Assignment history">
        <p>content</p>
      </ScrollableRegion>,
    );

    // `Card` is already a named `region`; nesting another inside it made
    // `getByRole('region', { name: /employee types/i })` ambiguous and broke 20 LookupAdminPage tests.
    expect(screen.queryByRole('region', { name: 'Assignment history' })).toBeNull();
    expect(screen.getByRole('group', { name: 'Assignment history' })).toBeInTheDocument();
  });

  it('is keyboard focusable, so the hidden columns are reachable without a pointer', () => {
    render(
      <ScrollableRegion label="Assignment history">
        <table>
          <tbody>
            <tr>
              <td>cell</td>
            </tr>
          </tbody>
        </table>
      </ScrollableRegion>,
    );

    const region = screen.getByRole('group', { name: 'Assignment history' });
    expect(region).toHaveAttribute('tabindex', '0');
  });

  it('carries an accessible name, because an unlabelled focusable region is its own problem', () => {
    render(
      <ScrollableRegion label="EDJEr assignment history">
        <p>content</p>
      </ScrollableRegion>,
    );

    expect(screen.getByRole('group', { name: 'EDJEr assignment history' })).toBeInTheDocument();
  });

  it('renders an edge mask that the Gate A assertion can find', () => {
    const { container } = render(
      <ScrollableRegion label="Assignment history">
        <p>content</p>
      </ScrollableRegion>,
    );

    // The `data-` hook rather than a class or a computed gradient: the e2e assertion queries this
    // attribute, so a mask that stopped carrying it would be a requirement with no enforcement.
    expect(container.querySelector('[data-scroll-edge-mask]')).not.toBeNull();
  });

  it('hides the mask from assistive technology and from the pointer', () => {
    const { container } = render(
      <ScrollableRegion label="Assignment history">
        <p>content</p>
      </ScrollableRegion>,
    );

    const mask = container.querySelector('[data-scroll-edge-mask]');
    // Decoration over content: announcing it would add noise, and catching clicks would steal them
    // from the cell underneath.
    expect(mask).toHaveAttribute('aria-hidden', 'true');
    expect(mask?.className).toContain('pointer-events-none');
  });

  it('renders its children inside the focusable region, not beside it', () => {
    render(
      <ScrollableRegion label="Assignment history">
        <span>the table</span>
      </ScrollableRegion>,
    );

    const region = screen.getByRole('group', { name: 'Assignment history' });
    expect(within(region).getByText('the table')).toBeInTheDocument();
  });
});

describe('StackedRows', () => {
  interface Row {
    id: number;
    name: string;
    client: string;
    ended: string;
  }

  const ROWS: Row[] = [
    { id: 1, name: 'Marcus Bellweather', client: 'Leading EDJE (Internal)', ended: '09/09/2026' },
    { id: 2, name: 'Priya Raghunathan', client: 'Everlee Pharmaceuticals', ended: 'Current' },
  ];

  const COLUMNS = ['EDJEr', 'Client', 'End Date'];

  function renderRows(overrides: Partial<Parameters<typeof StackedRows<Row>>[0]> = {}) {
    return render(
      <StackedRows<Row>
        caption="Assignment history"
        columns={COLUMNS}
        rows={ROWS}
        rowKey={(row) => row.id}
        cells={(row) => [row.name, row.client, row.ended]}
        {...overrides}
      />,
    );
  }

  /**
   * `useIsNarrowViewport` returns false when `matchMedia` is unavailable, which is the state jsdom runs
   * in — so these render the TABLE branch unless a test stubs otherwise. That default is deliberate: it
   * is what let the five migrated screens keep their existing specs unchanged. See the hook's own tests.
   */
  /**
   * Two columns CAN legitimately share a label — "Start Date" appearing under two different
   * groupings, say — and today none of the seven card-stack screens does, which is exactly why this is
   * worth a test rather than a comment. A latent React key collision surfaces the day someone adds the
   * second one, on a screen nobody was editing.
   *
   * The key must be the INDEX. `columns` is a fixed-order, fixed-length list rendered straight through,
   * so the index is stable and unique by construction; the label is neither.
   */
  describe('duplicate column labels', () => {
    const DUPE_COLUMNS = ['EDJEr', 'Start Date', 'Start Date'];

    // In `afterEach`, not at the end of each test: a failing assertion returns before any trailing
    // cleanup, and a leaked `matchMedia` stub then makes the NEXT test render the card branch. That
    // happened while writing these two — it broke the desktop tests above, which is a far more
    // confusing signal than the failure it was hiding.
    afterEach(() => {
      vi.unstubAllGlobals();
      vi.restoreAllMocks();
    });

    function renderDuped() {
      return render(
        <StackedRows<Row>
          caption="Assignment history"
          columns={DUPE_COLUMNS}
          rows={ROWS}
          rowKey={(row) => row.id}
          cells={(row) => [row.name, row.client, row.ended]}
        />,
      );
    }

    it('renders both same-named columns in the table branch without a key collision', () => {
      const errors: string[] = [];
      const spy = vi.spyOn(console, 'error').mockImplementation((...args: unknown[]) => {
        errors.push(args.map(String).join(' '));
      });

      renderDuped();

      const firstBodyRow = screen.getAllByRole('row')[1];
      const cells = within(firstBodyRow).getAllByRole('cell');
      expect(cells).toHaveLength(3);
      expect(cells[1]).toHaveTextContent('Leading EDJE (Internal)');
      expect(cells[2]).toHaveTextContent('09/09/2026');
      expect(errors.filter((e) => /same key|unique "key"/i.test(e))).toEqual([]);
      expect(spy).toBeDefined();
    });

    it('renders both same-named columns in the card branch without a key collision', () => {
      vi.stubGlobal(
        'matchMedia',
        vi.fn(() => ({ matches: true, addEventListener: vi.fn(), removeEventListener: vi.fn() })),
      );
      const errors: string[] = [];
      const spy = vi.spyOn(console, 'error').mockImplementation((...args: unknown[]) => {
        errors.push(args.map(String).join(' '));
      });

      renderDuped();

      const list = screen.getByRole('list', { name: 'Assignment history' });
      // Three label-value pairs per card, even though two labels are identical — a collision would
      // drop one, silently losing a value rather than failing.
      const firstCard = within(list).getAllByRole('listitem')[0];
      expect(within(firstCard).getAllByRole('term')).toHaveLength(3);
      expect(within(firstCard).getAllByRole('definition')).toHaveLength(3);
      expect(errors.filter((e) => /same key|unique "key"/i.test(e))).toEqual([]);
      expect(spy).toBeDefined();
    });
  });

  it('renders the table branch at desktop width, so desktop specs are unaffected', () => {
    renderRows();

    const table = screen.getByRole('table', { name: 'Assignment history' });
    expect(within(table).getByText('Marcus Bellweather')).toBeInTheDocument();
    expect(within(table).getByRole('columnheader', { name: 'End Date' })).toBeInTheDocument();
  });

  it('pairs every column label with its cell, in order', () => {
    renderRows();

    const firstBodyRow = screen.getAllByRole('row')[1];
    const cells = within(firstBodyRow).getAllByRole('cell');
    expect(cells.map((cell) => cell.textContent)).toEqual([
      'Marcus Bellweather',
      'Leading EDJE (Internal)',
      '09/09/2026',
    ]);
  });

  it('shows the empty message instead of an empty table', () => {
    renderRows({ rows: [], emptyMessage: 'No assignments on record.' });

    expect(screen.getByText('No assignments on record.')).toBeInTheDocument();
    expect(screen.queryByRole('table')).toBeNull();
  });

  it('renders label-value cards below md, carrying every column label', () => {
    // Stub the narrow viewport: this is the branch the five read screens use on a phone, and the one
    // that removes the off-screen axis entirely.
    vi.stubGlobal(
      'matchMedia',
      vi.fn(() => ({
        matches: true,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      })),
    );

    try {
      renderRows();

      // No table at all — that is the point. Nothing can strand if nothing is off-screen.
      expect(screen.queryByRole('table')).toBeNull();

      const list = screen.getByRole('list', { name: 'Assignment history' });
      const items = within(list).getAllByRole('listitem');
      expect(items).toHaveLength(2);

      // Field PARITY (CHK010): every column label appears as a card field label, and the values pair
      // with them. A card is a re-arrangement of the row, never a reduced version.
      for (const label of COLUMNS) {
        expect(within(items[0]).getByText(label)).toBeInTheDocument();
      }
      expect(within(items[0]).getByText('Marcus Bellweather')).toBeInTheDocument();
      expect(within(items[0]).getByText('09/09/2026')).toBeInTheDocument();
      expect(within(items[1]).getByText('Current')).toBeInTheDocument();
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it('puts a per-column class on the CELL box, never on a span inside it', () => {
    // This pin outlived its original reason and is kept deliberately. The Compass visual baselines
    // masked `.tabular-nums`, so putting the class on the cell box meant a column that MOVED still
    // failed, while a span shrank the mask to the text and let a moved column hide inside it —
    // exactly what turned three baselines red during feature 011's migration. **Issue #574 deleted
    // that gate**, so this no longer guards a comparison; it guards the shape, which is where the
    // class belongs anyway and which a future rule-3 gate would need back.
    renderRows({ cellClassNames: [undefined, undefined, 'tabular-nums'] });

    const firstBodyRow = screen.getAllByRole('row')[1];
    const cells = within(firstBodyRow).getAllByRole('cell');

    expect(cells[2].className, 'the End Date CELL carries it').toContain('tabular-nums');
    expect(cells[0].className, 'a column with no entry is untouched').not.toContain('tabular-nums');
  });

  it('carries the per-column class onto the card value below md too', () => {
    vi.stubGlobal(
      'matchMedia',
      vi.fn(() => ({
        matches: true,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      })),
    );

    try {
      const { container } = renderRows({ cellClassNames: [undefined, undefined, 'tabular-nums'] });

      const values = Array.from(container.querySelectorAll('dd'));
      expect(values[2].className).toContain('tabular-nums');
      expect(values[0].className).not.toContain('tabular-nums');
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it('uses a definition list below md, so the pairs are announced as pairs', () => {
    vi.stubGlobal(
      'matchMedia',
      vi.fn(() => ({
        matches: true,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      })),
    );

    try {
      const { container } = renderRows();

      // `dt`/`dd` rather than a stack of divs: a screen reader announces the label with its value.
      expect(container.querySelectorAll('dt')).toHaveLength(COLUMNS.length * ROWS.length);
      expect(container.querySelectorAll('dd')).toHaveLength(COLUMNS.length * ROWS.length);
    } finally {
      vi.unstubAllGlobals();
    }
  });
});
