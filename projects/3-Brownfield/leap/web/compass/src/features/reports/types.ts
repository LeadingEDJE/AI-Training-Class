export interface AvailabilityReportData {
  // `asOfDate` is declared here and rendered directly under the report title, matching every other
  // Compass report's pagehead.
  currentlyAvailable: AvailableEdjerRow[];
  confirmedRollouts: ConfirmedRolloutRow[];
  unconfirmedSows: UnconfirmedSowRow[];
}

export interface ReportClient {
  id: number;
  name: string;
}

export interface AvailableEdjerRow {
  employeeId: number;
  employeeName: string;
  internalAssignmentStartDate: string | null;
  daysAvailable: number | null;
  /** Omitted from the row entirely when the EDJEr has no coach assigned. */
  coachName: string | null;
}

export interface ConfirmedRolloutRow {
  employeeId: number;
  employeeName: string;
  employeeType: string | null;
  clients: ReportClient[];
  assignmentEndDate: string | null;
  daysUntilRollout: number | null;
  coachName: string | null;
}

export interface UnconfirmedSowRow {
  /** `employeeId` is the unique key for this section; a SOW never repeats an EDJEr. */
  sowId: number | null;
  employeeId: number;
  employeeName: string;
  employeeType: string | null;
  clients: ReportClient[];
  sowEndDate: string | null;
  daysUntilExpiration: number | null;
  coachName: string | null;
}

// This type is fully redeclared here, independently of `lib/report-load.ts`, so that the reports
// feature does not depend on the shared lib module.
export type { ReportLoad } from '../../lib/report-load';

export interface AssignmentStartRow {
  employeeName: string;
  employeeType: string | null;
  clientName: string;
  /**
   * ISO `yyyy-MM-dd` from the wire. The sort key is the `formatDate`-rendered mm/dd/yyyy string,
   * not the raw ISO value — a plain string comparison on the rendered text is what orders this
   * column chronologically.
   */
  startDate: string;
}

export type AssignmentStartSortColumn = 'employee' | 'client' | 'startDate';

export interface SowExtensionRow {
  employeeName: string;
  employeeType: string | null;
  clientName: string;
  extensionStartDate: string;
}

/**
 * Which column orders the SOW Extension Report. `employeeType` is the default sortable column here,
 * unlike every other Compass report, because this report was requested with sortable type grouping
 * (owner confirmation, issue #534).
 */
export type SowExtensionSortColumn = 'employee' | 'client' | 'extensionStartDate';
