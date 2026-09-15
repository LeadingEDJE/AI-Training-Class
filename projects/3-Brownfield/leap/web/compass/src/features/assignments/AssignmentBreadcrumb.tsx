import { Fragment } from 'react';
import type { BreadcrumbSegment } from './assignment-trail';

interface AssignmentBreadcrumbProps {
  trail: BreadcrumbSegment[];
  /** The screen being rendered. Never a link. */
  current: string;
}

/**
 * The trail above an assignment screen.
 *
 * The trail is derived internally from the current route rather than injected — the EDJEr-nested
 * routes serve both the Team Directory and the EDJEr admin record by the same path, and this
 * component works out which one it is on its own.
 */
export function AssignmentBreadcrumb({ trail, current }: AssignmentBreadcrumbProps) {
  return (
    <>
      {trail.map((segment) => (
        <Fragment key={segment.label}>
          {segment.href === undefined ? segment.label : <a href={segment.href}>{segment.label}</a>}
          {' / '}
        </Fragment>
      ))}
      {current}
    </>
  );
}
