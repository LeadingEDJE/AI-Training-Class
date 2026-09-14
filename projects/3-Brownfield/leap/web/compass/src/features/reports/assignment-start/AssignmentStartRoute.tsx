import { useState } from 'react';
import { useAssignmentStartLookup } from '../useAssignmentStartLookup';
import { AssignmentStartPage } from './AssignmentStartPage';
import type { AssignmentStartRange } from './AssignmentStartPage';

/**
 * Connects the Assignment Start lookup to the read surface (AC-40, issue #78).
 *
 * **The range lives here, and it starts as `null`.** That null is what keeps the query from running on
 * mount: unlike the Availability Report, this screen answers a question the user has to ask first, and
 * firing a lookup over an arbitrary default range would put rows on screen that nobody requested.
 *
 * `AssignmentStartPage` only calls `onRun` with a range it has already validated, so an inverted range
 * never reaches this state and therefore never becomes a request — spec US4 scenario 2 requires that
 * no query be issued, not merely that its result be ignored.
 */
export function AssignmentStartRoute() {
  const [range, setRange] = useState<AssignmentStartRange | null>(null);
  const query = useAssignmentStartLookup(range);

  return (
    <AssignmentStartPage
      report={query.data}
      // `isPending` is true for an idle disabled query as well as an in-flight one, so it is only
      // meaningful once a range exists. Without that guard the screen would read as "loading" before
      // the user has asked anything.
      isPending={range !== null && query.isPending}
      // TWO sources, as `AvailabilityReportRoute` records: `isError` covers what still throws (dead
      // network, unparseable body), and `kind === 'failed'` covers a non-OK status, which resolves so a
      // 403 can arrive as its own state. A refusal is in neither, deliberately.
      isError={query.isError || query.data?.kind === 'failed'}
      onRun={setRange}
    />
  );
}
