/** The Availability Report's three sections, mirroring `AvailabilityReportDto` (AC-38, FR-009). */
export interface AvailabilityReportData {
  // `asOfDate` is deliberately NOT declared. The server returns it (AvailabilityReportDto) and the
  // three sections are computed against it, but mockup `#s-reports` puts no as-of date on the reports
  // pagehead -- only the title and the RPT-1 note -- and Principle X rule 3 gives the design source
  // presentation. Rendering one would be a departure needing its own recorded deviation. Declaring a
  // field no component reads would leave dead weight in the type instead, so it is left off; the wire
  // still carries it for any consumer that needs it.
  currentlyAvailable: AvailableEdjerRow[];
  confirmedRollouts: ConfirmedRolloutRow[];
  unconfirmedSows: UnconfirmedSowRow[];
}

/** One client named by a report row, mirroring `DashboardBreakdownClientDto`. */
export interface ReportClient {
  id: number;
  name: string;
}

/**
 * One §1 row — an EDJEr currently on the beach (FR-010).
 *
 * **Three fields, and deliberately no client.** The mockup's `EDJE Client Assignment Start` is a
 * single DATE column, not a client column plus a date; FR-029 once read it as four and that was a
 * misread of the file (spec Finding 10). The server's `AvailableEdjerRowDto` has no client property
 * either, so the three-column shape is structural on both sides rather than a rendering convention.
 */
export interface AvailableEdjerRow {
  employeeId: number;
  employeeName: string;
  internalAssignmentStartDate: string | null;
  /** Whole days from `internalAssignmentStartDate` to today — how long they've been on the beach (issue #334). */
  daysAvailable: number | null;
  /** Null when the EDJEr has no coach — the row is present regardless (FR-013). */
  coachName: string | null;
}

/** One §2 row — an EDJEr all of whose active assignments carry an end date (FR-011). */
export interface ConfirmedRolloutRow {
  employeeId: number;
  employeeName: string;
  /** e.g. "Full Time", "Part Time", "1099" -- rendered as its own column (issue #386). */
  employeeType: string | null;
  /** A LIST, because two assignments can tie on the latest end date; naming one would drop the other. */
  clients: ReportClient[];
  assignmentEndDate: string | null;
  daysUntilRollout: number | null;
  coachName: string | null;
}

/** One §3 row — a SOW expiring within 90 days with no follow-on (FR-012, BR-5). */
export interface UnconfirmedSowRow {
  /**
   * Row identity. This section is one row per SOW, so `employeeId` repeats when an EDJEr holds two
   * exposed SOWs and cannot key the list; nor can a composite of client plus date, which collides for
   * two SOWs on the same client ending the same day.
   */
  sowId: number | null;
  employeeId: number;
  employeeName: string;
  /** e.g. "Full Time", "Part Time", "1099" -- rendered as its own column (issue #386). */
  employeeType: string | null;
  clients: ReportClient[];
  /** Null if the source row had no end date. Never defaulted -- see `AvailabilityReportDto`. */
  sowEndDate: string | null;
  /** Travels with `sowEndDate`; both null or both present. */
  daysUntilExpiration: number | null;
  coachName: string | null;
}

/**
 * The outcome of a report read.
 *
 * **A refusal and a failure are distinct states**, for the reason `DashboardLoad` records: the reports
 * are gated by `CompassReporting`, which excludes Compass Admin (FR-019), and a deep link or bookmark
 * reaches the screen even though the nav does not offer it. Rendering "could not be loaded" for a 403
 * tells that viewer the screen is broken when it is working exactly as specified.
 */
// Re-exported, not redeclared. `readGated` in `lib/report-load.ts` is the single definition, lifted
// there when the assignment-duration hook became the third copy of the same non-OK-resolves read (the
// Rule of Three, which `useAvailabilityReport` had named in advance). #78's lookup is the fourth
// consumer and takes the same one.
export type { ReportLoad } from '../../lib/report-load';

/**
 * One Assignment Start lookup row (AC-40, RPT-5, issue #78), mirroring `AssignmentStartRowDto`.
 *
 * Four fields, and no identifiers: the mockup's columns are `EDJEr Name`, `Client Name` and
 * `Assignment Start Date`, plus `Employee Type` as its own column following the name (issue #386) —
 * nothing on this screen links anywhere. Adding ids "for later" would put fields in the type that no
 * component reads, which is the weight `AvailabilityReportData` above deliberately declines to carry.
 */
export interface AssignmentStartRow {
  employeeName: string;
  /** e.g. "Full Time", "Part Time", "1099" -- rendered as its own column (issue #386). */
  employeeType: string | null;
  clientName: string;
  /**
   * ISO `yyyy-MM-dd` from the wire. Rendered through `formatDate`, never printed raw (issue #234).
   *
   * **The sort key, and it is this value rather than the rendered one.** A lexical comparison of
   * `yyyy-MM-dd` IS chronological, so no parsing is needed — but the same comparison over the
   * mm/dd/yyyy string `formatDate` produces orders by month across years, putting `12/31/2025` after
   * `06/15/2026`. That is wrong and looks entirely plausible — the same trap
   * `AssignmentDurationRow.totalDays` names for its own column (FR-030).
   */
  startDate: string;
}

/**
 * Which column orders the Assignment Start lookup (issue #461).
 *
 * `startDate` is the default and matches the order the server returns
 * (`CompassReportRepository.GetAssignmentStartsAsync` orders by StartDate then EmployeeName), so the
 * screen opens on exactly the order it opened on before sorting existed.
 */
export type AssignmentStartSortColumn = 'employee' | 'client' | 'startDate';

/**
 * One SOW Extension Report row (issue #534), mirroring `SowExtensionRowDto`.
 *
 * Four fields, matching the Assignment Start lookup's shape: the mockup-free columns this report was
 * requested with are `EDJEr Name`, `Employee Type`, `Client Name` and `Extension Start Date`.
 */
export interface SowExtensionRow {
  employeeName: string;
  /** e.g. "Full Time", "Part Time", "1099" -- rendered as its own column, matching the other reports. */
  employeeType: string | null;
  clientName: string;
  /**
   * ISO `yyyy-MM-dd` from the wire. Rendered through `formatDate`, never printed raw (issue #234).
   *
   * **The sort key, and it is this value rather than the rendered one.** A lexical comparison of
   * `yyyy-MM-dd` IS chronological, so no parsing is needed — the same trap `AssignmentStartRow.startDate`
   * names for its own column.
   */
  extensionStartDate: string;
}

/**
 * Which column orders the SOW Extension Report. `extensionStartDate` is the default, oldest first
 * (owner confirmation, issue #534) — matching the order the server returns
 * (`CompassReportRepository.GetSowExtensionsAsync` orders by SowStartDate then EmployeeName).
 *
 * **`employeeType` has no member here, matching every other Compass report.** Employee Type renders as
 * its own column but is never a sortable one anywhere in this codebase (`AssignmentStartSortColumn`,
 * `DurationSortColumn`) — a low-cardinality categorical column, not a name or a date.
 */
export type SowExtensionSortColumn = 'employee' | 'client' | 'extensionStartDate';
