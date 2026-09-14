/**
 * The ancestry above an assignment screen, per origin.
 *
 * Separate from `AssignmentBreadcrumb.tsx` because `react-refresh/only-export-components` forbids a
 * component file exporting functions.
 */

/** One ancestor in an assignment screen's trail. `href` is omitted for a segment with no screen. */
export interface BreadcrumbSegment {
  label: string;
  href?: string;
}

export type AssignmentOrigin = 'admin' | 'directory';

/**
 * The origin a validated `?from=` denotes. Anything but `admin` is the directory.
 *
 * Both EDJEr-nested containers convert through this rather than repeating the comparison, so adding a
 * third origin is a change the compiler can point at.
 */
export function assignmentOriginOf(from: 'admin' | undefined): AssignmentOrigin {
  return from === 'admin' ? 'admin' : 'directory';
}

/**
 * Shared by both EDJEr-nested containers so they cannot disagree: saving moves the viewer from the
 * new-assignment screen to the detail screen, and the trail must not change meaning across that step.
 */
export function employeeAssignmentTrail(
  origin: AssignmentOrigin,
  employee: { id: number; label: string },
): BreadcrumbSegment[] {
  if (origin === 'admin') {
    return [
      { label: 'Admin' },
      { label: 'EDJEr Configuration', href: '/compass/admin/edjers' },
      { label: employee.label, href: `/compass/admin/edjers/${employee.id}` },
    ];
  }

  return [
    { label: 'Team Directory', href: '/compass/team-directory' },
    { label: employee.label, href: `/compass/team-directory/${employee.id}` },
  ];
}

export function clientAssignmentTrail(client: { id: number; label: string }): BreadcrumbSegment[] {
  return [
    { label: 'Client Directory', href: '/compass/client-directory' },
    { label: client.label, href: `/compass/client-directory/${client.id}` },
  ];
}
