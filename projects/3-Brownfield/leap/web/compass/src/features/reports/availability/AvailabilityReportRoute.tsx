import { AvailabilityReportPage } from './AvailabilityReportPage';
import { useAvailabilityReport } from '../useAvailabilityReport';

/** Connects the Availability Report screen to the read surface. */
export function AvailabilityReportRoute() {
  const query = useAvailabilityReport();

  return (
    <AvailabilityReportPage
      report={query.data}
      isPending={query.isPending}
      // TWO sources, for the reason `SalesDashboardRoute` records: `isError` covers what still throws
      // (dead network, unparseable body), and `kind === 'failed'` covers a non-OK status, which now
      // RESOLVES so a 403 can arrive as its own state. A refusal is in neither, deliberately — it is
      // not an error, and folding it in here would undo the distinction the three-state load exists
      // to draw.
      isError={query.isError || query.data?.kind === 'failed'}
    />
  );
}
