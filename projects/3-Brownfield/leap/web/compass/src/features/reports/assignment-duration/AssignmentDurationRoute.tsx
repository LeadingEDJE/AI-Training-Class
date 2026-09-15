import { AssignmentDurationPage } from './AssignmentDurationPage';
import { useAssignmentDuration } from './useAssignmentDuration';

export function AssignmentDurationRoute() {
  const query = useAssignmentDuration();

  return (
    <AssignmentDurationPage
      report={query.data}
      isPending={query.isPending}
      // TWO sources, as `AvailabilityReportRoute` records: `isError` covers what still throws (dead
      // network, unparseable body), and `kind === 'failed'` covers both a non-OK status and a refusal —
      // a 403 and a 500 both resolve into the same `failed` state here.
      isError={query.isError || query.data?.kind === 'failed'}
    />
  );
}
