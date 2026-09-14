import { render, screen } from '@testing-library/react';
import { AssignmentBreadcrumb } from '../../../../src/features/assignments/AssignmentBreadcrumb';
import type { BreadcrumbSegment } from '../../../../src/features/assignments/assignment-trail';

/**
 * The trail above an assignment screen. Injected rather than derived: the EDJEr-nested routes serve
 * both the Team Directory and the EDJEr admin record by the same path.
 */
describe('AssignmentBreadcrumb', () => {
  const TEAM: BreadcrumbSegment[] = [
    { label: 'Team Directory', href: '/compass/team-directory' },
    { label: 'Maya Alvarez', href: '/compass/team-directory/7' },
  ];

  const ADMIN: BreadcrumbSegment[] = [
    { label: 'Admin' },
    { label: 'EDJEr Configuration', href: '/compass/admin/edjers' },
    { label: 'Maya Alvarez', href: '/compass/admin/edjers/7' },
  ];

  it('renders each linked segment as an anchor, in order', () => {
    render(<AssignmentBreadcrumb trail={TEAM} current="New Assignment" />);

    const links = screen.getAllByRole('link');
    expect(links.map((link) => link.textContent)).toEqual(['Team Directory', 'Maya Alvarez']);
    expect(links[0]).toHaveAttribute('href', '/compass/team-directory');
    expect(links[1]).toHaveAttribute('href', '/compass/team-directory/7');
  });

  it('renders a segment with no href as plain text, not a dead link', () => {
    // "Admin" mirrors the EDJEr form's own trail, which renders the word as text.
    render(<AssignmentBreadcrumb trail={ADMIN} current="New Assignment" />);

    expect(screen.getByText(/Admin/)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'Admin' })).not.toBeInTheDocument();
  });

  it('names the current screen last, and never as a link', () => {
    render(<AssignmentBreadcrumb trail={ADMIN} current="New Assignment" />);

    expect(screen.queryByRole('link', { name: 'New Assignment' })).not.toBeInTheDocument();
    expect(document.body).toHaveTextContent(
      'Admin / EDJEr Configuration / Maya Alvarez / New Assignment',
    );
  });

  it('returns an administrator to the record they came from', () => {
    // The last linked segment is the ADMIN record, not the read-only one.
    render(<AssignmentBreadcrumb trail={ADMIN} current="Assignment — Contoso" />);

    expect(screen.getByRole('link', { name: 'Maya Alvarez' })).toHaveAttribute(
      'href',
      '/compass/admin/edjers/7',
    );
  });

  it('renders a single-segment trail without a leading separator', () => {
    render(<AssignmentBreadcrumb trail={[{ label: 'Only', href: '/x' }]} current="Here" />);

    expect(document.body).toHaveTextContent('Only / Here');
  });

  it('renders the current screen alone when there is no trail', () => {
    // Not reachable today; asserted so an empty trail degrades to a name, not a stray separator.
    render(<AssignmentBreadcrumb trail={[]} current="Here" />);

    expect(document.body).toHaveTextContent('Here');
    expect(screen.queryByRole('link')).not.toBeInTheDocument();
  });
});
