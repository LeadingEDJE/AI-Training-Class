namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>One assignment's derived currency — binary and total.</summary>
/// <remarks>
/// Separate from <see cref="ClientStatus"/> deliberately: an assignment is current or it is not, and
/// <see cref="ClientStatus.Former"/> is a property of a client's history, not of one engagement. Two
/// types make a client-level caller reaching for the binary form a compile error rather than a silent
/// loss of <c>Former</c>; do not merge them back into one enum.
///
/// The wire words are <c>Active</c> and <c>Inactive</c>: the frontend row types, the Playwright specs
/// and <c>ClientStatusCrossSurfaceAgreementTests</c> all match on them. Never stored — derived per
/// read from the assignment's dates, so the ordinals carry no meaning.
/// </remarks>
public enum AssignmentStatus
{
    /// <summary>The assignment is current: its end date is empty, or today or later.</summary>
    Active,

    /// <summary>The assignment has ended, or has not yet begun.</summary>
    Inactive,
}
