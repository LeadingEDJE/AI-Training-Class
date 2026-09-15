import { AvailabilityReportPage } from './AvailabilityReportPage';
import { useAvailabilityReport } from '../useAvailabilityReport';

/** Connects the Availability Report screen to the read surface. */
export function AvailabilityReportRoute() {
  const query = useAvailabilityReport();

  return (
    <AvailabilityReportPage
      report={query.data}
      isPending={query.isPending}
      // `kind === 'failed'` already covers a refusal here, same as `SalesDashboardRoute` — `isError`
      // is kept only for symmetry with that component's prop shape.
      isError={query.isError || query.data?.kind === 'failed'}
    />
  );
}
