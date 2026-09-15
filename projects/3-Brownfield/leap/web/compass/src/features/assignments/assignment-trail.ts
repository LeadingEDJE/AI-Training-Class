/** One ancestor in an assignment screen's trail. `href` is omitted for a segment with no screen. */
export interface BreadcrumbSegment {
  label: string;
  href?: string;
}

export type AssignmentOrigin = 'admin' | 'directory';

/**
 * The origin a validated `?from=` denotes. Each EDJEr-nested container repeats this comparison
 * inline in a couple of places; this is just the one shared spot for the common case.
 */
export function assignmentOriginOf(from: 'admin' | undefined): AssignmentOrigin {
  return from === 'admin' ? 'admin' : 'directory';
}

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
