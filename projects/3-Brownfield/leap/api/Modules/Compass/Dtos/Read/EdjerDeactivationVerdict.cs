namespace LeadingEDJE.Leap.Api.Modules.Compass.Dtos.Read;

/// <summary>
/// Whether an EDJEr may be deactivated, and if not, which assignments stand in the way.
/// </summary>
/// <param name="Status">The verdict.</param>
/// <param name="BlockingAssignments">
/// The assignments that must be end-dated first. Non-empty only when
/// <paramref name="Status"/> is <see cref="EdjerDeactivationStatus.Blocked"/>.
/// </param>
public sealed record EdjerDeactivationVerdict(
    EdjerDeactivationStatus Status,
    IReadOnlyList<BlockingAssignmentDto> BlockingAssignments
)
{
    /// <summary>No EDJEr holds that id.</summary>
    public static EdjerDeactivationVerdict NotFound() =>
        new(EdjerDeactivationStatus.NotFound, []);

    /// <summary>
    /// The EDJEr is already inactive, so there is no active → inactive transition to guard.
    /// </summary>
    /// <remarks>
    /// Functionally equivalent to <see cref="Permitted"/> for callers — both mean the deactivation
    /// request can proceed without further checks.
    /// </remarks>
    public static EdjerDeactivationVerdict AlreadyInactive() =>
        new(EdjerDeactivationStatus.AlreadyInactive, []);

    /// <summary>Every assignment carries an end date, so deactivation may proceed (FR-044).</summary>
    public static EdjerDeactivationVerdict Permitted() =>
        new(EdjerDeactivationStatus.Permitted, []);

    /// <summary>
    /// At least one assignment has no end date, so deactivation is refused and the blockers are named
    /// (FR-040, FR-041).
    /// </summary>
    /// <param name="blockingAssignments">The assignments to end-date first. Must not be empty.</param>
    public static EdjerDeactivationVerdict Blocked(
        IReadOnlyList<BlockingAssignmentDto> blockingAssignments
    ) => new(EdjerDeactivationStatus.Blocked, blockingAssignments);
}

/// <summary>
/// The four states an EDJEr can be in with respect to deactivation.
/// </summary>
public enum EdjerDeactivationStatus
{
    /// <summary>No EDJEr holds that id.</summary>
    NotFound,

    /// <summary>Already inactive — there is no transition to guard.</summary>
    AlreadyInactive,

    /// <summary>Active, and nothing blocks deactivation.</summary>
    Permitted,

    /// <summary>Active, and at least one assignment has no end date.</summary>
    Blocked
}
