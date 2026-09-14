import { StatusPill } from '../../components/ui';

/**
 * The employee-type cell every report table shares: the EDJEr's employee type ("Full Time", "Part
 * Time", "1099") as its own column, following the EDJEr name (issue #386 — the Availability Report and
 * Client Assignment Duration report previously rendered it inline next to the name, issues #334/#335).
 * Empty when absent, since the FK backing employee type is required in practice but the wire type stays
 * nullable.
 *
 * Extracted (Rule of Three) once the Assignment Start report's new employee-type column made this the
 * third identical rendering — it previously lived as a near-duplicate `EmployeeTypeCell` in both
 * `AvailabilityReportPage.tsx` and `AssignmentDurationPage.tsx`, plus a diverged inline copy here.
 */
export function EmployeeTypeCell({ employeeType }: { employeeType: string | null }) {
  return employeeType !== null ? <StatusPill tone="neutral" label={employeeType} /> : '';
}
