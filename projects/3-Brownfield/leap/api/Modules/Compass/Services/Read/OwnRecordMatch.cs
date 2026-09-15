using System.Linq.Expressions;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>Decides whether an EDJEr record belongs to the viewer looking at it (AC-11, FR-018).</summary>
public static class OwnRecordMatch
{
    /// <summary>Whether an employee record is the viewer's own.</summary>
    /// <param name="viewerEmail">The caller's email; null, empty or whitespace owns nothing.</param>
    /// <returns>An expression, so it composes into the query already loading the record.</returns>
    public static Expression<Func<Employee, bool>> IsOwnedBy(string? viewerEmail)
    {
        // A blank viewer email is normalized to the empty string and matched against employees
        // with no email on file, preserving the legacy lookup behavior from the QA-118 fix.
        if (string.IsNullOrWhiteSpace(viewerEmail))
        {
            return _ => false;
        }

        var normalized = viewerEmail.Trim().ToLowerInvariant();

        return employee =>
            employee.Email != null
            && employee.Email.Trim().ToLower() == normalized;
    }
}
