import { AssignmentDurationPage } from './AssignmentDurationPage';
import { useAssignmentDuration } from './useAssignmentDuration';

/** Connects the Client Assignment Duration screen to the read surface. */
export function AssignmentDurationRoute() {
  const query = useAssignmentDuration();

  return (
    <AssignmentDurationPage
      report={query.data}
      isPending={query.isPending}
      // TWO sources, as `AvailabilityReportRoute` records: `isError` covers what still throws (dead
      // network, unparseable body), and `kind === 'failed'` covers a non-OK status, which now RESOLVES
      // so a 403 can arrive as its own state. A refusal is in neither, deliberately.
      isError={query.isError || query.data?.kind === 'failed'}
    />
  );
}
