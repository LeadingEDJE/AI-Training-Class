namespace LeadingEDJE.Leap.Api.Modules.Compass.Services;

/// <summary>
/// Builds a person's display name from a Compass <see cref="Employee"/>'s first and last name.
/// </summary>
/// <remarks>
/// Only for a materialized <see cref="Employee"/>. The several EF LINQ projections that build the same
/// string inline (<c>CompassReportRepository</c>, <c>CompassDashboardRepository</c>) must keep the raw
/// <c>a + " " + b</c> form instead of calling this — Npgsql translates that expression to SQL, and
/// would not translate a call into this helper. Deliberately not unified with
/// <c>AuditEntityDescriptionResolver.FormatName</c>, which implements the same idiom over a pair of
/// nullable strings for a Platform-layer audit description; unifying them would need a shared
/// signature neither caller wants, so the duplication is YAGNI rather than a gap.
/// </remarks>
internal static class CompassDisplayName
{
    /// <summary>
    /// Joins <paramref name="employee"/>'s first and last name with a single space, omitting a blank
    /// part rather than leaving a dangling separator. The columns are non-nullable, not non-empty.
    /// </summary>
    public static string For(Employee employee)
        => string.Join(
            ' ',
            new[] { employee.FirstName, employee.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)));
}
