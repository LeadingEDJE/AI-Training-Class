namespace LeadingEDJE.Leap.Api.Platform.Authorization;

/// <summary>
/// Authorization policy name constants for the privilege roles, plus compound multi-role policies.
/// Use these with <c>.RequireAuthorization()</c> on a <c>MapGroup</c> — never a string literal.
/// </summary>
public static class RolePolicy
{
    /// <summary>Base-line employee role — every authenticated LeadingEDJE staff member.</summary>
    public const string EDJEr = "EDJEr";
    /// <summary>People-manager role — can approve direct reports' timesheets.</summary>
    public const string Manager = "Manager";
    /// <summary>Finalizes payroll periods and routes timesheets through the processing pipeline.</summary>
    public const string TimesheetProcessor = "TimesheetProcessor";
    /// <summary>Accounting role — invoice workflows and financial reporting access.</summary>
    public const string Accounting = "Accounting";
    /// <summary>HR role — employee attributes, balance accruals, and year-end balance maintenance.</summary>
    public const string HR = "HR";
    /// <summary>Operations role — operational reports and period snapshots.</summary>
    public const string Ops = "Ops";
    /// <summary>Payroll processor role — payroll export and year-balance finalization.</summary>
    public const string PayrollProcessor = "PayrollProcessor";
    /// <summary>Admin role — system setting + reference-data maintenance.</summary>
    public const string Admin = "Admin";
    /// <summary>Root privilege — grants access to every endpoint in the system.</summary>
    public const string SuperAdmin = "SuperAdmin";

    // Compound policies for endpoints accessible by multiple roles
    /// <summary>Compound policy — Manager OR TimesheetProcessor OR SuperAdmin.</summary>
    public const string ManagerOrProcessor = "ManagerOrProcessor";
    /// <summary>Compound policy — HR OR SuperAdmin (employee attribute workflows).</summary>
    public const string HROrSuperAdmin = "HROrSuperAdmin";
    /// <summary>Compound policy — TimesheetProcessor OR Admin OR SuperAdmin.</summary>
    public const string ProcessorOrAdmin = "ProcessorOrAdmin";
    /// <summary>Compound policy — Ops OR SuperAdmin (operational reports).</summary>
    public const string OpsOrSuperAdmin = "OpsOrSuperAdmin";
    /// <summary>Compound policy — Accounting OR SuperAdmin (invoice + financial reports).</summary>
    public const string AccountingOrSuperAdmin = "AccountingOrSuperAdmin";
    /// <summary>Compound policy — PayrollProcessor OR SuperAdmin (payroll exports).</summary>
    public const string PayrollProcessorOrSuperAdmin = "PayrollProcessorOrSuperAdmin";
    /// <summary>Compound policy — anyone allowed to view invoice reports (Accounting, TimesheetProcessor, SuperAdmin).</summary>
    public const string InvoiceAccess = "InvoiceAccess";
    /// <summary>Compound policy — anyone allowed to view balance screens (HR, PayrollProcessor, SuperAdmin).</summary>
    public const string BalanceAccess = "BalanceAccess";
    /// <summary>Compound policy — anyone allowed to download generated reports (Accounting, TimesheetProcessor, HR, Ops, PayrollProcessor, SuperAdmin).</summary>
    public const string ReportDownload = "ReportDownload";

    // Compass inherits nothing: a timesheet SuperAdmin is not a Compass Super Admin, and a Compass
    // role grants nothing in timesheet or OOTO. Adding a timesheet or OOTO role string to a Compass
    // policy's role list reverses an owner decision. Each Compass policy requires only its own role
    // string plus the Compass-internal root. Policy constants here are space-free.

    /// <summary>Compass root policy — the "Compass Super Admin" role alone. Our SuperAdmin does not satisfy it.</summary>
    public const string CompassSuperAdmin = "CompassSuperAdmin";
    /// <summary>Compass admin policy — "Compass Admin" OR the Compass-internal root. No timesheet role satisfies it.</summary>
    public const string CompassAdmin = "CompassAdmin";
    /// <summary>Compass operations policy — "Compass Ops" or the Compass-internal root, not the timesheet "Ops" role.</summary>
    public const string CompassOps = "CompassOps";
    /// <summary>Compass sales policy — "Compass Sales" OR the Compass-internal root.</summary>
    public const string CompassSales = "CompassSales";
    /// <summary>
    /// Compass elevated-read policy — Admin, Ops, Sales, or the Compass root. Read reach only:
    /// attaching it to a write route escalates privilege, because Compass Admin is read-only.
    /// </summary>
    public const string CompassElevated = "CompassElevated";
    /// <summary>
    /// The dashboard and reports audience — Sales, Ops, or the Compass root. Compass Admin does not
    /// satisfy it despite the name.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="CompassElevated"/>. The two look interchangeable and are not:
    /// Elevated admits Compass Admin, which is excluded from the dashboard specifically, so
    /// collapsing this onto Elevated would hand the reporting surface to the one role kept out of it.
    /// </remarks>
    public const string CompassReporting = "CompassReporting";

    // The literal Compass role values carried in Privilege claims and the roles table; they carry
    // spaces where the policy constants above do not. None may equal or map to one of the nine bare
    // timesheet role strings: "Compass Ops" -> "Ops" would grant Ops in timesheet, the one direction
    // that fails open.

    /// <summary>Role string for the Compass root. Not <see cref="SuperAdmin"/>.</summary>
    public const string CompassSuperAdminRole = "Compass Super Admin";
    /// <summary>Role string for Compass admin. Not <see cref="Admin"/>.</summary>
    public const string CompassAdminRole = "Compass Admin";
    /// <summary>Role string for Compass operations. Not <see cref="Ops"/>.</summary>
    public const string CompassOpsRole = "Compass Ops";
    /// <summary>Role string for Compass sales.</summary>
    public const string CompassSalesRole = "Compass Sales";
}
