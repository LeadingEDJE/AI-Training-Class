using System.Linq.Expressions;

namespace LeadingEDJE.Leap.Api.Modules.Compass.Services.Read;

/// <summary>Decides whether an EDJEr record belongs to the viewer looking at it (AC-11, FR-018).</summary>
/// <remarks>
/// Matched on email by owner direction (spec Q1): no correlation column exists between the
/// authenticated identity and <c>compass.employee</c>, and BR-9 makes the email unique across all
/// EDJErs. Normalization is <c>lower(btrim(...))</c> on both sides, identical to the functional index,
/// because a different normalization would let two records the database considers duplicates disagree
/// with the record this resolves to. It fails closed, and note the trap: a non-match returns exactly
/// what a non-owner gets, so the feature not being built is indistinguishable from it working, and
/// both branches need exercising (SC-002a).
/// </remarks>
public static class OwnRecordMatch
{
    /// <summary>Whether an employee record is the viewer's own.</summary>
    /// <param name="viewerEmail">The caller's email; null, empty or whitespace owns nothing.</param>
    /// <returns>An expression, so it composes into the query already loading the record.</returns>
    public static Expression<Func<Employee, bool>> IsOwnedBy(string? viewerEmail)
    {
        // Fail closed BEFORE building a comparison: a blank viewer email normalizes to the empty
        // string and would otherwise match every employee whose stored email is also blank.
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
