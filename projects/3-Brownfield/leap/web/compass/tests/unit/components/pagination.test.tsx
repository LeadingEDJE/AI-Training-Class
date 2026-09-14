import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { PageSizeField, Pager } from '../../../src/components/pagination';

const EDJERS = { singular: 'EDJEr', plural: 'EDJErs' };

function renderPager(overrides: Partial<Parameters<typeof Pager>[0]> = {}) {
  const onPage = vi.fn();
  render(
    <Pager
      firstIndex={0}
      shownCount={20}
      totalCount={45}
      currentPage={1}
      totalPages={3}
      itemNoun={EDJERS}
      onPage={onPage}
      {...overrides}
    />,
  );
  return onPage;
}

describe('Pager', () => {
  it('states the range as a live region, so a page move is announced', () => {
    // The range is the only part of the screen that reliably changes on every page move, which makes
    // it the thing that tells someone their Next click did anything.
    renderPager();

    expect(screen.getByRole('status')).toHaveTextContent('Showing 1 to 20 of 45 EDJErs');
  });

  it('counts the range from the first row shown, not from the page number', () => {
    renderPager({ firstIndex: 40, shownCount: 5, currentPage: 3 });

    expect(screen.getByRole('status')).toHaveTextContent('Showing 41 to 45 of 45 EDJErs');
  });

  it('uses the singular noun for a single row', () => {
    renderPager({ shownCount: 1, totalCount: 1, totalPages: 1 });

    expect(screen.getByRole('status')).toHaveTextContent('Showing 1 to 1 of 1 EDJEr');
  });

  it('offers no page controls when everything fits on one page', () => {
    renderPager({ totalPages: 1, totalCount: 12, shownCount: 12 });

    expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument();
  });

  it('moves forward and back through named controls', async () => {
    const onPage = renderPager({ currentPage: 2, firstIndex: 20 });

    await userEvent.click(screen.getByRole('button', { name: 'Next' }));
    expect(onPage).toHaveBeenCalledWith(3);

    await userEvent.click(screen.getByRole('button', { name: 'Previous' }));
    expect(onPage).toHaveBeenCalledWith(1);
  });

  it('disables rather than hides the control at each end', async () => {
    // Hiding a control makes the row reflow as you reach a boundary and leaves a keyboard user's focus
    // somewhere unpredictable.
    const onPage = renderPager({ currentPage: 1 });

    const previous = screen.getByRole('button', { name: 'Previous' });
    expect(previous).toBeDisabled();

    await userEvent.click(previous);
    expect(onPage).not.toHaveBeenCalled();
  });

  it('disables Next on the last page', () => {
    renderPager({ currentPage: 3, firstIndex: 40, shownCount: 5 });

    expect(screen.getByRole('button', { name: 'Next' })).toBeDisabled();
  });

  it('says which page of how many', () => {
    renderPager({ currentPage: 2 });

    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
  });
});

describe('PageSizeField', () => {
  it('is a labelled select carrying every offered size', () => {
    render(<PageSizeField label="EDJErs per page" value={20} onChange={vi.fn()} />);

    const select = screen.getByRole('combobox', { name: 'EDJErs per page' });
    expect(select).toHaveValue('20');
    expect(
      Array.from(select.querySelectorAll('option')).map((option) => option.textContent),
    ).toEqual(['20', '50', '100', 'All']);
  });

  it('reports a numeric size as a NUMBER, not as the option string', async () => {
    const onChange = vi.fn();
    render(<PageSizeField label="EDJErs per page" value={20} onChange={onChange} />);

    await userEvent.selectOptions(screen.getByRole('combobox'), '50');

    expect(onChange).toHaveBeenCalledWith(50);
  });

  it('reports All as the literal, so no sentinel number can drift out of step', async () => {
    const onChange = vi.fn();
    render(<PageSizeField label="EDJErs per page" value={20} onChange={onChange} />);

    await userEvent.selectOptions(screen.getByRole('combobox'), 'all');

    expect(onChange).toHaveBeenCalledWith('all');
  });

  it('gives each instance its own id, so two on one screen keep their own labels', () => {
    render(
      <>
        <PageSizeField label="EDJErs per page" value={20} onChange={vi.fn()} />
        <PageSizeField label="Clients per page" value={50} onChange={vi.fn()} />
      </>,
    );

    expect(screen.getByRole('combobox', { name: 'EDJErs per page' })).toHaveValue('20');
    expect(screen.getByRole('combobox', { name: 'Clients per page' })).toHaveValue('50');
  });
});
