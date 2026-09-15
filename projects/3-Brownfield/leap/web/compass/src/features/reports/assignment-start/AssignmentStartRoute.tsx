import { useState } from 'react';
import { useAssignmentStartLookup } from '../useAssignmentStartLookup';
import { AssignmentStartPage } from './AssignmentStartPage';
import type { AssignmentStartRange } from './AssignmentStartPage';

export function AssignmentStartRoute() {
  const [range, setRange] = useState<AssignmentStartRange | null>(null);
  const query = useAssignmentStartLookup(range);

  return (
    <AssignmentStartPage
      report={query.data}
      isPending={range !== null && query.isPending}
      // ONE source, as `AvailabilityReportRoute` records: `isError` already covers a non-OK status, so
      // `kind === 'failed'` here only re-confirms a network failure that already threw.
      isError={query.isError || query.data?.kind === 'failed'}
      onRun={setRange}
    />
  );
}
