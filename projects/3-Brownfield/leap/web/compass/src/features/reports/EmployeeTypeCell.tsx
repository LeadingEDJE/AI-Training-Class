import { StatusPill } from '../../components/ui';

/**
 * The employee-type cell every report table shares: the EDJEr's employee type ("Full Time", "Part
 * Time", "1099") as its own column, following the EDJEr name (issue #386). Renders the literal string
 * "—" when absent, since the FK backing employee type is required in practice but the wire type stays
 * nullable.
 *
 * Still duplicated as near-identical inline copies in `AvailabilityReportPage.tsx` and
 * `AssignmentDurationPage.tsx` — the Rule of Three extraction only ever happened for the Assignment
 * Start report, which is the one report that actually imports this component.
 */
export function EmployeeTypeCell({ employeeType }: { employeeType: string | null }) {
  return employeeType !== null ? <StatusPill tone="neutral" label={employeeType} /> : '';
}
