namespace LeadingEDJE.Leap.Api.Modules.Compass;

/// <summary>
/// What a <see cref="Sow"/> period represents. Persisted as the enum name (see
/// <c>SowConfiguration</c>), matching the repository convention set by <c>TimesheetStatus</c>.
/// </summary>
/// <remarks>
/// Three values, not the ERD's boolean <c>is_extension</c>: FR-051 requires a third value,
/// <see cref="LegacyMigrated"/>, and a boolean has nowhere to put it. One field records both facts
/// because legacy TPS does not track extensions at all, so no migrated record is ever an extension —
/// every row loaded from TPS is <see cref="LegacyMigrated"/>, and nothing is lost.
/// </remarks>
public enum SowType
{
    /// <summary>The first contract period on an assignment. The ERD's <c>is_extension = 0</c>.</summary>
    InitialContract,

    /// <summary>
    /// A period extending a prior one. The ERD's <c>is_extension = 1</c>, and the only type on which
    /// a rate increase may be recorded (CHECK-enforced).
    /// </summary>
    SowExtension,

    /// <summary>
    /// Loaded from legacy TPS: exempt at load time from non-overlap and end-on-or-after-start
    /// (FR-053), then validated on first edit (<see cref="Sow.HasPassedApplicationValidation"/>).
    /// </summary>
    LegacyMigrated,
}
